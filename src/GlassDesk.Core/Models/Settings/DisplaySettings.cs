namespace GlassDesk.Core.Models.Settings;

/// <summary>与显示器和 DPI 相关的行为。</summary>
public sealed class DisplaySettings
{
    /// <summary>
    /// 旧版本 Dock 显示器选择器的兼容字段。Chapter 11 起生产 Dock 永远使用进程启动时
    /// Windows 选定的 Primary Monitor，此字段不再参与窗口放置。
    /// </summary>
    public string? DockDisplayId { get; set; }

    /// <summary>
    /// 旧版本跨显示器跟随设置的兼容字段。生产启动台始终使用进程启动时绑定的 Primary Monitor。
    /// </summary>
    public bool LauncherFollowsPointer { get; set; } = true;

    /// <summary>
    /// 前台运行全屏应用时，抑制热角、边缘触发与鼠标侧键触发，
    /// 使它们不会打断游戏或视频。
    /// </summary>
    public bool SuppressTriggersWhenFullscreen { get; set; } = true;
}

/// <summary>
/// 桌面图标与任务栏。
/// </summary>
/// <remarks>
/// 这里的一切默认都是「不打扰 Windows」。计划书明确要求：
/// 除非用户主动提出，否则不得修改任务栏；边缘触发拦截属于
/// 实验性功能，默认保持关闭。
/// </remarks>
public sealed class DesktopTaskbarSettings
{
    public HotkeyDefinition ToggleDesktopIcons { get; set; } = new();

    /// <summary>
    /// 实验性功能。在某个 Windows 版本上提供它之前，需要逐版本验证；
    /// 未知版本必须报告为不支持，而不是靠猜测。
    /// </summary>
    public bool BlockTaskbarEdgeReveal { get; set; }

    /// <summary>
    /// 仅反映 Windows 的自动隐藏设置；修改它时写入的是有文档记载的
    /// shell 设置，而不是去篡改任务栏窗口。
    /// </summary>
    public bool TaskbarAutoHideEnabled { get; set; }

    /// <summary>
    /// 接管 Windows 原生任务栏：隐藏任务栏窗口，并把单独轻按 Win 键换成
    /// 开关 GlassDesk 启动台。默认关闭——不打扰 Windows 是本类的既定约定，
    /// 只有用户主动打开这项设置才动手，退出产品时恢复原状。
    /// </summary>
    public bool HideNativeTaskbar { get; set; }

    internal void Normalize()
    {
        ToggleDesktopIcons ??= new HotkeyDefinition();
    }
}
