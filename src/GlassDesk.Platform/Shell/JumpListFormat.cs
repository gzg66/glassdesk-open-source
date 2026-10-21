using System.Buffers.Binary;
using System.Text;

namespace GlassDesk.Platform.Shell;

/// <summary>
/// 跳转列表文件（<c>*.customDestinations-ms</c>）的纯字节解析。
/// </summary>
/// <remarks>
/// 这个格式没有公开文档，结构是这样（实测 44 个真实文件得出）：
/// 文件里逐项排列，每一项是<strong>16 字节 ShellLink CLSID</strong> + <strong>一个 shell link</strong> 后面跟若干
/// <strong>属性存储块</strong>，存储块形如
/// <c>&lt;uint32 总长&gt; "1SPS" &lt;16 字节 fmtid&gt; &lt;body&gt;</c>，
/// 任务标题只在 <c>fmtid == FMTID_SummaryInformation</c> 那一块里，
/// body 内是 <c>1F 00 00 00</c> + <c>uint32 字符数</c> + UTF-16LE 文本。
///
/// <para>
/// <b>两条已实测的死路，别再试：</b>
/// <list type="number">
/// <item>
/// 用 propsys 的 <c>PSGetPropertyFromPropertyStorage</c> 读这些存储块——返回值
/// <c>0x8007000D</c>（ERROR_INVALID_DATA），把长度从 24 到 120 字节逐个试也不成立。
/// 跳转列表的存储块不是 MS-PROPSTORE 序列化属性存储，只是套了同样的签名。
/// </item>
/// <item>
/// 在整段二进制里找「第一段像样的 UTF-16 字符串」——实测会把二进制垃圾当标题
/// （Edge、PowerShell ISE、VS Code 的文件都出现过）。必须先按存储块边界与 fmtid
/// 定位，再在块内按锚点取。
/// </item>
/// </list>
/// </para>
///
/// <para>
/// 本类型<strong>只读 span、只做边界判断</strong>：解析不到就返回 <c>null</c>，
/// 不抛异常。调用方据此跳过该条目——一个坏条目不该让整个菜单打不开。
/// </para>
/// </remarks>
internal static class JumpListFormat
{
    /// <summary>一个 shell link 签名的长度：4 字节头长度 + 16 字节 CLSID。</summary>
    internal const int LinkSignatureLength = 20;

    /// <summary>每个 item 在 link 之前还有一个 16 字节的 CLSID 前缀（实测）。</summary>
    private const int ItemPrefixLength = 16;

    /// <summary>文件头：版本 4 + 分类数 4 + 8 字节保留。</summary>
    private const int FileHeaderLength = 16;

    /// <summary>跳转列表文件格式版本；实测只有 2。</summary>
    private const int SupportedVersion = 2;

    /// <summary>分类数上限：一份文件里最多几十个分类，超过就当文件坏了。</summary>
    private const int MaxCategoryCount = 64;

    /// <summary>单个分类的条目数上限。</summary>
    private const int MaxCategoryItemCount = 256;

    /// <summary>分类名最大字符数。</summary>
    private const int MaxCategoryNameLength = 64;

    /// <summary>shell link 头声明的长度（<c>0x4C</c>）。</summary>
    private const uint LinkHeaderSize = 0x4C;

    /// <summary>存储块固定开销：长度 4 + 签名 4 + fmtid 16。</summary>
    private const int StoreHeaderLength = 24;

    /// <summary>存储块长度上限。实测最大的块远小于此，超过就当不是块。</summary>
    private const int MaxStoreLength = 8192;

    /// <summary>标题字符数上限（含结尾 NUL）。</summary>
    private const int MaxTitleCharCount = 512;

    /// <summary>shell link 头的 CLSID 尾巴：<c>{00021401-0000-0000-C000-000000000046}</c>。</summary>
    private static ReadOnlySpan<byte> LinkClassId =>
    [
        0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    ];

    /// <summary>存储块签名 <c>"1SPS"</c>。</summary>
    private static ReadOnlySpan<byte> StoreSignature => "1SPS"u8;

    /// <summary><c>FMTID_SummaryInformation</c>：标题所在存储块的格式 id。</summary>
    private static ReadOnlySpan<byte> SummaryInformationFormatId =>
    [
        0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10,
        0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9,
    ];

    /// <summary>标题取值前面的锚点：<c>VT_LPWSTR</c> 标记加两字节保留。</summary>
    private static ReadOnlySpan<byte> TitleValueAnchor => [0x1F, 0x00, 0x00, 0x00];

    /// <summary>
    /// 找出文件里每个 shell link 头的偏移（升序）。
    /// </summary>
    /// <remarks>
    /// 只按签名匹配，不遍历 link 结构：link 的长度由调用方用「下一个签名」界定。
    /// 目标、参数、工作目录一律交给系统 <c>IShellLink</c> 读，不在这里解
    /// MS-SHLLINK 的编码分支（ANSI/Unicode、相对路径、环境变量、IDList）。
    /// </remarks>
    internal static IReadOnlyList<int> FindLinkOffsets(ReadOnlySpan<byte> file)
    {
        var offsets = new List<int>();
        if (file.Length < LinkSignatureLength) return offsets;

        for (var offset = 0; offset <= file.Length - LinkSignatureLength; offset++)
        {
            if (!IsLinkAt(file, offset)) continue;
            offsets.Add(offset);
            offset += LinkSignatureLength - 1;
        }

        return offsets;
    }

