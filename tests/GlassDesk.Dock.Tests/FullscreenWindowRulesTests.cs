using GlassDesk.Platform.Shell;

namespace GlassDesk.Dock.Tests;

/// <summary>
/// 最大化但带标题栏的窗口不能被当成全屏应用，否则任务栏隐藏后 Dock 会进游戏休眠，
/// 在最大化浏览器前永远唤不出来。
/// </summary>
public sealed class FullscreenWindowRulesTests
{
    [Fact]
    public void Maximized_window_with_caption_covering_monitor_is_ordinary_window()
        => Assert.True(FullscreenWindowRules.IsOrdinaryMaximizedWindow(coversMonitor: true, isMaximized: true, hasCaption: true));

    [Fact]
    public void Borderless_fullscreen_game_is_not_an_ordinary_window()
        => Assert.False(FullscreenWindowRules.IsOrdinaryMaximizedWindow(coversMonitor: true, isMaximized: true, hasCaption: false));

    [Fact]
    public void Normal_window_never_counts_even_with_caption()
        => Assert.False(FullscreenWindowRules.IsOrdinaryMaximizedWindow(coversMonitor: false, isMaximized: false, hasCaption: true));

    [Fact]
    public void Window_that_does_not_cover_the_monitor_is_not_fullscreen_either()
        => Assert.False(FullscreenWindowRules.IsOrdinaryMaximizedWindow(coversMonitor: false, isMaximized: true, hasCaption: true));
}
