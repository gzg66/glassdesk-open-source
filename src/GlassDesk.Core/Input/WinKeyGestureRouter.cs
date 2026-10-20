namespace GlassDesk.Core.Input;

/// <summary>Win 键拦截器的当前状态：Win 键是否按下、是否已经为组合键补发过、期间是否按过别的键。</summary>
public readonly record struct WinKeyState(bool WinDown, bool Replayed, bool ChordSeen)
{
    public static WinKeyState Idle => default;
}

/// <summary>
/// 处理一个键盘事件后要做的事。由平台层翻译成 SendInput 调用与钩子返回值。
/// </summary>
/// <param name="State">本次事件之后的状态。</param>
/// <param name="Swallow">是否吞掉这个事件（返回 1，不再传给系统）。</param>
/// <param name="ReplayWinDown">是否补发一次 Win 按下，让 Win+X 这类组合键仍然成立。</param>
/// <param name="ReplayWinUp">是否补发一次 Win 抬起，结束上一步补发的按下。</param>
/// <param name="ToggleLauncher">是否把这次单独轻按 Win 当成「开关启动台」。</param>
public readonly record struct WinKeyDecision(
    WinKeyState State,
    bool Swallow,
    bool ReplayWinDown,
    bool ReplayWinUp,
    bool ToggleLauncher);

/// <summary>
/// 判断 Win 键轻按要不要被吃掉、组合键要不要放行。
/// </summary>
/// <remarks>
/// 只处理「物理」Win 键：注入事件（<c>LLKHF_INJECTED</c>）由调用方直接放行，
/// 否则补发的按下会被自己再吃一遍。
/// 轻按 Win 会吞掉按下与抬起，所以系统看不到它、开始菜单也不会弹出；
/// 但用户按住 Win 再按别的键时，必须先补发一次 Win 按下，
/// 否则 Win+E / Win+R 这类组合键会退化成单独的 E / R。
/// </remarks>
public static class WinKeyGestureRouter
{
    public static WinKeyDecision OnKeyDown(in WinKeyState state, bool isWinKey)
    {
        if (isWinKey)
        {
            // 长按产生的重复按下也要吃掉，否则系统会看到半次按键。
            return new WinKeyDecision(
                state with { WinDown = true },
                Swallow: true,
                ReplayWinDown: false,
                ReplayWinUp: false,
                ToggleLauncher: false);
        }

        if (!state.WinDown)
            return new WinKeyDecision(state, false, false, false, false);

        var replayed = state.Replayed;
        var decision = new WinKeyDecision(
            state with { ChordSeen = true, Replayed = true },
            Swallow: false,
            ReplayWinDown: !replayed,
            ReplayWinUp: false,
            ToggleLauncher: false);
        return decision;
    }

    public static WinKeyDecision OnKeyUp(in WinKeyState state, bool isWinKey)
    {
        if (!isWinKey || !state.WinDown)
            return new WinKeyDecision(state, false, false, false, false);

        if (state.Replayed)
        {
            // 组合键用过补发的按下，抬起也要补发，否则系统会以为 Win 还按着。
            return new WinKeyDecision(WinKeyState.Idle, Swallow: false, ReplayWinDown: false, ReplayWinUp: true, ToggleLauncher: false);
        }

        return new WinKeyDecision(WinKeyState.Idle, Swallow: true, ReplayWinDown: false, ReplayWinUp: false, ToggleLauncher: true);
    }
}
