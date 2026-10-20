using System.Text.Json;
using GlassDesk.Core.Input;
using GlassDesk.Core.Serialization;

namespace GlassDesk.Core.Models.Settings;

public static class SettingsPageEditor
{
    public static GlassDeskSettings Clone(GlassDeskSettings settings) => JsonSerializer.Deserialize(
        JsonSerializer.SerializeToUtf8Bytes(settings, GlassDeskJsonContext.Default.GlassDeskSettings),
        GlassDeskJsonContext.Default.GlassDeskSettings)!;

    public static void Apply(GlassDeskSettings target, GlassDeskSettings source)
    {
        target.Appearance = source.Appearance;
        target.Dock = source.Dock;
        target.Launcher = source.Launcher;
        target.Shortcuts = source.Shortcuts;
        target.Display = source.Display;
        target.Performance = source.Performance;
        target.DesktopTaskbar = source.DesktopTaskbar;
    }

    /// <summary>
    /// 得到一份把 Dock 显示方式改成指定值的副本（<c>true</c> = 自动隐藏）。
    /// </summary>
    /// <remarks>
    /// 「显示方式」只改 <see cref="DockSettings.AutoHide"/>，其余字段（位置、大小、
    /// 偏移、各入口开关）必须原样带过去——设置页每次只改一个控件，不能因为
    /// 切换显示方式就把用户调好的布局打回默认。
    /// 返回新对象而不是就地修改：设置页保存失败时旧值必须仍可用。
    /// </remarks>
    public static DockSettings WithDockVisibility(DockSettings dock, bool autoHide)
    {
        ArgumentNullException.ThrowIfNull(dock);

        return new DockSettings
        {
            SizeScale = dock.SizeScale,
            ShowLauncherEntry = dock.ShowLauncherEntry,
            ShowThisPcEntry = dock.ShowThisPcEntry,
            ShowDesktopIconsEntry = dock.ShowDesktopIconsEntry,
            FrameLengthDip = dock.FrameLengthDip,
            IconSize = dock.IconSize,
            IconSpacing = dock.IconSpacing,
            MagnificationPeak = dock.MagnificationPeak,
            Placement = dock.Placement,
            OffsetX = dock.OffsetX,
            OffsetY = dock.OffsetY,
            AutoHide = autoHide,
            AutoHideLeaveDelayMs = dock.AutoHideLeaveDelayMs,
            EdgeRevealEnabled = dock.EdgeRevealEnabled,
            EdgeHotzoneSize = dock.EdgeHotzoneSize,
            BlockRevealWhenCovered = dock.BlockRevealWhenCovered,
            TargetDisplayId = dock.TargetDisplayId,
            ShowRunningIndicator = dock.ShowRunningIndicator,
        };
    }

    public static GlassDeskSettings ResetPage(GlassDeskSettings settings, string pageName)
    {
        var copy = Clone(settings);
        switch (pageName)
        {
            case "外观":
                copy.Appearance = new() { DockCornerRadius = settings.Appearance.DockCornerRadius };
                break;
            case "Dock":
                copy.Dock = new() { TargetDisplayId = settings.Dock.TargetDisplayId };
                copy.Appearance.DockCornerRadius = 40;
                break;
            case "启动台": copy.Launcher = new(); break;
            case "快捷操作": copy.Shortcuts = new(); break;
            case "显示器": copy.Display = new(); copy.Dock.TargetDisplayId = null; break;
            case "性能": copy.Performance = new(); break;
            case "桌面与任务栏": copy.DesktopTaskbar = new(); break;
            default: throw new ArgumentException("此页面没有可重置的配置。", nameof(pageName));
        }
        return copy;
    }

    public static string? Validate(GlassDeskSettings settings)
    {
        var dock = settings.Dock;
        static bool Range(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
        if ((dock.FrameLengthDip != 0 && !Range(dock.FrameLengthDip, 64, DockSettings.MaximumFrameLengthDip))
            || !Range(dock.SizeScale, DockSettings.MinimumSizeScale, DockSettings.MaximumSizeScale)
            || !Range(dock.IconSize, 32, 96) || !Range(dock.IconSpacing, 0, 24) || !Range(dock.MagnificationPeak, 1, 2)
            || !Range(dock.OffsetX, -200, 200) || !Range(dock.OffsetY, -200, 200)
            || !Range(dock.AutoHideLeaveDelayMs, 0, 3000) || !Range(dock.EdgeHotzoneSize, 1, 64)
            || !Enum.IsDefined(dock.Placement)) return "Dock 数值超出允许范围。";
        if (!Range(settings.Launcher.Rows, 2, 10) || !Range(settings.Launcher.Columns, 2, 12)
            || !Range(settings.Launcher.WindowWorkAreaRatio, 0.3, 1)) return "启动台网格或窗口比例超出允许范围。";
        if (!Range(settings.Appearance.DockCornerRadius, 0, 40) || !Range(settings.Appearance.LauncherCornerRadius, 0, 80)
            || !Range(settings.Appearance.GlassTintOpacity, 0, 1)
            || !Range(settings.Appearance.GlassBackgroundClarity, 0, 1)
            || !Range(settings.Appearance.DockGlassTransparency, 0, 1)
            || !Range(settings.Appearance.LauncherGlassTransparency, 0, 1)
            || !Enum.IsDefined(settings.Appearance.Theme)
            || !Enum.IsDefined(settings.Appearance.GlassMaterial)) return "外观数值超出允许范围。";
        var shortcuts = settings.Shortcuts;
        if (!Range(shortcuts.HotCornerDwellMs, 100, 3000) || !Enum.IsDefined(shortcuts.HotCornerTarget)
            || !Enum.IsDefined(shortcuts.MouseBackAction) || !Enum.IsDefined(shortcuts.MouseForwardAction)) return "热角停留时间应为 100–3000 毫秒，且触发动作必须有效。";
        if (shortcuts.HotCornersEnabled && shortcuts.HotCornerTarget == HotCorner.None) return "开启热角后需要选择一个角落。";
        var seen = new HashSet<HotkeyChord>();
        foreach (var (_, definition) in HotkeyText.Bindings(shortcuts))
        {
            if (!definition.Enabled) continue;
            if (!HotkeyText.TryGetChord(definition, out var chord, out var error)) return error;
            if (!seen.Add(chord)) return "多个操作使用了同一个快捷键。";
        }
        return null;
    }
}
