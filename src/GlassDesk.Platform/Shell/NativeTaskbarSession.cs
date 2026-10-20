using System.Runtime.InteropServices;

namespace GlassDesk.Platform.Shell;

/// <summary>
/// 隐藏 / 恢复 Windows 原生任务栏（主屏 <c>Shell_TrayWnd</c> 与各副屏
/// <c>Shell_SecondaryTrayWnd</c>）。
/// </summary>
/// <remarks>
/// 只做两件事：把任务栏窗口 <c>ShowWindow(SW_HIDE)</c>，需要时再显示回来。
/// 任务栏的「贴边滑出」是它自己响应指针位置做的，窗口被隐藏后它就不再有机会滑出，
/// 这正是「指针移回 Dock 原位置时原生任务栏被带出来」的解法。
/// 资源管理器重启或用户改任务栏设置时 shell 可能重新显示窗口，
/// 所以调用方需要周期性调用 <see cref="EnsureHidden"/>。
/// 产品退出前必须 <see cref="Dispose"/>，否则任务栏会一直消失。
/// </remarks>
public sealed class NativeTaskbarSession : IDisposable
{
    private static readonly string[] TaskbarClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private bool _hidden;
    private bool _disposed;

    /// <summary>是否由本会话隐藏了任务栏。</summary>
    public bool IsHidden => _hidden && !_disposed;

    /// <summary>最近一次操作的可读说明，用于设置页状态文本。</summary>
    public string Status { get; private set; } = "原生任务栏未被接管";

    /// <summary>隐藏任务栏窗口。成功返回 true；找不到任务栏窗口时返回 false。</summary>
    public bool TryHide(out string? error)
    {
        error = null;
        if (_disposed) { error = "任务栏会话已关闭"; return false; }
        var windows = FindTaskbarWindows();
        if (windows.Length == 0) { error = "找不到任务栏窗口（Shell_TrayWnd）"; Status = error; return false; }

        foreach (var hwnd in windows)
            ShowWindow(hwnd, SwHide);

        _hidden = true;
        var stillVisible = CountVisible(windows);
        if (stillVisible > 0)
        {
            Status = stillVisible == windows.Length ? "任务栏隐藏失败" : $"已隐藏部分任务栏（还剩 {stillVisible} 个窗口）";
            error = Status;
            return false;
        }

        Status = $"已隐藏 {windows.Length} 个任务栏窗口";
        return true;
    }

    /// <summary>把任务栏窗口显示回来。</summary>
    public bool TryRestore(out string? error)
    {
        error = null;
        if (!_hidden) { Status = "原生任务栏未被接管"; return true; }
        var windows = FindTaskbarWindows();
        foreach (var hwnd in windows)
            ShowWindow(hwnd, SwShow);

        var visible = CountVisible(windows);
        if (windows.Length == 0 || visible < windows.Length)
        {
            Status = "任务栏恢复失败，请重启资源管理器";
            error = Status;
            return false;
        }

        _hidden = false;
        Status = "已恢复原生任务栏";
        return true;
    }

    /// <summary>外部已经恢复任务栏之后，把会话状态标记回「未接管」。</summary>
    public void MarkRestored()
    {
        _hidden = false;
        if (!_disposed) Status = "原生任务栏未被接管";
    }

    /// <summary>
    /// 无论本会话之前是否隐藏过，都把任务栏窗口显示回来。
    /// </summary>
    /// <remarks>
    /// 上一个进程被强制结束（崩溃、任务管理器）时任务栏会停留在隐藏状态，
    /// 下次启动且设置已关闭时必须靠这里把它救回来。
    /// 返回本次恢复的窗口数。
    /// </remarks>
    public int EnsureVisible()
    {
        if (_disposed) return 0;
        var shown = 0;
        foreach (var hwnd in FindTaskbarWindows())
        {
            if (IsWindowVisible(hwnd)) continue;
            ShowWindow(hwnd, SwShow);
            shown++;
        }

        if (shown > 0) _hidden = false;
        return shown;
    }

    /// <summary>
    /// 确认任务栏仍然隐藏；shell 重新显示它时再隐藏一次。
    /// 返回本次重新隐藏的窗口数。
    /// </summary>
    public int EnsureHidden()
    {
        if (!IsHidden) return 0;
        var rehidden = 0;
        foreach (var hwnd in FindTaskbarWindows())
        {
            if (!IsWindowVisible(hwnd)) continue;
            ShowWindow(hwnd, SwHide);
            rehidden++;
        }

        return rehidden;
    }

    public void Dispose()
    {
        if (_disposed) return;
        TryRestore(out _);
        _disposed = true;
    }

    internal static nint[] FindTaskbarWindows()
    {
        var found = new List<nint>(2);
        EnumWindows((hwnd, _) =>
        {
            // 不能只找可见窗口：隐藏之后还要靠这份清单把任务栏显示回来。
            var name = new char[64];
            var length = GetClassName(hwnd, name, name.Length);
            if (length <= 0) return true;
            var className = new string(name, 0, length);
            foreach (var candidate in TaskbarClasses)
            {
                if (string.Equals(className, candidate, StringComparison.Ordinal))
                {
                    found.Add(hwnd);
                    break;
                }
            }

            return true;
        }, 0);

        return [.. found];
    }

    private static int CountVisible(nint[] windows)
    {
        var count = 0;
        foreach (var hwnd in windows)
            if (IsWindowVisible(hwnd)) count++;
        return count;
    }

    private const int SwHide = 0;
    private const int SwShow = 5;

    private delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, [Out] char[] name, int size);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
}
