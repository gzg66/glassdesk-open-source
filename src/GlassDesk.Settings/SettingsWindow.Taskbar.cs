using System.Windows;

using GlassDesk.Core.Models.Settings;

namespace GlassDesk.Settings;

/// <summary>
/// 「系统任务栏」页：接管 Windows 原生任务栏的开关。
/// </summary>
/// <remarks>
/// 单独成文件是为了让 fork 的改动集中在一处，上游同步时只可能撞到需要增删的那一行。
/// </remarks>
public sealed partial class SettingsWindow
{
    private bool _loadingNativeTaskbarControls;

    /// <summary>用户要求临时露出 Windows 任务栏（托盘图标在里面）。</summary>
    public event EventHandler? TaskbarRevealRequested;

    /// <summary>供宿主把接管结果（成功/失败原因）显示在设置页。</summary>
    public void ReportNativeTaskbarStatus(string text)
    {
        if (NativeTaskbarStatus is not null)
            NativeTaskbarStatus.Text = text;
    }

    private void LoadNativeTaskbarControls()
    {
        if (_dataStore is null) return;
        _loadingNativeTaskbarControls = true;
        try
        {
            var enabled = _dataStore.Settings.Current.DesktopTaskbar.HideNativeTaskbar;
            HideNativeTaskbarCheck.IsChecked = enabled;
            NativeTaskbarStatus.Text = enabled
                ? "已开启：Windows 原生任务栏将被隐藏，Win 键改为打开 GlassDesk 启动台。"
                : "已关闭：Windows 任务栏保持系统原样（指针回到屏幕底边时它仍会滑出）。";
        }
        finally { _loadingNativeTaskbarControls = false; }
    }

    private void OnRevealNativeTaskbarClick(object sender, RoutedEventArgs e)
        => TaskbarRevealRequested?.Invoke(this, EventArgs.Empty);

    private void OnHideNativeTaskbarChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingNativeTaskbarControls) return;
        if (sender is not System.Windows.Controls.CheckBox { IsChecked: { } desired }) return;
        if (_dataStore is null) return;

        var current = _dataStore.Settings.Current;
        current.DesktopTaskbar.HideNativeTaskbar = desired;
        try
        {
            _dataStore.Settings.Save();
        }
        catch (Exception ex)
        {
            NativeTaskbarStatus.Text = "保存失败：" + ex.Message;
            return;
        }

        DesktopTaskbarChanged?.Invoke(this, current.DesktopTaskbar);
        NativeTaskbarStatus.Text = desired
            ? "已开启：正在隐藏 Windows 原生任务栏…"
            : "已关闭：正在恢复 Windows 原生任务栏…";
    }
}
