using GlassDesk.Core.Models;

namespace GlassDesk.Core.Tests;

/// <summary>启动台滚轮翻页：页内还能滚就先滚，到边才翻页。</summary>
public sealed class LauncherWheelNavigationTests
{
    [Fact]
    public void WheelDown_turns_to_next_page_when_content_cannot_scroll()
    {
        Assert.Equal(1, LauncherWheelNavigation.Decide(wheelDelta: -120, page: 0, pageCount: 2, canScrollUp: false, canScrollDown: false));
    }

    [Fact]
    public void WheelUp_turns_to_previous_page_when_content_cannot_scroll()
    {
        Assert.Equal(-1, LauncherWheelNavigation.Decide(wheelDelta: 120, page: 1, pageCount: 2, canScrollUp: false, canScrollDown: false));
    }

    [Fact]
    public void WheelUp_on_first_page_does_not_move()
    {
        Assert.Equal(0, LauncherWheelNavigation.Decide(wheelDelta: 120, page: 0, pageCount: 3, canScrollUp: false, canScrollDown: false));
    }

    [Fact]
    public void WheelDown_on_last_page_does_not_move()
    {
        Assert.Equal(0, LauncherWheelNavigation.Decide(wheelDelta: -120, page: 2, pageCount: 3, canScrollUp: false, canScrollDown: false));
    }

    [Fact]
    public void Single_page_never_turns()
    {
        Assert.Equal(0, LauncherWheelNavigation.Decide(wheelDelta: -120, page: 0, pageCount: 1, canScrollUp: false, canScrollDown: false));
    }

    [Fact]
    public void WheelDown_lets_the_scroll_viewer_consume_when_more_rows_below()
    {
        Assert.Equal(0, LauncherWheelNavigation.Decide(wheelDelta: -120, page: 0, pageCount: 2, canScrollUp: false, canScrollDown: true));
    }

    [Fact]
    public void WheelUp_lets_the_scroll_viewer_consume_when_more_rows_above()
    {
        Assert.Equal(0, LauncherWheelNavigation.Decide(wheelDelta: 120, page: 1, pageCount: 2, canScrollUp: true, canScrollDown: false));
    }

    [Fact]
    public void Zero_delta_is_ignored()
    {
        Assert.Equal(0, LauncherWheelNavigation.Decide(wheelDelta: 0, page: 0, pageCount: 2, canScrollUp: false, canScrollDown: false));
    }
}
