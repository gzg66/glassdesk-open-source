namespace GlassDesk.Core.Models;

/// <summary>
/// 启动台的滚轮翻页判定。
/// </summary>
/// <remarks>
/// 两条规则，与用户在启动台上的直觉一致：
///
/// <item>页内还有没滚到的内容时，滚轮先滚内容——否则图标多的页面会「一滚就跳页」，
/// 根本看不到下面的行。</item>
/// <item>已经滚到边（或根本没有可滚内容）才翻页；滚轮上推 = 上一页，下推 = 下一页。</item>
///
/// 单页、或者已经在第一页/最后一页、或者根本没有滚轮位移时返回 0，表示
/// 「这次滚轮不归翻页管」，由调用方决定是否让滚动条自己处理。
/// </remarks>
public static class LauncherWheelNavigation
{
    /// <summary>算出这次滚轮应该翻几页；<c>0</c> 表示不翻页。</summary>
    public static int Decide(int wheelDelta, int page, int pageCount, bool canScrollUp, bool canScrollDown)
    {
        if (wheelDelta == 0 || pageCount <= 1)
        {
            return 0;
        }

        // Windows 的滚轮：向前推（远离自己）为正值，通常表示「上一页」。
        var direction = wheelDelta > 0 ? -1 : 1;

        if (direction < 0 && canScrollUp)
        {
            return 0;
        }

        if (direction > 0 && canScrollDown)
        {
            return 0;
        }

        var target = page + direction;
        return target < 0 || target >= pageCount ? 0 : direction;
    }
}
