using GlassDesk.Platform.Shell;
using Xunit;

namespace GlassDesk.AppServices.Tests;

/// <summary>
/// 从跳转列表文件里读出任务：link 的字段交给系统 <c>IShellLink</c>，标题来自存储块。
/// </summary>
public sealed class JumpListTaskReaderTests
{
    private const string TargetPath = @"C:\不存在\测试程序.exe";
    private const string WorkingDirectory = @"C:\不存在";

    [Fact]
    public void ReadFile_ReturnsEmptyForMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".customDestinations-ms");

        Assert.Empty(JumpListTaskReader.ReadFile(missing));
    }

    [Fact]
    public void ReadFile_ReturnsEmptyForGarbageContent()
    {
        var path = JumpListFixtures.WriteTempFile([0x00, 0xFF, 0x10, 0x4C, 0x00, 0x00, 0x00, 0x99]);
        try
        {
            Assert.Empty(JumpListTaskReader.ReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_ReadsTargetArgumentsWorkingDirectoryAndTitle()
    {
        var link = JumpListFixtures.BuildLinkBytes(TargetPath, "--inprivate", WorkingDirectory);
        var path = JumpListFixtures.WriteTempFile(
            JumpListFixtures.BuildFile("", (link, "新建无痕窗口(I)")));

        try
        {
            var tasks = JumpListTaskReader.ReadFile(path);

            var task = Assert.Single(tasks);
            Assert.Equal("新建无痕窗口(I)", task.Title);
            Assert.Equal("--inprivate", task.Arguments);
            Assert.Equal(WorkingDirectory, task.WorkingDirectory);
            Assert.Equal("测试程序.exe", Path.GetFileName(task.ExecutablePath));
            Assert.Equal(File.GetLastWriteTimeUtc(path), task.SourceFileWriteTimeUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_KeepsFileOrderForMultipleLinks()
    {
        var first = JumpListFixtures.BuildLinkBytes(TargetPath, "--first", WorkingDirectory);
        var second = JumpListFixtures.BuildLinkBytes(TargetPath, "--second", WorkingDirectory);
        var path = JumpListFixtures.WriteTempFile(
            JumpListFixtures.BuildFile("", (first, "第一个"), (second, "第二个")));

        try
        {
            var tasks = JumpListTaskReader.ReadFile(path);

            Assert.Equal(2, tasks.Count);
            Assert.Equal("第一个", tasks[0].Title);
            Assert.Equal("--first", tasks[0].Arguments);
            Assert.Equal("第二个", tasks[1].Title);
            Assert.Equal("--second", tasks[1].Arguments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_SkipsLinkWithoutTitle()
    {
        var link = JumpListFixtures.BuildLinkBytes(TargetPath, "--orphan", WorkingDirectory);

        // 分类头声称有一个条目，但那个 item 只有 link、没有标题存储块。
        var content = JumpListFixtures.Concat(
            JumpListFixtures.BuildCategoryHeaderOnly(1),
            JumpListFixtures.ItemPrefix,
            link,
            [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]);
        var path = JumpListFixtures.WriteTempFile(content);

        try
        {
            Assert.Empty(JumpListTaskReader.ReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_ToleratesTruncatedLinkWithoutInventingExecutablePath()
    {
        var link = JumpListFixtures.BuildLinkBytes(TargetPath, "--truncated", WorkingDirectory);
        var truncated = link[..(link.Length / 2)];
        var path = JumpListFixtures.WriteTempFile(
            JumpListFixtures.BuildFile("", (truncated, "截断")));

        try
        {
            // 实测：系统 <c>IShellLink</c> 对截断的 link 是宽容的——Load 成功，
            // GetPath 给出被截断的路径（`C:\不存在`，没有 .exe 后缀）。
            // 这里要保证的是「不抛异常、不凭空造出一个可执行文件路径」；
            // 按后缀过滤可执行文件的职责在 JumpListIndex，不在这里重复。
            var tasks = JumpListTaskReader.ReadFile(path);

            Assert.DoesNotContain(tasks, task => task.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
