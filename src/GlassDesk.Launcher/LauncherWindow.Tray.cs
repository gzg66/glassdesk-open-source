using System.Windows;

namespace GlassDesk.Launcher;

/// <summary>
/// 启动台的「托盘图标」按钮：Win 11 任务栏被接管（隐藏）之后，系统托盘溢出面板
/// 仍然可以由 shell 自己的按钮拉起来，这里把那个按钮的入口搬到启动台上。
/// </summary>
/// <remarks>
/// 单独成文件，避免与上游对 LauncherWindow 的改动互相踩踏。
/// 真正的 UIA 调用在宿主里（<c>GlassDesk.App.TrayFlyout</c>），Dock/启动台不直接引用 Win32。
/// </remarks>
public partial class LauncherWindow
{
    /// <summary>宿主提供的「显示系统托盘图标列表」动作；为空时按钮不显示。</summary>
    public Action? ShowTrayFlyout
    {
        get => _showTrayFlyout;
        set
        {
            _showTrayFlyout = value;
            if (TrayFlyoutButton is not null)
                TrayFlyoutButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private Action? _showTrayFlyout;

    private void OnTrayFlyoutClick(object sender, RoutedEventArgs e) => _showTrayFlyout?.Invoke();
}
