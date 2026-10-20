namespace GlassDesk.Platform.Shell;

/// <summary>
/// 把「几何上铺满屏幕」和「窗口样式」合起来，判断前台窗口算不算全屏应用。
/// </summary>
/// <remarks>
/// 任务栏自动隐藏（或被 GlassDesk 接管隐藏）之后，最大化窗口的可见边框与显示器
/// 完全重合，单看几何无法和独占全屏区分。可是最大化窗口只是普通桌面程序，
/// 用户在这种窗口前仍然需要 Dock 唤出；把它当成全屏会让 Dock 进「游戏休眠」，
/// 从此不再响应指针。
/// </remarks>
public static class FullscreenWindowRules
{
    /// <summary>
    /// 铺满屏幕、带最大化状态、并且有标题栏的普通窗口，按「不是全屏应用」处理。
    /// </summary>
    /// <remarks>
    /// 无边框全屏（游戏、视频播放器的全屏模式）没有 <c>WS_CAPTION</c>，
    /// 仍然会被判成全屏，游戏休眠行为保持不变。
    /// </remarks>
    public static bool IsOrdinaryMaximizedWindow(bool coversMonitor, bool isMaximized, bool hasCaption)
        => coversMonitor && isMaximized && hasCaption;
}
