using System.Buffers.Binary;
using System.Text;
using GlassDesk.Platform.Shell;
using Xunit;

namespace GlassDesk.AppServices.Tests;

/// <summary>
/// 跳转列表文件的纯字节解析：link 签名定位与标题取值的边界情况。
/// </summary>
/// <remarks>
/// 样本全部在内存里拼出来，不读用户目录：真实文件含用户路径，不能进仓库，
/// 也不能让 CI 依赖某台机器的 `CustomDestinations`。
/// </remarks>
public sealed class JumpListFormatTests
{
    /// <summary>shell link 头里的 CLSID：<c>{00021401-0000-0000-C000-000000000046}</c>。</summary>
    private static readonly byte[] LinkClassId =
    [
        0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    ];

    [Fact]
    public void FindLinkOffsets_ReturnsBothLinkStarts()
    {
        var link = BuildLink("--inprivate");
        var store = JumpListFixtures.BuildTitleStore("新建窗口(N)");
        var file = JumpListFixtures.Concat(link, store, link);

        var offsets = JumpListFormat.FindLinkOffsets(file);

        Assert.Equal(2, offsets.Count);
        Assert.Equal(0, offsets[0]);
        Assert.Equal(link.Length + store.Length, offsets[1]);
    }

    [Fact]
    public void TryReadTitle_ReadsChineseTitle()
    {
        var region = JumpListFixtures.Concat(BuildLink("--inprivate"), JumpListFixtures.BuildTitleStore("新建窗口(N)"));

        Assert.Equal("新建窗口(N)", JumpListFormat.TryReadTitle(region));
    }

    [Fact]
    public void TryReadTitle_IgnoresBinaryJunkThatLooksLikeTitleAnchor()
    {
        var junk = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(junk.AsSpan(8), 0x1F);
        BinaryPrimitives.WriteUInt32LittleEndian(junk.AsSpan(12), 0x1F0000);
        BinaryPrimitives.WriteUInt32LittleEndian(junk.AsSpan(16), 3);
        junk[20] = 0xFF;
        junk[21] = 0xFE;

        var region = JumpListFixtures.Concat(junk, JumpListFixtures.BuildTitleStore("新建无痕窗口(I)"));

        Assert.Equal("新建无痕窗口(I)", JumpListFormat.TryReadTitle(region));
    }

    [Fact]
    public void TryReadTitle_SkipsStoreWhoseLengthRunsPastRegion()
    {
        var lyingStore = JumpListFixtures.BuildTitleStore("假标题");
        BinaryPrimitives.WriteUInt32LittleEndian(lyingStore, 0x4000);

        var region = JumpListFixtures.Concat(lyingStore, JumpListFixtures.BuildTitleStore("真标题"));

        Assert.Equal("真标题", JumpListFormat.TryReadTitle(region));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullWhenTitleHasNoTerminator()
    {
        var store = JumpListFixtures.BuildTitleStore("缺结尾", terminate: false);

        Assert.Null(JumpListFormat.TryReadTitle(store));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullOnAbsurdCharacterCount()
    {
        var store = JumpListFixtures.BuildTitleStore("正常标题");
        // 把字符数改成 0xFFFFFF：越过区域末尾，必须被边界判断挡下而不是抛异常。
        var countOffset = store.Length - Encoding.Unicode.GetByteCount("正常标题") - 2 - 4;
        BinaryPrimitives.WriteUInt32LittleEndian(store.AsSpan(countOffset), 0xFFFFFF);

        Assert.Null(JumpListFormat.TryReadTitle(store));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullWhenStoreLengthExceedsRegion()
    {
        var store = JumpListFixtures.BuildTitleStore("越界标题");
        BinaryPrimitives.WriteUInt32LittleEndian(store, (uint)store.Length + 16);

        Assert.Null(JumpListFormat.TryReadTitle(store));
    }

    [Fact]
    public void TryReadTitle_ReturnsNullOnEmptyRegion()
    {
        Assert.Null(JumpListFormat.TryReadTitle(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void FindTaskLinkOffsets_ReturnsOnlyItemsOfUnnamedCategories()
    {
        var first = JumpListFixtures.BuildLinkBytes(@"C:\不存在\测试程序.exe", "--recent-1", string.Empty);
        var second = JumpListFixtures.BuildLinkBytes(@"C:\不存在\测试程序.exe", "--recent-2", string.Empty);
        var task = JumpListFixtures.BuildLinkBytes(@"C:\不存在\测试程序.exe", "--task", string.Empty);

        var file = JumpListFixtures.BuildFile(
            ("最近关闭", [(first, "网页一"), (second, "网页二")]),
            (string.Empty, [(task, "新建窗口(N)")]));

        var links = JumpListFormat.FindLinkOffsets(file);

        Assert.Equal(3, links.Count);
        Assert.Equal([links[2]], JumpListFormat.FindTaskLinkOffsets(file));
    }

    /// <summary>拼一个最小 MS-SHLLINK：只有参数串，没有 IDList/LinkInfo。</summary>
    private static byte[] BuildLink(string arguments)
    {
        var buffer = new List<byte>(0x4C + 8 + arguments.Length * 2 + 4);
        WriteUInt32(buffer, 0x4C);
        buffer.AddRange(LinkClassId);
        WriteUInt32(buffer, 0x00000020); // LinkFlags: HasArguments
        WriteUInt32(buffer, 0);          // FileAttributes
        WriteUInt64(buffer, 0);          // CreationTime
        WriteUInt64(buffer, 0);          // AccessTime
        WriteUInt64(buffer, 0);          // WriteTime
        WriteUInt32(buffer, 0);          // FileSize
        WriteUInt32(buffer, 0);          // IconIndex
        WriteUInt32(buffer, 0);          // ShowCommand
        buffer.AddRange([0, 0]);         // HotKey
        buffer.AddRange([0, 0]);         // Reserved1
        WriteUInt32(buffer, 0);          // Reserved2
        WriteUInt32(buffer, 0);          // Reserved3
        WriteUInt16(buffer, (ushort)arguments.Length);
        buffer.AddRange(Encoding.Unicode.GetBytes(arguments));
        WriteUInt32(buffer, 0);          // TerminalBlock

        return [.. buffer];
    }

    private static void WriteUInt16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)value);
        buffer.Add((byte)(value >> 8));
    }

    private static void WriteUInt32(List<byte> buffer, uint value)
    {
        buffer.Add((byte)value);
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value >> 16));
        buffer.Add((byte)(value >> 24));
    }

    private static void WriteUInt64(List<byte> buffer, ulong value)
    {
        WriteUInt32(buffer, (uint)value);
        WriteUInt32(buffer, (uint)(value >> 32));
    }
}
