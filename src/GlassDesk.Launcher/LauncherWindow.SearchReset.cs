using System.Windows.Controls;

namespace GlassDesk.Launcher;

/// <summary>
/// 每次唤出启动台都从空搜索开始：上次搜到一半的过滤词不该留到下一次。
/// </summary>
/// <remarks>
/// 上游只在回忆上次页数（<c>RememberLastPage</c>）时保留状态，搜索框的文本属于
/// 「这一次要找什么」，留着只会让用户先删一遍。清空放在显示之前，
/// 首帧就是完整列表，不会看到旧结果闪一下。
/// </remarks>
public partial class LauncherWindow
{
    /// <summary>清空搜索框（以及它带出来的过滤结果）。</summary>
    public void ResetSearchForOpen()
    {
        if (string.IsNullOrEmpty(SearchBox.Text)) return;
        SearchBox.Text = string.Empty;
        SearchBox.CaretIndex = 0;
    }
}
