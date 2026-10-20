using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace GlassDesk.App;

/// <summary>
/// 直接把 Windows 的系统托盘溢出面板拉起来，不需要显示任务栏。
/// </summary>
/// <remarks>
/// Win 11 的任务栏是 XAML island：托盘按钮不是普通子窗口，也不能靠 <c>ShowWindow</c>
/// 打开溢出面板（会得到空白窗口）。但它仍然是 UI Automation 元素——
/// 找到任务栏里的 <c>DesktopWindowContentBridge</c>，再点「显示隐藏的图标」按钮，
/// 面板就会由 shell 自己弹出，此时任务栏窗口保持隐藏，用户也看不到整条任务栏闪一下。
/// 找不到（例如 Windows 10 的布局）时由调用方退回「临时显示任务栏」。
/// </remarks>
internal static class TrayFlyout
{
    private const string OverflowButtonName = "显示隐藏的图标";

    public static bool TryShow(out string? error)
    {
        error = null;
        try
        {
            var tray = FindWindow("Shell_TrayWnd", null);
            if (tray == 0)
            {
                error = "找不到任务栏窗口";
                return false;
            }

            var bridge = FindChildByClass(tray, "DesktopWindowContentBridge");
            if (bridge == 0)
            {
                error = "任务栏没有可用的 UI 自动化入口";
                return false;
            }

            var root = AutomationElement.FromHandle(bridge);
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, OverflowButtonName));
            var button = root?.FindFirst(TreeScope.Descendants, condition);
            if (button is null)
            {
                error = "找不到「显示隐藏的图标」按钮";
                return false;
            }

            if (button.GetCurrentPattern(InvokePattern.Pattern) is not InvokePattern invoke)
            {
                error = "托盘按钮不支持 Invoke";
                return false;
            }

            invoke.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static nint FindChildByClass(nint parent, string classNamePart)
    {
        nint found = 0;
        EnumChildWindows(parent, (hwnd, _) =>
        {
            var buffer = new char[128];
            var length = GetClassName(hwnd, buffer, buffer.Length);
            if (length <= 0) return true;
            if (!new string(buffer, 0, length).Contains(classNamePart, StringComparison.Ordinal)) return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    private delegate bool EnumChildProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, [Out] char[] name, int size);
}
