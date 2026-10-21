using GlassDesk.Core.Models;
using GlassDesk.Platform.Shell;

namespace GlassDesk.AppServices;

/// <summary>一次任务启动的结果。失败时带一句可读的说明。</summary>
public sealed record JumpListTaskLaunchResult(bool Started, string? Message);

/// <summary>
/// 程序跳转列表任务的进程级缓存，以及执行任务的那条路径。
/// </summary>
/// <remarks>
/// 索引必须缓存：右键菜单是同步打开的，而一次构建要读几十个文件、解析上百个 link。
/// 快照是不可变字典 + 30 秒存活期 + 目录变化检查；构建在<strong>调用线程</strong>同步做——
/// 启动台是 WPF UI 线程（STA），而 <c>IShellLink</c> 正是 STA 组件，所以不需要另起线程宿主。
/// 实测 44 个文件 / 约 350KB / 约 200 个 link 是毫秒级；若将来真机上变慢，降级办法是
/// 「只读最近 N 个文件」，而不是引入工作线程。
///
/// <para>
/// 执行也收在这一层：启动台项目没有 <c>GlassDesk.Platform</c> 的引用，让它去调
/// <c>ShellExecutor</c> 会把分层打穿。
/// </para>
///
/// <para>
/// 「任务」的执行语义是<strong>总是新开</strong>，不是「已运行就提到前面」——
/// 后者是应用图标本身的语义，两者混在一起会让「新建窗口」变成「切到已有窗口」。
/// </para>
/// </remarks>
public static class JumpListTaskCatalog
{
    /// <summary>快照存活期。</summary>
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private static readonly object Gate = new();

    private static Dictionary<string, IReadOnlyList<JumpListTask>> _index = new(StringComparer.OrdinalIgnoreCase);
    private static DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private static string _fingerprint = string.Empty;
    private static int _failureCount;
    private static bool _built;

    /// <summary>每个程序最多返回几条任务。</summary>
    public static int MaxTasksPerApp => JumpListIndex.MaxTasksPerApp;

    /// <summary>构建索引失败过几次（供探针与诊断用，不进界面）。</summary>
    public static int LastFailureCount
    {
        get
        {
            lock (Gate) return _failureCount;
        }
    }

    /// <summary>
    /// 取某个程序的任务；没有就返回空列表。
    /// </summary>
    public static IReadOnlyList<JumpListTask> TasksFor(AppEntry? entry)
    {
        if (entry is null) return [];

        var index = CurrentIndex();
        if (index.Count == 0) return [];

        try
        {
            return JumpListIndex.Match(entry, index);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _ = ex;
            return [];
        }
    }

    /// <summary>
    /// 启动一条任务。
    /// </summary>
    public static JumpListTaskLaunchResult Launch(JumpListTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            var (started, errorCode) = LaunchShell(task.ExecutablePath, task.Arguments, task.WorkingDirectory);

            return started
                ? new JumpListTaskLaunchResult(true, null)
                : new JumpListTaskLaunchResult(false, $"任务未能启动（外壳错误码 {errorCode}）。");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _ = ex;
            return new JumpListTaskLaunchResult(false, "任务未能启动。");
        }
    }

    /// <summary>目录与时钟的注入点，仅供测试。</summary>
    internal static Func<string> RecentFolder { get; set; } = () => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Recent");

    internal static Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    internal static Func<string, IReadOnlyList<JumpListTask>> Reader { get; set; } = JumpListTaskReader.ReadAll;

    internal static Func<string, string?, string?, (bool Started, int ErrorCode)> LaunchShell { get; set; } =
        (target, arguments, workingDirectory) =>
        {
            var outcome = ShellExecutor.Open(target, arguments, workingDirectory);
            return (outcome.Started, outcome.ErrorCode);
        };

    /// <summary>清掉快照与注入点，仅供测试。</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _index = new Dictionary<string, IReadOnlyList<JumpListTask>>(StringComparer.OrdinalIgnoreCase);
            _builtAt = DateTimeOffset.MinValue;
            _fingerprint = string.Empty;
            _failureCount = 0;
            _built = false;
        }

        RecentFolder = () => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Recent");
        Clock = () => DateTimeOffset.UtcNow;
        Reader = JumpListTaskReader.ReadAll;
        LaunchShell = (target, arguments, workingDirectory) =>
        {
            var outcome = ShellExecutor.Open(target, arguments, workingDirectory);
            return (outcome.Started, outcome.ErrorCode);
        };
    }

    private static Dictionary<string, IReadOnlyList<JumpListTask>> CurrentIndex()
    {
        lock (Gate)
        {
            var now = Clock();
            var fingerprint = ComputeFingerprint();

            // 首次必须同步建好：否则用户第一次右键看不到任务，会以为功能没生效。
            var stale = !_built
                || now - _builtAt > TimeToLive
                || !string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal);

            if (!stale) return _index;

            try
            {
                _index = JumpListIndex.Build(Reader(RecentFolder()));
                _fingerprint = fingerprint;
                _builtAt = now;
                _built = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 保留旧快照：一个坏文件不该把已经能用的任务清空。
                _ = ex;
                _failureCount++;
                _builtAt = now;
            }

            return _index;
        }
    }

    /// <summary>
    /// 目录指纹：文件数与最大写入时间。只做一次枚举，几十项的量级。
    /// </summary>
    private static string ComputeFingerprint()
    {
        try
        {
            var directory = Path.Combine(RecentFolder(), "CustomDestinations");
            if (!Directory.Exists(directory)) return string.Empty;

            var files = Directory.GetFiles(directory, "*.customDestinations-ms");
            if (files.Length == 0) return string.Empty;

            long newest = 0;
            foreach (var file in files)
            {
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > newest) newest = ticks;
            }

            return $"{files.Length}:{newest}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return string.Empty;
        }
    }
}
