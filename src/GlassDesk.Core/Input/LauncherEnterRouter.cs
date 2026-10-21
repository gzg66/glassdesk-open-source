using GlassDesk.Core.Models;

namespace GlassDesk.Core.Input;

/// <summary>回车时刻的界面状态：输入法是否正在组字、是否有对话框/右键菜单/拖拽占着输入。</summary>
public readonly record struct LauncherEnterContext(bool ImeComposing, bool InteractionBusy)
{
    public static LauncherEnterContext Idle => default;
}

/// <summary>回车决定：是否由启动台接管，以及接管后打开当前页的第几个结果。</summary>
public readonly record struct LauncherEnterDecision(bool Activate, int SlotIndex);

/// <summary>
/// Win 唤出启动台 → 输入搜索 → 回车直接打开第一个匹配的应用。
/// </summary>
/// <remarks>
/// 只在「搜索中 + 当前页有结果 + 输入法没在组字 + 没有对话框/右键菜单/拖拽」时接管回车；
/// 其余情况返回不接管，让 WPF 把回车交给真正持有焦点的控件（例如「添加」按钮）。
/// 目标是**当前页第一个可见结果**：结果分页时按用户眼前看到的顺序打开，
/// 而不是跳到第一页里那条已经看不见的匹配。
/// </remarks>
public static class LauncherEnterRouter
{
    public static LauncherEnterDecision OnEnter(LauncherViewState view, LauncherEnterContext context)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (context.ImeComposing || context.InteractionBusy) return new LauncherEnterDecision(false, 0);
        if (!view.IsSearching || view.OpenGroupId is not null) return new LauncherEnterDecision(false, 0);
        if (view.VisibleCount <= 0) return new LauncherEnterDecision(false, 0);
        return new LauncherEnterDecision(true, view.Page * view.Capacity);
    }
}
