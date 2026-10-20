namespace GlassDesk.Core.Models.Settings;

/// <summary>
/// 用户可配置内容的根，持久化为单个 JSON 文档。
/// </summary>
/// <remarks>
/// 这是我们自己的设置模型。它从不读写参考产品的
/// 配置（MacDock 的 <c>%APPDATA%\MacDock\settings.json</c> 或启动台的
/// localStorage），那些配置保持原样，老程序才能继续工作。
/// </remarks>
public sealed class GlassDeskSettings
{
    /// <summary>
    /// 磁盘上文档的 schema 版本。只要某次变更无法仅靠
    /// 默认值吸收就递增；旧文件在加载时迁移。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public AppearanceSettings Appearance { get; set; } = new();

    public DockSettings Dock { get; set; } = new();

    public LauncherSettings Launcher { get; set; } = new();

    public ShortcutSettings Shortcuts { get; set; } = new();

    public DisplaySettings Display { get; set; } = new();

    public PerformanceSettings Performance { get; set; } = new();

    public DesktopTaskbarSettings DesktopTaskbar { get; set; } = new();

    /// <summary>
    /// 把为 null 的节替换为默认值。反序列化手工编辑过或写入不完整的
    /// 文件时，某个节可能为 null，尽管类型声明并非如此；
    /// 否则每个使用方都得自己做 null 检查。
    /// </summary>
    /// <param name="disableRemovedFeatures">
    /// 从磁盘读入旧配置时置 <c>true</c>：把早期版本里已移除的功能（自动隐藏等）关掉。
    /// 保存路径必须传 <c>false</c>，否则用户在设置页刚打开的开关会被就地改回去
    /// ——这正是「显示方式」点了没反应的原因。
    /// </param>
    public void Normalize(bool disableRemovedFeatures = false)
    {
        Appearance ??= new AppearanceSettings();
        Dock ??= new DockSettings();
        Launcher ??= new LauncherSettings();
        Shortcuts ??= new ShortcutSettings();
        Display ??= new DisplaySettings();
        Performance ??= new PerformanceSettings();
        DesktopTaskbar ??= new DesktopTaskbarSettings();
        // EdgeRevealEnabled 仍无 UI，始终关闭（指针唤出由 AutoHide 的静止矩形判定负责）。
        Dock.EdgeRevealEnabled = false;
        if (disableRemovedFeatures)
        {
            // 旧配置里的 AutoHide 属于早期未完成功能，读入时关掉一次；
            // 用户之后可以在设置页重新打开（fork 已实现 1.3.3 的显示方式）。
            Dock.AutoHide = false;
        }
        Launcher.FullscreenMode = false;
        Shortcuts.HotCornersEnabled = false;
        Shortcuts.MouseSideButtonsEnabled = false;

        // Keep legacy enum values readable, but product settings have one appearance.
        Appearance.Theme = ThemeMode.Dark;
        if (Appearance.GlassMaterial != GlassMaterialMode.FrostedGlass)
            Appearance.GlassMaterial = GlassMaterialMode.AndroidLiquidGlass;
        Dock.SizeScale = double.IsFinite(Dock.SizeScale)
            ? Math.Clamp(Dock.SizeScale, DockSettings.MinimumSizeScale, DockSettings.MaximumSizeScale) : 1;
        // Settings now owns its opaque surface instead of changing every window.
        Appearance.ReduceTransparency = false;
        Appearance.DockGlassTransparency = double.IsFinite(Appearance.DockGlassTransparency)
            ? Math.Clamp(Appearance.DockGlassTransparency, 0, 1) : AppearanceSettings.DefaultDockGlassTransparency;
        Appearance.LauncherGlassTransparency = double.IsFinite(Appearance.LauncherGlassTransparency)
            ? Math.Clamp(Appearance.LauncherGlassTransparency, 0, 1) : AppearanceSettings.DefaultLauncherGlassTransparency;
        Appearance.GlassTintOpacity = double.IsFinite(Appearance.GlassTintOpacity)
            ? Math.Clamp(Appearance.GlassTintOpacity, 0, 1) : AppearanceSettings.DefaultGlassTintOpacity;
        Appearance.GlassBackgroundClarity = double.IsFinite(Appearance.GlassBackgroundClarity)
            ? Math.Clamp(Appearance.GlassBackgroundClarity, 0, 1) : AppearanceSettings.DefaultGlassBackgroundClarity;

        Shortcuts.Normalize();
        DesktopTaskbar.Normalize();
    }
}
