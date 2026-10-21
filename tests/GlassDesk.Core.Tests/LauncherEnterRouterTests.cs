using GlassDesk.Core.Input;
using GlassDesk.Core.Models;

namespace GlassDesk.Core.Tests;

public sealed class LauncherEnterRouterTests
{
    private static LauncherViewState SearchView(int rows = 5, int columns = 7, int matches = 3)
    {
        var view = new LauncherViewState(rows, columns) { Query = "wechat" };
        view.SetItemCount(matches);
        return view;
    }

    [Fact]
    public void Enter_OpensFirstMatch_WhenSearchHasResults()
    {
        var decision = LauncherEnterRouter.OnEnter(SearchView(), LauncherEnterContext.Idle);
        Assert.True(decision.Activate);
        Assert.Equal(0, decision.SlotIndex);
    }

    [Fact]
    public void Enter_IsIgnored_WhenNotSearching()
    {
        var view = new LauncherViewState(5, 7);
        view.SetItemCount(3);
        Assert.False(LauncherEnterRouter.OnEnter(view, LauncherEnterContext.Idle).Activate);
    }

    [Fact]
    public void Enter_IsIgnored_WhenSearchHasNoMatch()
    {
        var decision = LauncherEnterRouter.OnEnter(SearchView(matches: 0), LauncherEnterContext.Idle);
        Assert.False(decision.Activate);
    }

    [Fact]
    public void Enter_IsIgnored_WhileImeIsComposing()
    {
        var decision = LauncherEnterRouter.OnEnter(SearchView(), LauncherEnterContext.Idle with { ImeComposing = true });
        Assert.False(decision.Activate);
    }

    [Fact]
    public void Enter_IsIgnored_WhileDialogMenuOrDragIsOpen()
    {
        var decision = LauncherEnterRouter.OnEnter(SearchView(), LauncherEnterContext.Idle with { InteractionBusy = true });
        Assert.False(decision.Activate);
    }

    [Fact]
    public void Enter_IsIgnored_WhenGroupPanelIsOpen()
    {
        var view = SearchView();
        view.OpenGroupId = "group";
        Assert.False(LauncherEnterRouter.OnEnter(view, LauncherEnterContext.Idle).Activate);
    }

    [Fact]
    public void Enter_TargetsFirstVisibleSlot_OnLaterSearchPage()
    {
        var view = SearchView(rows: 2, columns: 2, matches: 6);
        view.MoveToPage(1);
        var decision = LauncherEnterRouter.OnEnter(view, LauncherEnterContext.Idle);
        Assert.True(decision.Activate);
        Assert.Equal(4, decision.SlotIndex);
    }
}
