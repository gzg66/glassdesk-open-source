using System.IO;
using System.Windows.Threading;

using GlassDesk.Core.Input;
using GlassDesk.Core.Storage;
using GlassDesk.Platform.Input;
using GlassDesk.Platform.Shell;

namespace GlassDesk.App;

/// <summary>
/// 把「隐藏 Windows 原生任务栏」这项设置落到系统上：隐藏任务栏窗口 + 接管单独轻按的 Win 键。
/// </summary>
/// <remarks>
/// 只在用户主动打开设置时动手，并且退出时一定恢复：
/// 任务栏是系统的核心界面，产品崩溃或异常退出后它必须还能回来，
/// 所以 <see cref="Dispose"/> 会被 <c>AppHost.Dispose</c> 调用。
/// 资源管理器重启会重建任务栏窗口，因此用一个 1 秒的看门狗周期性确认它仍然隐藏。
/// </remarks>
internal sealed class DesktopTaskbarIntegration : IDisposable
{
    private readonly GlassDeskDataStore _data;
    private readonly Dispatcher _dispatcher;
    private readonly Action _toggleLauncher;
    private readonly NativeTaskbarSession _taskbar = new();
    private readonly WinKeyHook _hook;
    private WinKeyState _winKeyState = WinKeyState.Idle;
    private readonly DispatcherTimer _watchdog;
    private readonly DispatcherTimer _reveal;
    private bool _suspended;
    private bool _disposed;

    public DesktopTaskbarIntegration(GlassDeskDataStore data, Dispatcher dispatcher, Action toggleLauncher)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _toggleLauncher = toggleLauncher ?? throw new ArgumentNullException(nameof(toggleLauncher));

        _watchdog = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _watchdog.Tick += OnWatchdogTick;
        _reveal = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _reveal.Tick += OnRevealElapsed;
        _hook = new WinKeyHook(DecideWinKey);
        _hook.Tapped += OnWinKeyTapped;
    }

    /// <summary>最近一次接管结果，设置页拿它显示状态。</summary>
    public string Status { get; private set; } = "原生任务栏未被接管";

    /// <summary>状态变化时通知宿主刷新设置页文本。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>按当前设置决定隐藏还是恢复任务栏。可重复调用。</summary>
    public void Apply()
    {
        if (_disposed) return;
        _reveal.Stop();
        _suspended = false;
        if (_data.Settings.Current.DesktopTaskbar.HideNativeTaskbar)
            Start();
        else
            Stop();
    }

    /// <summary>把纯逻辑层的判定翻译成钩子能执行的指令。</summary>
    private WinKeyDirective DecideWinKey(bool isWinKey, bool isDown)
    {
        var decision = isDown
            ? WinKeyGestureRouter.OnKeyDown(_winKeyState, isWinKey)
            : WinKeyGestureRouter.OnKeyUp(_winKeyState, isWinKey);
        _winKeyState = decision.State;
        return new WinKeyDirective(decision.Swallow, decision.ReplayWinDown, decision.ReplayWinUp, decision.ToggleLauncher);
    }

    /// <summary>
    /// 临时把 Windows 任务栏显示回来（托盘图标、系统托盘菜单都在里面），到时自动再隐藏。
    /// </summary>
    /// <remarks>
    /// 任务栏被 SW_HIDE 之后，Win11 的托盘溢出面板是 XAML island，只有 shell 自己的按钮
    /// 才能把它拉起来，外部 ShowWindow 只会得到空白；所以这里退一步：整条任务栏露 60 秒，
    /// 用完自动收回去，不需要改设置。
    /// </remarks>
    public void RevealTaskbarTemporarily(TimeSpan duration)
    {
        if (_disposed) return;
        if (!_data.Settings.Current.DesktopTaskbar.HideNativeTaskbar)
        {
            _taskbar.EnsureVisible();
            Report("Windows 原生任务栏本来就显示着");
            return;
        }

        if (duration <= TimeSpan.Zero) duration = TimeSpan.FromSeconds(60);
        _suspended = true;
        _watchdog.Stop();
        var shown = _taskbar.EnsureVisible();
        _reveal.Interval = duration;
        _reveal.Stop();
        _reveal.Start();
        Report($"已临时显示 Windows 任务栏（{shown} 个窗口），{duration.TotalSeconds:0} 秒后自动隐藏");
    }

    private void OnRevealElapsed(object? sender, EventArgs e)
    {
        _reveal.Stop();
        if (_disposed) return;
        _suspended = false;
        if (_data.Settings.Current.DesktopTaskbar.HideNativeTaskbar) Start();
        else Report("Windows 原生任务栏保持显示");
    }

    private void Start()
    {
        if (_suspended) return;
        // 上一个进程若被强制结束，任务栏可能还停在隐藏状态；先救回来再按设置隐藏，
        // 以免「隐藏失败」被误报成成功。
        _taskbar.EnsureVisible();
        var hid = _taskbar.TryHide(out var hideError);
        if (hid) _watchdog.Start();

        var hookNote = string.Empty;
        if (!_hook.TryStart(out var hookError))
        {
            hookNote = "；Win 键接管失败：" + hookError;
            WriteLog("Win 键钩子安装失败：" + hookError);
        }
        else
        {
            WriteLog("Win 键钩子已安装");
        }

        Report(hid
            ? _taskbar.Status + "；Win 键：轻按打开启动台" + hookNote
            : "任务栏隐藏失败：" + hideError + hookNote);
    }

    private void Stop()
    {
        _watchdog.Stop();
        _hook.Stop();
        _winKeyState = WinKeyState.Idle;
        var shown = _taskbar.EnsureVisible();
        _taskbar.MarkRestored();
        Report(shown > 0 ? $"已恢复 Windows 原生任务栏（{shown} 个窗口）" : "Windows 原生任务栏保持原样");
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var rehidden = _taskbar.EnsureHidden();
        if (!_hook.IsRunning)
        {
            // 钩子被系统摘掉时重装一次，否则 Win 键会静默恢复成系统的开始菜单。
            _hook.TryStart(out _);
        }

        if (rehidden > 0) Report($"{_taskbar.Status}；资源管理器重新显示了任务栏，已再次隐藏");
    }

    private void OnWinKeyTapped()
    {
        if (_disposed) return;
        WriteLog("Win 键轻按：切换启动台");
        // 钩子回调在 UI 线程上同步执行，但同步直接调用会拖长钩子返回时间，
        // 超过系统的 LowLevelHooksTimeout 就会被摘钩，所以转投递一次。
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed) _toggleLauncher();
        }));
    }

    private void Report(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
        WriteLog(status);
    }

    private void WriteLog(string message)
    {
        try
        {
            var directory = _data.Paths.LogDirectory;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "desktop-taskbar.log"),
                $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不能影响设置生效。
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reveal.Stop();
        _reveal.Tick -= OnRevealElapsed;
        _watchdog.Stop();
        _watchdog.Tick -= OnWatchdogTick;
        _hook.Tapped -= OnWinKeyTapped;
        _hook.Dispose();
        _taskbar.Dispose();
        Report(_taskbar.Status);
    }
}
