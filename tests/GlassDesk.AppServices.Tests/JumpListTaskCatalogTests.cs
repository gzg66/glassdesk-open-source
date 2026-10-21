using GlassDesk.Core.Models;
using GlassDesk.Platform.Shell;
using Xunit;

namespace GlassDesk.AppServices.Tests;

/// <summary>
/// 任务索引的缓存、失效与执行。
/// </summary>
/// <remarks>
/// 读文件与调外壳都通过内部接缝注入：测试不能真的去读用户的跳转列表，
/// 也不能真的拉起进程。
/// </remarks>
public sealed class JumpListTaskCatalogTests
{
    private const string EdgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    public JumpListTaskCatalogTests()
    {
        JumpListTaskCatalog.ResetForTests();
    }

    [Fact]
    public void TasksFor_BuildsOnFirstCall()
    {
        UseReader(_ => [Task("新建窗口(N)")]);

        var tasks = JumpListTaskCatalog.TasksFor(Entry());

        Assert.Single(tasks);
        Assert.Equal("新建窗口(N)", tasks[0].Title);
    }

    [Fact]
    public void TasksFor_ReadsOnceWhileCacheIsFresh()
    {
        var reads = 0;
        UseReader(_ =>
        {
            reads++;
            return [Task("新建窗口(N)")];
        });

        JumpListTaskCatalog.TasksFor(Entry());
        JumpListTaskCatalog.TasksFor(Entry());

        Assert.Equal(1, reads);
    }

    [Fact]
    public void TasksFor_RebuildsAfterTimeToLiveExpires()
    {
        var now = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
        var reads = 0;
        UseReader(_ =>
        {
            reads++;
            return [Task("新建窗口(N)")];
        });
        JumpListTaskCatalog.Clock = () => now;

        JumpListTaskCatalog.TasksFor(Entry());
        now = now.AddSeconds(31);
        JumpListTaskCatalog.TasksFor(Entry());

        Assert.Equal(2, reads);
    }

    [Fact]
    public void TasksFor_ReturnsEmptyAndCountsFailureWhenReaderThrows()
    {
        UseReader(_ => throw new IOException("坏文件"));

        var tasks = JumpListTaskCatalog.TasksFor(Entry());

        Assert.Empty(tasks);
        Assert.Equal(1, JumpListTaskCatalog.LastFailureCount);
    }

    [Fact]
    public void TasksFor_RecoversAfterFailureOnceTimeToLiveExpires()
    {
        var now = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
        var shouldThrow = true;
        UseReader(_ => shouldThrow ? throw new IOException("坏文件") : [Task("新建窗口(N)")]);
        JumpListTaskCatalog.Clock = () => now;

        Assert.Empty(JumpListTaskCatalog.TasksFor(Entry()));

        shouldThrow = false;
        now = now.AddSeconds(31);

        Assert.Single(JumpListTaskCatalog.TasksFor(Entry()));
    }

    [Fact]
    public void Launch_PassesTargetArgumentsAndWorkingDirectoryToShell()
    {
        string? target = null;
        string? arguments = null;
        string? workingDirectory = null;
        JumpListTaskCatalog.LaunchShell = (path, args, directory) =>
        {
            target = path;
            arguments = args;
            workingDirectory = directory;
            return (true, 0);
        };

        var result = JumpListTaskCatalog.Launch(
            new JumpListTask(EdgePath, "--inprivate", @"C:\不存在", "新建无痕窗口(I)", "src", DateTime.UtcNow));

        Assert.True(result.Started);
        Assert.Null(result.Message);
        Assert.Equal(EdgePath, target);
        Assert.Equal("--inprivate", arguments);
        Assert.Equal(@"C:\不存在", workingDirectory);
    }

    [Fact]
    public void Launch_ReportsShellErrorCode()
    {
        JumpListTaskCatalog.LaunchShell = (_, _, _) => (false, 5);

        var result = JumpListTaskCatalog.Launch(
            new JumpListTask(EdgePath, string.Empty, string.Empty, "新建窗口(N)", "src", DateTime.UtcNow));

        Assert.False(result.Started);
        Assert.Contains("5", result.Message, StringComparison.Ordinal);
    }

    private static void UseReader(Func<string, IReadOnlyList<JumpListTask>> reader)
    {
        JumpListTaskCatalog.ResetForTests();
        JumpListTaskCatalog.RecentFolder = () => Path.GetTempPath();
        JumpListTaskCatalog.Reader = reader;
    }

    private static JumpListTask Task(string title) =>
        new(EdgePath, "--edge-jumplist-new-window", string.Empty, title, "src", DateTime.UtcNow);

    private static AppEntry Entry() => new()
    {
        Id = "entry-1",
        DisplayName = "Edge",
        Kind = AppItemKind.Program,
        Target = EdgePath,
    };
}
