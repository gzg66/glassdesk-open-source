using GlassDesk.Core.Models.Settings;

namespace GlassDesk.Core.Tests;

/// <summary>Dock「显示方式」（常驻显示 / 自动隐藏）的设置构造规则。</summary>
public sealed class DockVisibilitySettingsTests
{
    private static DockSettings CustomDock() => new()
    {
        AutoHide = false,
        AutoHideLeaveDelayMs = 420,
        EdgeHotzoneSize = 20,
        Placement = DockPlacement.TopCenter,
        SizeScale = 1.2,
        OffsetX = 24,
        ShowLauncherEntry = false,
    };

    [Fact]
    public void WithDockVisibility_true_enables_auto_hide_and_keeps_other_values()
    {
        var original = CustomDock();

        var changed = SettingsPageEditor.WithDockVisibility(original, autoHide: true);

        Assert.True(changed.AutoHide);
        Assert.Equal(420, changed.AutoHideLeaveDelayMs);
        Assert.Equal(20, changed.EdgeHotzoneSize);
        Assert.Equal(DockPlacement.TopCenter, changed.Placement);
        Assert.Equal(1.2, changed.SizeScale);
        Assert.Equal(24, changed.OffsetX);
        Assert.False(changed.ShowLauncherEntry);

        // 输入对象不被就地修改（设置页每次都拿新快照，避免半写状态）。
        Assert.False(original.AutoHide);
    }

    [Fact]
    public void WithDockVisibility_false_disables_auto_hide()
    {
        var original = CustomDock();
        original.AutoHide = true;

        var changed = SettingsPageEditor.WithDockVisibility(original, autoHide: false);

        Assert.False(changed.AutoHide);
        Assert.Equal(420, changed.AutoHideLeaveDelayMs);
    }

    [Fact]
    public void WithDockVisibility_result_passes_validation()
    {
        var settings = new GlassDeskSettings();
        settings.Dock = SettingsPageEditor.WithDockVisibility(new DockSettings(), autoHide: true);

        Assert.Null(SettingsPageEditor.Validate(settings));
    }

    [Fact]
    public void Normalize_keeps_auto_hide_choice()
    {
        // 回归：Normalize 曾在保存时把 AutoHide 强制改回 false，导致设置页点了没反应。
        var settings = new GlassDeskSettings();
        settings.Dock.AutoHide = true;

        settings.Normalize();

        Assert.True(settings.Dock.AutoHide);
    }
}
