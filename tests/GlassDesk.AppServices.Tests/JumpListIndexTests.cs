using GlassDesk.Core.Models;
using GlassDesk.Platform.Shell;
using Xunit;

namespace GlassDesk.AppServices.Tests;

/// <summary>
/// 把跳转列表任务按可执行文件归一化，并匹配到应用库里的条目。
/// </summary>
/// <remarks>
/// 全部用内存里造的任务，不读文件、不碰 COM：这一层要能被穷举地测。
/// </remarks>
public sealed class JumpListIndexTests
{
    private const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string PlaywrightChromePath = @"C:\Users\me\ms-playwright\chromium-1200\chrome-win64\chrome.exe";
    private const string EdgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    [Fact]
    public void Build_MergesSameExecutableAndOrdersByWriteTimeDescending()
    {
        var older = Task(EdgePath, "新建窗口(N)", "--old", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Task(EdgePath, "新建无痕窗口(I)", "--new", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var index = JumpListIndex.Build([older, newer]);

        var tasks = Assert.Single(index).Value;
        Assert.Equal(["新建无痕窗口(I)", "新建窗口(N)"], tasks.Select(task => task.Title));
    }

    [Fact]
    public void Build_DropsEntriesThatAreNotExecutables()
    {
        var index = JumpListIndex.Build(
        [
            Task(@"C:\somewhere\readme.txt", "不该出现", string.Empty, DateTime.UtcNow),
            Task(EdgePath, "新建窗口(N)", string.Empty, DateTime.UtcNow),
        ]);

        Assert.Single(index);
        Assert.Equal("新建窗口(N)", Assert.Single(Assert.Single(index).Value).Title);
    }

    [Fact]
    public void Build_CollapsesSameTitleKeepingShortestArguments()
    {
        // 实测形态：一份跳转列表来自带 --user-data-dir 的临时档案夹，另一份是用户自己的档案。
        // 同一个标题只该出现一次，取参数最短的那条（临时档案夹的参数更长）。
        var debugProfile = Task(
            EdgePath, "新建窗口(N)",
            @"--user-data-dir=""C:\Temp\edge-debug-profile"" --edge-jumplist-new-window",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var normal = Task(
            EdgePath, "新建窗口(N)", "--edge-jumplist-new-window",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var index = JumpListIndex.Build([debugProfile, normal]);

        var task = Assert.Single(Assert.Single(index).Value);
        Assert.Equal("--edge-jumplist-new-window", task.Arguments);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", ChromePath)]
    [InlineData(@"C:\Somewhere\README.MD", "")]
    public void NormalizeExecutable_KeepsExecutablesOnly(string input, string expected)
    {
        Assert.Equal(expected, JumpListIndex.NormalizeExecutable(input));
    }

    [Fact]
    public void NormalizeExecutable_ExpandsEnvironmentVariables()
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (string.IsNullOrEmpty(programFiles)) return; // 没这个变量的环境跳过（Windows 上一定有）

        var normalized = JumpListIndex.NormalizeExecutable(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe");

        Assert.StartsWith(programFiles, normalized, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("chrome.exe", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_FindsByExactPath()
    {
        var entry = Entry(EdgePath);
        var index = JumpListIndex.Build([Task(EdgePath, "新建窗口(N)", "--edge-jumplist-new-window", DateTime.UtcNow)]);

        Assert.Single(JumpListIndex.Match(entry, index));
    }

    [Fact]
    public void Match_ResolvesShortcutTarget()
    {
        var entry = Entry(@"C:\Users\me\Desktop\Edge.lnk");
        var index = JumpListIndex.Build([Task(EdgePath, "新建窗口(N)", string.Empty, DateTime.UtcNow)]);

        var tasks = JumpListIndex.Match(entry, index, _ => EdgePath);

        Assert.Single(tasks);
    }

    [Fact]
    public void Match_FallsBackToUniqueFileName()
    {
        var entry = Entry(@"D:\别的目录\msedge.exe");
        var index = JumpListIndex.Build([Task(EdgePath, "新建窗口(N)", string.Empty, DateTime.UtcNow)]);

        Assert.Single(JumpListIndex.Match(entry, index));
    }

    [Fact]
    public void Match_RefusesAmbiguousFileName()
    {
        var entry = Entry(@"D:\别的目录\chrome.exe");
        var index = JumpListIndex.Build(
        [
            Task(ChromePath, "打开新的窗口(N)", string.Empty, DateTime.UtcNow),
            Task(PlaywrightChromePath, "打开新的无痕式窗口(I)", string.Empty, DateTime.UtcNow),
        ]);

        Assert.Empty(JumpListIndex.Match(entry, index));
    }

    [Fact]
    public void Match_FallsBackFromProxyExecutable()
    {
        var entry = Entry(EdgePath);
        var proxyPath = Path.Combine(Path.GetDirectoryName(EdgePath)!, "msedge_proxy.exe");
        var index = JumpListIndex.Build([Task(proxyPath, "新建窗口(N)", string.Empty, DateTime.UtcNow)]);

        Assert.Single(JumpListIndex.Match(entry, index));
    }

    [Fact]
    public void Match_DoesNotBorrowTasksFromAnotherExecutableInTheSameDirectory()
    {
        // 同一目录里还可能是**另一个程序**（System32 下有 powershell.exe 与
        // powershell_ise.exe），把邻居的任务挂过来会让「右键 A 点出来的却是 B」。
        var entry = Entry(EdgePath);
        var sibling = Path.Combine(Path.GetDirectoryName(EdgePath)!, "msedge_另一个.exe");
        var index = JumpListIndex.Build([Task(sibling, "新建窗口(N)", string.Empty, DateTime.UtcNow)]);

        Assert.Empty(JumpListIndex.Match(entry, index));
    }

    [Fact]
    public void Match_RefusesSameDirectoryWhenSeveralCandidates()
    {
        var entry = Entry(EdgePath);
        var directory = Path.GetDirectoryName(EdgePath)!;
        var index = JumpListIndex.Build(
        [
            Task(Path.Combine(directory, "one.exe"), "任务一", string.Empty, DateTime.UtcNow),
            Task(Path.Combine(directory, "two.exe"), "任务二", string.Empty, DateTime.UtcNow),
        ]);

        Assert.Empty(JumpListIndex.Match(entry, index));
    }

    [Fact]
    public void Match_CapsTasksPerApp()
    {
        var entry = Entry(EdgePath);
        var tasks = Enumerable.Range(0, 12)
            .Select(number => Task(EdgePath, $"任务 {number}", string.Empty, DateTime.UtcNow))
            .ToList();

        Assert.Equal(JumpListIndex.MaxTasksPerApp, JumpListIndex.Match(entry, JumpListIndex.Build(tasks)).Count);
    }

    private static JumpListTask Task(string executablePath, string title, string arguments, DateTime writeTime) =>
        new(executablePath, arguments, string.Empty, title, @"C:\fake\a.customDestinations-ms", writeTime);

    private static AppEntry Entry(string target) => new()
    {
        Id = "entry-1",
        DisplayName = "测试",
        Kind = AppItemKind.Program,
        Target = target,
    };
}
