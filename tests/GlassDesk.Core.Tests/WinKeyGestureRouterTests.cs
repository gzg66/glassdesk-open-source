using GlassDesk.Core.Input;

namespace GlassDesk.Core.Tests;

/// <summary>
/// Win 键拦截：轻按 = 开关启动台且吞掉事件；按住后按别的键 = 组合键，必须放行。
/// </summary>
public sealed class WinKeyGestureRouterTests
{
    [Fact]
    public void SingleTap_swallows_both_events_and_toggles_launcher()
    {
        var down = WinKeyGestureRouter.OnKeyDown(WinKeyState.Idle, isWinKey: true);
        Assert.True(down.Swallow);
        Assert.False(down.ToggleLauncher);

        var up = WinKeyGestureRouter.OnKeyUp(down.State, isWinKey: true);
        Assert.True(up.Swallow);
        Assert.True(up.ToggleLauncher);
        Assert.Equal(WinKeyState.Idle, up.State);
    }

    [Fact]
    public void Chord_replays_win_down_so_combinations_still_work()
    {
        var down = WinKeyGestureRouter.OnKeyDown(WinKeyState.Idle, isWinKey: true);

        var e = WinKeyGestureRouter.OnKeyDown(down.State, isWinKey: false);
        Assert.False(e.Swallow);
        Assert.True(e.ReplayWinDown);
        Assert.False(e.ToggleLauncher);

        // 补发只做一次：同一次按住里再按第二个键不需要重复补发。
        var r = WinKeyGestureRouter.OnKeyDown(e.State, isWinKey: false);
        Assert.False(r.ReplayWinDown);

        var winUp = WinKeyGestureRouter.OnKeyUp(r.State, isWinKey: true);
        Assert.True(winUp.ReplayWinUp);
        Assert.False(winUp.Swallow);
        Assert.False(winUp.ToggleLauncher);
        Assert.Equal(WinKeyState.Idle, winUp.State);
    }

    [Fact]
    public void Repeated_tap_toggles_every_time()
    {
        var state = WinKeyState.Idle;
        var toggles = 0;
        for (var i = 0; i < 3; i++)
        {
            var down = WinKeyGestureRouter.OnKeyDown(state, isWinKey: true);
            var up = WinKeyGestureRouter.OnKeyUp(down.State, isWinKey: true);
            state = up.State;
            if (up.ToggleLauncher) toggles++;
        }

        Assert.Equal(3, toggles);
        Assert.Equal(WinKeyState.Idle, state);
    }

    [Fact]
    public void Holding_win_repeats_are_swallowed_without_toggling()
    {
        var down = WinKeyGestureRouter.OnKeyDown(WinKeyState.Idle, isWinKey: true);
        var repeat = WinKeyGestureRouter.OnKeyDown(down.State, isWinKey: true);

        Assert.True(repeat.Swallow);
        Assert.False(repeat.ToggleLauncher);
        Assert.False(repeat.ReplayWinDown);
    }

    [Fact]
    public void Other_keys_without_win_are_untouched()
    {
        var d = WinKeyGestureRouter.OnKeyDown(WinKeyState.Idle, isWinKey: false);
        var u = WinKeyGestureRouter.OnKeyUp(WinKeyState.Idle, isWinKey: false);

        Assert.False(d.Swallow);
        Assert.False(u.Swallow);
        Assert.False(d.ToggleLauncher);
        Assert.False(u.ToggleLauncher);
    }

    [Fact]
    public void Stray_win_release_without_press_is_ignored()
    {
        var up = WinKeyGestureRouter.OnKeyUp(WinKeyState.Idle, isWinKey: true);

        Assert.False(up.Swallow);
        Assert.False(up.ToggleLauncher);
        Assert.False(up.ReplayWinUp);
    }

    [Fact]
    public void After_chord_a_fresh_tap_toggles_again()
    {
        var down = WinKeyGestureRouter.OnKeyDown(WinKeyState.Idle, isWinKey: true);
        var e = WinKeyGestureRouter.OnKeyDown(down.State, isWinKey: false);
        var winUp = WinKeyGestureRouter.OnKeyUp(e.State, isWinKey: true);

        var tapDown = WinKeyGestureRouter.OnKeyDown(winUp.State, isWinKey: true);
        var tapUp = WinKeyGestureRouter.OnKeyUp(tapDown.State, isWinKey: true);

        Assert.True(tapUp.ToggleLauncher);
    }

    [Fact]
    public void Chord_key_release_does_not_end_win_press()
    {
        var down = WinKeyGestureRouter.OnKeyDown(WinKeyState.Idle, isWinKey: true);
        var e = WinKeyGestureRouter.OnKeyDown(down.State, isWinKey: false);
        var eUp = WinKeyGestureRouter.OnKeyUp(e.State, isWinKey: false);

        Assert.False(eUp.Swallow);
        Assert.False(eUp.ToggleLauncher);
        Assert.True(eUp.State.WinDown);

        var winUp = WinKeyGestureRouter.OnKeyUp(eUp.State, isWinKey: true);
        Assert.True(winUp.ReplayWinUp);
        Assert.False(winUp.ToggleLauncher);
    }
}
