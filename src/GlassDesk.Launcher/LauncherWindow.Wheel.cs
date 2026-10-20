using System.Windows.Input;
using GlassDesk.Core.Models;

namespace GlassDesk.Launcher;

public partial class LauncherWindow
{
    /// <summary>
    /// 滚轮翻页。
    /// </summary>
    /// <remarks>
    /// 判定交给 <see cref="LauncherWheelNavigation"/>（有单测）：页内还能滚就先滚内容，
    /// 滚到边才翻页。搜索中、打开分组面板时不翻页——那两个状态下的滚轮属于内容本身。
    /// </remarks>
    private void OnLauncherWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || _view.IsSearching || _view.OpenGroupId is not null)
        {
            return;
        }

        var canScrollUp = PageScroll.VerticalOffset > 0.5;
        var canScrollDown = PageScroll.VerticalOffset < PageScroll.ScrollableHeight - 0.5;

        var delta = LauncherWheelNavigation.Decide(e.Delta, _view.Page, _view.PageCount, canScrollUp, canScrollDown);
        if (delta == 0)
        {
            return;
        }

        NavigatePage(delta);
        e.Handled = true;
    }
}
