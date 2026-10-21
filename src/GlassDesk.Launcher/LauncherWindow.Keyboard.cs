using System.Windows;
using System.Windows.Input;
using GlassDesk.Core.Input;

namespace GlassDesk.Launcher;

/// <summary>启动台的键盘操作：搜索后按回车直接打开第一个匹配的应用。</summary>
/// <remarks>
/// 用窗口级 <c>PreviewKeyDown</c> 而不是给 <c>SearchBox</c> 挂事件：WPF 的窗口内部焦点可能被
/// 点过的按钮、托盘面板或切换动画带走，但用户心里「我在搜索」这件事没变。
/// 反过来，若焦点真的落在某个按钮上（Tab 到「添加」/翻页箭头），回车应该按那个按钮，
/// 所以只在搜索框或窗口自身持有键盘焦点时才接管。
/// </remarks>
public partial class LauncherWindow
{
    private LauncherEnterContext CurrentEnterContext() => new(
        _imeComposing,
        _keepOpenWhileModalCount > 0 || _openContextMenuCount > 0
        || _dragEdit is not null || _dragOrigin is not null);

    private void OnLauncherPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled) return;
        if (!KeyboardFocusIsSearch()) return;
        if (!TryActivateFirstSearchMatch()) return;
        e.Handled = true;
    }

    private bool KeyboardFocusIsSearch() =>
        Keyboard.FocusedElement is null or LauncherWindow
        || ReferenceEquals(Keyboard.FocusedElement, SearchBox);

    private bool TryActivateFirstSearchMatch()
    {
        var decision = LauncherEnterRouter.OnEnter(_view, CurrentEnterContext());
        if (!decision.Activate) return false;
        var localIndex = decision.SlotIndex - _view.Page * _view.Capacity;
        if (localIndex < 0 || localIndex >= TileGrid.Children.Count) return false;
        if (TileGrid.Children[localIndex] is not LauncherAppTileButton { Tag: string id }) return false;
        // 搜索结果里没有分组槽位，tile 的 Tag 就是应用 Id。
        ActivateEntry(id);
        return true;
    }
}