    /// <summary>
    /// 在一个 item 区域里取任务标题；取不到返回 <c>null</c>。
    /// </summary>
    /// <param name="itemRegion">
    /// 该 item 的区域：本 item 的 link 起点到下一个 link 起点（最后一个到文件末尾）。
    /// 区域里除了存储块还夹着 link 自身的字节，所以必须先按存储块定位再取标题。
    /// </param>
    internal static string? TryReadTitle(ReadOnlySpan<byte> itemRegion)
    {
        if (itemRegion.Length < StoreHeaderLength) return null;

        var offset = 0;
        while (offset <= itemRegion.Length - StoreHeaderLength)
        {
            if (!IsTitleStoreAt(itemRegion, offset, out var storeLength))
            {
                offset++;
                continue;
            }

            var body = itemRegion.Slice(offset + StoreHeaderLength, storeLength - StoreHeaderLength);
            var title = ReadTitleValue(body);
            if (title is not null) return title;

            // 同一个 item 里出现第二个标题块时继续试，别在这里放弃。
            offset += storeLength;
        }

        return null;
    }

    /// <summary>
    /// 找出只属于「任务」分类的 link 偏移（升序）。
    /// </summary>
    /// <remarks>
    /// 跳转列表文件由若干<strong>分类</strong>组成：<c>常去网站</c>、<c>最近关闭</c> 这类是数据驱动
    /// 的「最近条目」，而<strong>没有名字</strong>的那个分类才是程序注册的任务（Windows 在开始菜单里
    /// 把它显示为「任务」）。实测：Chrome / Edge 的任务恰好是文件末尾两三条，VS Code 的任务在
    /// 最前面，Windows Terminal 整个文件只有一个无名分类。
    ///
    /// <para>
    /// <b>为什么必须按分类筛：</b>不筛的话，右键 Chrome 会得到四十条 puppeteer / Playwright
    /// 临时档案夹里的「最近关闭」，那是数据而不是任务。
    /// </para>
    ///
    /// <para>
    /// 一个分类读不出来时返回空列表（宁可不显示任务，也不把「最近」当成任务）。
    /// 条目在分类内的位置用「link 偏移减 16 = item 起点」映射：每个 item 都是
    /// <c>&lt;16 字节 CLSID&gt; &lt;link&gt; &lt;存储块…&gt;</c>，实测 44 个文件都如此。
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<int> FindTaskLinkOffsets(ReadOnlySpan<byte> file) =>
        FindTaskLinkOffsets(file, FindLinkOffsets(file));

    /// <summary>
    /// 同上，但复用调用方<strong>已经算过</strong>的 link 偏移，避免把整份文件扫两遍。
    /// </summary>
    internal static IReadOnlyList<int> FindTaskLinkOffsets(ReadOnlySpan<byte> file, IReadOnlyList<int> links)
    {
        if (links.Count == 0) return [];
        if (file.Length < FileHeaderLength) return [];
        if (BinaryPrimitives.ReadInt32LittleEndian(file) != SupportedVersion) return [];

        var categoryCount = BinaryPrimitives.ReadInt32LittleEndian(file[4..]);
        if (categoryCount <= 0 || categoryCount > MaxCategoryCount) return [];

        // 每个 item 的起点 = link 偏移 - 16；分类头正好结束在它后面第一个 item 的起点上。
        var itemOffsets = new int[links.Count];
        for (var index = 0; index < links.Count; index++)
        {
            var start = links[index] - ItemPrefixLength;
            if (start < FileHeaderLength) return [];
            itemOffsets[index] = start;
        }

        var tasks = new List<int>();
        var firstItem = 0;

        for (var category = 0; category < categoryCount && firstItem < links.Count; category++)
        {
            if (!TryReadCategoryHeaderEndingAt(file, itemOffsets[firstItem], out var isNamed, out var count))
            {
                return [];
            }

            if (count <= 0 || firstItem + count > links.Count) return [];

            if (!isNamed)
            {
                for (var offset = 0; offset < count; offset++) tasks.Add(links[firstItem + offset]);
            }

            firstItem += count;
        }

        return tasks;
    }

