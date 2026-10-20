using GlassDesk.Core.Input;

namespace GlassDesk.Core.Tests;

/// <summary>
/// 回归：补发组合键前缀时，补发的必须始终是 Win 键本身。
/// 曾经的实现拿「触发组合键的那个字母键」去补发，于是 Win+D 变成单独按了 D，
/// Win 组合键整体失效，只有轻按 Win 打开启动台还正常。
/// </summary>
public sealed class WinKeyIdentityTests
{
    [Fact]
    public void Non_win_event_keeps_remembered_win_key_instead_of_taking_the_letter()
    {
        var remembered = WinKeyIdentity.LeftWin;

        var identity = WinKeyIdentity.FromEvent(0x44 /* D */, 0x20, 0x00, remembered);

        Assert.Equal(WinKeyIdentity.VkLWin, identity.VirtualKey);
        Assert.Equal(0x5Bu, identity.ScanCode);
        Assert.False(identity.Extended);
    }

    [Fact]
    public void Left_win_event_records_scan_code_from_the_event()
    {
        var identity = WinKeyIdentity.FromEvent(WinKeyIdentity.VkLWin, 0x5B, 0x01, WinKeyIdentity.LeftWin);

        Assert.Equal(WinKeyIdentity.VkLWin, identity.VirtualKey);
        Assert.Equal(0x5Bu, identity.ScanCode);
    }

    [Fact]
    public void Right_win_event_wins_over_remembered_left_win()
    {
        var identity = WinKeyIdentity.FromEvent(WinKeyIdentity.VkRWin, 0x5C, 0x01, WinKeyIdentity.LeftWin);

        Assert.Equal(WinKeyIdentity.VkRWin, identity.VirtualKey);
        Assert.Equal(0x5Cu, identity.ScanCode);
        Assert.True(identity.Extended);
    }

    [Fact]
    public void Remembered_right_win_survives_a_letter_event()
    {
        var identity = WinKeyIdentity.FromEvent(0x45 /* E */, 0x12, 0x00, WinKeyIdentity.RightWin);

        Assert.Equal(WinKeyIdentity.VkRWin, identity.VirtualKey);
        Assert.True(identity.Extended);
    }
}
