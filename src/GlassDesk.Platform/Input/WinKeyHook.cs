using System.Runtime.InteropServices;

namespace GlassDesk.Platform.Input;

/// <summary>一次按键事件的处置结果；由产品的纯逻辑层决定，钩子只负责执行。</summary>
/// <param name="Swallow">吞掉事件，不再传给系统。</param>
/// <param name="ReplayWinDown">补发一次 Win 按下（让 Win+X 组合键成立）。</param>
/// <param name="ReplayWinUp">补发一次 Win 抬起。</param>
/// <param name="ToggleLauncher">把这次轻按当成开关启动台。</param>
public readonly record struct WinKeyDirective(bool Swallow, bool ReplayWinDown, bool ReplayWinUp, bool ToggleLauncher);

/// <summary>
/// 低级键盘钩子：把单独轻按 Win 键改成产品动作，同时保留 Win+X 组合键。
/// </summary>
/// <remarks>
/// 必须在带消息循环的线程上 <see cref="TryStart"/>（产品里就是 UI 线程）：
/// <c>WH_KEYBOARD_LL</c> 的回调投递到安装它的那个线程。
/// 轻按的按下与抬起都被吞掉，系统因此看不到 Win 键，开始菜单不会弹出；
/// 一旦发现用户按了别的键，就先补发一次 Win 按下，让 Win+E 这类组合键照常工作，
/// 抬起时再补发一次 Win 抬起收尾。
/// 注入事件直接放行，否则自己补发的按下会被自己吞掉。
/// </remarks>
public sealed class WinKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const uint KeyeventfKeyup = 0x0002;
    private const nuint ReplayMarker = 0x4744_5701;

    private static WinKeyHook? _owner;

    private readonly HookProc _callback;
    private readonly Func<bool, bool, WinKeyDirective> _decide;
    private nint _handle;
    private bool _disposed;

    /// <param name="decide">参数依次为「是否 Win 键」「是否按下」，返回处置结果。</param>
    public WinKeyHook(Func<bool, bool, WinKeyDirective> decide)
    {
        _decide = decide ?? throw new ArgumentNullException(nameof(decide));
        _callback = OnHookMessage;
    }

    /// <summary>用户单独轻按 Win 键后触发。在安装钩子的线程上同步回调，监听方应自行转投递。</summary>
    public event Action? Tapped;

    public bool IsRunning => _handle != 0 && !_disposed;

    public bool TryStart(out string? error)
    {
        error = null;
        if (_disposed) { error = "Win 键钩子已关闭"; return false; }
        if (_handle != 0) return true;

        _owner = this;
        _handle = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_handle == 0)
        {
            var code = Marshal.GetLastWin32Error();
            _owner = null;
            error = $"安装键盘钩子失败（Win32 {code}）";
            return false;
        }

        return true;
    }

    public void Stop()
    {
        if (_handle == 0) return;
        UnhookWindowsHookEx(_handle);
        _handle = 0;
        if (ReferenceEquals(_owner, this)) _owner = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private nint OnHookMessage(int code, nint wParam, nint lParam)
    {
        if (code < 0) return CallNextHookEx(0, code, wParam, lParam);

        var message = (int)wParam;
        if (message is not (WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp))
            return CallNextHookEx(0, code, wParam, lParam);

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

        // 只放行自己补发的那些事件（靠 dwExtraInfo 标记识别）。
        // 不能用 LLKHF_INJECTED 一刀切：那样 SendInput 模拟的 Win 轻按会绕过接管，
        // 开始菜单照样弹出，用户看到的还是「Win 键没被换掉」。
        if ((data.Flags & LlkhfInjected) != 0 && data.ExtraInfo == ReplayMarker)
            return CallNextHookEx(0, code, wParam, lParam);

        var isWinKey = data.VirtualKey is VkLWin or VkRWin;
        var isDown = message is WmKeyDown or WmSysKeyDown;
        var decision = _decide(isWinKey, isDown);

        if (decision.ReplayWinDown) SendWinKey(keyUp: false, (int)data.VirtualKey);
        if (decision.ReplayWinUp) SendWinKey(keyUp: true, (int)data.VirtualKey);
        if (decision.ToggleLauncher) Tapped?.Invoke();

        return decision.Swallow ? 1 : CallNextHookEx(0, code, wParam, lParam);
    }

    private static void SendWinKey(bool keyUp, int virtualKey)
    {
        var input = new Input
        {
            Type = 1,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = 0,
                    Flags = keyUp ? KeyeventfKeyup : 0,
                    Time = 0,
                    ExtraInfo = ReplayMarker,
                },
            },
        };
        _ = SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint handle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint handle, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