    /// <summary>
    /// 读一个「正好结束在 <paramref name="headerEnd"/>」的分类头。
    /// </summary>
    /// <remarks>
    /// 两种形状：有名字的 <c>&lt;u16 名字字符数&gt; &lt;名字 UTF-16&gt; &lt;u32 条目数&gt;</c>，
    /// 没名字的 <c>&lt;u32 条目数&gt;</c>。因为所有 item 的起点都能从 link 偏移推出来，
    /// 所以用「头必须在 item 起点上结束」当约束反向定位头的位置，不需要正向跳过 item 的
    /// 未知长度尾部。
    /// </remarks>
    private static bool TryReadCategoryHeaderEndingAt(
        ReadOnlySpan<byte> file,
        int headerEnd,
        out bool isNamed,
        out int itemCount)
    {
        isNamed = false;
        itemCount = 0;

        if (headerEnd <= FileHeaderLength || headerEnd > file.Length) return false;

        // 有名字：名字字符数 n 满足 headerEnd = start + 2 + 2n + 4。
        for (var nameLength = 1; nameLength <= MaxCategoryNameLength; nameLength++)
        {
            var start = headerEnd - 4 - 2 - (2 * nameLength);
            if (start < FileHeaderLength) break;
            if (BinaryPrimitives.ReadUInt16LittleEndian(file[start..]) != nameLength) continue;

            var name = file.Slice(start + 2, 2 * nameLength);
            if (!IsPlausibleCategoryName(name)) continue;

            var count = BinaryPrimitives.ReadInt32LittleEndian(file[(headerEnd - 4)..]);
            if (count is <= 0 or > MaxCategoryItemCount) continue;

            isNamed = true;
            itemCount = count;
            return true;
        }

        // 有名字的头读不懂时退到下面的「无名分类」分支。这是有意为之的取舍：
        // 两种头共用同一个条目数字段，所以「名字读不懂」与「本来就没有名字」在字节层面
        // 无法可靠区分——实测一份无名分类的前一个 item 尾巴里就有字节对得上某个名字长度，
        // 加上「读不懂就判死」的闸会把整份文件的**正常任务**一起丢掉。宁可在这种极罕见的
        // 情况下把「最近」分类的条目当任务显示，也不能让正常程序的任务消失。
        //
        // 没名字：4 字节条目数，直接接 item。
        if (headerEnd - 4 >= FileHeaderLength)
        {
            var unnamedCount = BinaryPrimitives.ReadInt32LittleEndian(file[(headerEnd - 4)..]);
            if (unnamedCount is > 0 and <= MaxCategoryItemCount)
            {
                itemCount = unnamedCount;
                return true;
            }
        }

        return false;
    }

    /// <summary>分类名必须是可显示文本（内部空名字会被上层当成「任务」分类）。</summary>
    private static bool IsPlausibleCategoryName(ReadOnlySpan<byte> name)
    {
        for (var index = 0; index + 1 < name.Length; index += 2)
        {
            var character = BinaryPrimitives.ReadUInt16LittleEndian(name[index..]);
            if (character < 0x20) return false;
            if (character is >= 0xE000 and <= 0xF8FF) return false;
        }

        return true;
    }

    private static bool IsLinkAt(ReadOnlySpan<byte> file, int offset)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(file[offset..]) != LinkHeaderSize) return false;

        return file.Slice(offset + 4, 16).SequenceEqual(LinkClassId);
    }

    private static bool IsTitleStoreAt(ReadOnlySpan<byte> region, int offset, out int storeLength)
    {
        storeLength = 0;
        if (offset + StoreHeaderLength > region.Length) return false;

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(region[offset..]);
        if (declaredLength < StoreHeaderLength || declaredLength > MaxStoreLength) return false;
        if ((long)offset + declaredLength > region.Length) return false;
        if (!region.Slice(offset + 4, 4).SequenceEqual(StoreSignature)) return false;
        if (!region.Slice(offset + 8, 16).SequenceEqual(SummaryInformationFormatId)) return false;

        storeLength = (int)declaredLength;
        return true;
    }

    private static string? ReadTitleValue(ReadOnlySpan<byte> body)
    {
        var searchFrom = 0;
        while (searchFrom <= body.Length - TitleValueAnchor.Length)
        {
            var anchor = body[searchFrom..].IndexOf(TitleValueAnchor);
            if (anchor < 0) return null;
            anchor += searchFrom;

            var countOffset = anchor + TitleValueAnchor.Length;
            if (countOffset + 4 <= body.Length)
            {
                var charCount = BinaryPrimitives.ReadUInt32LittleEndian(body[countOffset..]);
                if (charCount >= 2 && charCount <= MaxTitleCharCount)
                {
                    var textLength = (int)charCount * 2;
                    if ((long)countOffset + 4 + textLength <= body.Length)
                    {
                        var text = body.Slice(countOffset + 4, textLength);
                        if (BinaryPrimitives.ReadUInt16LittleEndian(text[^2..]) == 0)
                        {
                            var value = Encoding.Unicode.GetString(text[..^2]);
                            if (IsPlausibleTitle(value)) return value;
                        }
                    }
                }
            }

            searchFrom = anchor + 1;
        }

        return null;
    }

    /// <summary>
    /// 粗筛：标题必须是可显示文本。
    /// </summary>
    /// <remarks>
    /// 二进制垃圾恰好凑成「字符合法、结尾是 0」的概率不为零，实测抓到过。
    /// 这里挡掉控制字符与私用区字符——真实任务标题（含中文）不会被误杀。
    /// </remarks>
    private static bool IsPlausibleTitle(string value)
    {
        if (value.Length == 0) return false;

        foreach (var character in value)
        {
            if (character < 0x20) return false;
            if (character is >= '\uE000' and <= '\uF8FF') return false;
        }

        return true;
    }
}
