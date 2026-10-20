namespace GlassDesk.Core.Input;

/// <summary>
/// 补发组合键前缀时用的「Win 键身份」：左 Win 还是右 Win、扫描码、是否扩展键。
/// </summary>
/// <remarks>
/// 这个类型存在的唯一原因是一个踩过的坑：低级键盘钩子吞掉物理 Win 之后，
/// 一旦发现用户按的是 Win+X，钩子必须自己补发一次「Win 按下」，系统才认这个组合键。
/// 补发用的<strong>是哪个键</strong>一旦搞错——比如拿组合键里那个字母键顶替 Win 键——
/// 系统看到的就是「单独按了 X」：Win+X 全部失效，而轻按 Win 打开产品启动台却照常工作，
/// 症状像是「Win 键被接管了」而不是「补发错了键」，极易误判。
/// 所以把「从事件里取身份」做成可以脱离 Win32 单测的纯函数，并记住最近一次真实按下的 Win 键。
/// </remarks>
public readonly record struct WinKeyIdentity(int VirtualKey, uint ScanCode, bool Extended)
{
    /// <summary>左 Win 键的虚拟键码。</summary>
    public const int VkLWin = 0x5B;

    /// <summary>右 Win 键的虚拟键码。</summary>
    public const int VkRWin = 0x5C;

    /// <summary><c>KBDLLHOOKSTRUCT.flags</c> 里的「扩展键」位，数值与 <c>KEYEVENTF_EXTENDEDKEY</c> 相同。</summary>
    private const uint LlkhfExtended = 0x01;

    /// <summary>左 Win：非扩展键，扫描码 0x5B。</summary>
    public static WinKeyIdentity LeftWin { get; } = new(VkLWin, 0x5B, Extended: false);

    /// <summary>右 Win：扩展键，扫描码 0x5C。</summary>
    public static WinKeyIdentity RightWin { get; } = new(VkRWin, 0x5C, Extended: true);

    /// <summary>
    /// 事件本身是 Win 键就按它更新身份；不是 Win 键就沿用 <paramref name="remembered"/>。
    /// </summary>
    /// <param name="virtualKey">本次事件的虚拟键码。</param>
    /// <param name="scanCode">本次事件的扫描码。</param>
    /// <param name="flags">本次事件在 <c>KBDLLHOOKSTRUCT.flags</c> 里的原始标志位。</param>
    /// <param name="remembered">上一次记住的 Win 键身份。</param>
    public static WinKeyIdentity FromEvent(int virtualKey, uint scanCode, uint flags, WinKeyIdentity remembered)
    {
        if (virtualKey is not (VkLWin or VkRWin)) return remembered;
        return new WinKeyIdentity(virtualKey, scanCode, (flags & LlkhfExtended) != 0);
    }
}
