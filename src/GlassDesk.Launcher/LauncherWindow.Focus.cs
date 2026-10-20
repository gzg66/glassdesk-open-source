using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GlassDesk.Launcher;

/// <summary>
/// 让启动台真正拿到**系统级**键盘焦点，用户按 Win 唤出后可以直接打字搜索。
/// </summary>
/// <remarks>
/// WPF 的 <c>SearchBox.Focus()</c> 只解决窗口内部的逻辑焦点：如果窗口不是前台窗口，
/// 键盘输入根本不进这个进程。实测按 Win 唤出时前台窗口还是 shell 的「任务切换」，
/// 所以必须先把窗口抢到前台，再把焦点放进搜索框。
/// 抢前台用 <c>AttachThreadInput</c> 挂到当前前台线程上再 <c>SetForegroundWindow</c>，
/// 绕开「只有前台进程才能设置前台窗口」的限制；结束后立刻解开附加。
/// </remarks>
public partial class LauncherWindow
{
    private bool _focusHooked;

    /// <summary>把窗口提到前台，并把键盘焦点放进搜索框。</summary>
    public void TakeKeyboardFocus()
    {
        Dispatcher.BeginInvoke(new Action(ApplyFocus), DispatcherPriority.Loaded);
    }

    private void ApplyFocus()
    {
        if (!IsVisible) return;

        if (!_focusHooked)
        {
            _focusHooked = true;
            // 用户手动点回窗口、或从别的窗口切回来时，也把焦点送回搜索框。
            Activated += (_, _) =>
            {
                if (IsVisible && _settings.FocusSearchOnOpen) FocusSearchBox();
            };
        }

        ForegroundGrant.Force(this);
        FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }
}

internal static class ForegroundGrant
{
    private const int SwShow = 5;

    public static void Force(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;

        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            _ = SetFocus(hwnd);
            return;
        }

        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        var attached = false;
        if (targetThread != 0 && foregroundThread != 0 && targetThread != foregroundThread)
            attached = AttachThreadInput(foregroundThread, targetThread, true);

        try
        {
            _ = ShowWindow(hwnd, SwShow);
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
            _ = SetActiveWindow(hwnd);
            _ = SetFocus(hwnd);
        }
        finally
        {
            if (attached) _ = AttachThreadInput(foregroundThread, targetThread, false);
        }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetActiveWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
}
