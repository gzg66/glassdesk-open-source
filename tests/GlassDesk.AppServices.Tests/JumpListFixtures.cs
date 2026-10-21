using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace GlassDesk.AppServices.Tests;

/// <summary>
/// 跳转列表文件的测试样本构造器。
/// </summary>
/// <remarks>
/// 只在测试里用：真实文件含用户路径，不能进仓库，也不能让测试依赖某台机器的
/// <c>CustomDestinations</c> 目录。
/// </remarks>
internal static class JumpListFixtures
{
    /// <summary><c>FMTID_SummaryInformation</c>：标题所在的存储格式。</summary>
    private static readonly byte[] SummaryInformationFormatId =
    [
        0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10,
        0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9,
    ];

    /// <summary>拼一个 <c>&lt;长度&gt;1SPS&lt;fmtid&gt;&lt;body&gt;</c> 的标题存储块。</summary>
    internal static byte[] BuildTitleStore(string title, bool terminate = true)
    {
        var body = new List<byte>();
        body.AddRange([0x1F, 0x00, 0x00, 0x00]);
        WriteUInt32(body, (uint)(title.Length + (terminate ? 1 : 0)));
        body.AddRange(Encoding.Unicode.GetBytes(title + (terminate ? "\0" : string.Empty)));

        var store = new List<byte>();
        WriteUInt32(store, (uint)(4 + 4 + 16 + body.Count));
        store.AddRange("1SPS"u8.ToArray());
        store.AddRange(SummaryInformationFormatId);
        store.AddRange(body);

        return [.. store];
    }

    internal static byte[] Concat(params byte[][] parts)
    {
        var buffer = new List<byte>();
        foreach (var part in parts) buffer.AddRange(part);
        return [.. buffer];
    }

    /// <summary>把字节写进一个临时文件，返回路径；调用方负责删。</summary>
    internal static string WriteTempFile(byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>造一个真实 <c>.lnk</c> 的字节（<c>.lnk</c> 内容就是序列化的 shell link）。</summary>
    internal static byte[] BuildLinkBytes(string targetPath, string arguments, string workingDirectory)
    {
        var shortcutPath = Path.GetTempFileName();
        try
        {
            var written = GlassDesk.Platform.Shell.ShortcutWriter.Write(
                shortcutPath,
                new GlassDesk.Platform.Shell.ShortcutDefinition(
                    TargetPath: targetPath,
                    Arguments: arguments,
                    WorkingDirectory: workingDirectory));

            if (!written) Assert.Fail("造不出 .lnk 测试样本（ShortcutWriter.Write 返回 false）");

            return File.ReadAllBytes(shortcutPath);
        }
        finally
        {
            File.Delete(shortcutPath);
        }
    }

    /// <summary>每个 item 在 link 之前还有一个 16 字节的 CLSID 前缀。</summary>
    internal static readonly byte[] ItemPrefix =
    [
        0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    ];

    /// <summary>拼一个完整的跳转列表文件：文件头 + 一个分类 + 若干 item。</summary>
    /// <param name="categoryName">分类名；空串表示「任务」分类（Windows 里显示为「任务」）。</param>
    internal static byte[] BuildFile(string categoryName, params (byte[] Link, string Title)[] items) =>
        BuildFile((categoryName, items));

    /// <summary>拼一个多分类的跳转列表文件（前面的分类是「最近」之类的数据，最后一个才是任务）。</summary>
    internal static byte[] BuildFile(params (string CategoryName, (byte[] Link, string Title)[] Items)[] categories)
    {
        var parts = new List<byte[]>();

        var header = new List<byte>();
        WriteUInt32(header, 2); // 版本
        WriteUInt32(header, (uint)categories.Length);
        WriteUInt32(header, 0);
        WriteUInt32(header, 0);
        parts.Add([.. header]);

        foreach (var category in categories)
        {
            var bytes = new List<byte>();
            if (category.CategoryName.Length == 0)
            {
                WriteUInt32(bytes, (uint)category.Items.Length);
            }
            else
            {
                WriteUInt16(bytes, (ushort)category.CategoryName.Length);
                bytes.AddRange(Encoding.Unicode.GetBytes(category.CategoryName));
                WriteUInt32(bytes, (uint)category.Items.Length);
            }

            parts.Add([.. bytes]);

            foreach (var item in category.Items)
            {
                parts.Add(ItemPrefix);
                parts.Add(item.Link);
                parts.Add(BuildTitleStore(item.Title));
            }
        }

        return Concat([.. parts]);
    }

    /// <summary>拼「文件头 + 一个无名分类头（声明 <paramref name="itemCount"/> 个条目）」但**不包含** item。</summary>
    internal static byte[] BuildCategoryHeaderOnly(int itemCount)
    {
        var header = new List<byte>();
        WriteUInt32(header, 2);
        WriteUInt32(header, 1);
        WriteUInt32(header, 0);
        WriteUInt32(header, 0);
        WriteUInt32(header, (uint)itemCount);
        return [.. header];
    }

    private static void WriteUInt16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)value);
        buffer.Add((byte)(value >> 8));
    }

    private static void WriteUInt32(List<byte> buffer, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        buffer.AddRange(bytes.ToArray());
    }
}
