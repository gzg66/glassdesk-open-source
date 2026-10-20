using System.Runtime.InteropServices;
using GlassDesk.Platform.Interop;

namespace GlassDesk.Platform.Shell;

/// <summary>
/// 取外壳为某个文件显示的<strong>本地化</strong>名称。
/// </summary>
/// <remarks>
/// 直接读文件名会得到「notepad.exe」这种内部名字；Windows 资源管理器显示的是
/// 「记事本」。差别来自外壳名称（<c>SIGDN_NORMALDISPLAY</c>），它由系统按当前
/// 显示语言给出，也是开始菜单与属性对话框里看到的那一个。
///
/// <para>
/// 外壳不认这个路径（比如刚被删掉、或目标本身不是外壳项）时返回 <c>null</c>，
/// 由调用方退回文件名——绝不能因为取名字失败就丢掉一个可添加的项。
/// </para>
/// </remarks>
internal static class ShellDisplayName
{
    /// <summary>尽力取本地化显示名；取不到返回 <c>null</c>。</summary>
    public static string? TryResolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        nint itemIdList = 0;
        nint namePointer = 0;

        try
        {
            var hr = Shell32.SHParseDisplayName(path, 0, out itemIdList, 0, out _);
            if (hr != Ole32.SoK || itemIdList == 0)
            {
                return null;
            }

            hr = Shell32.SHGetNameFromIDList(itemIdList, Shell32.SigdnNormalDisplay, out namePointer);
            if (hr != Ole32.SoK || namePointer == 0)
            {
                return null;
            }

            var name = Marshal.PtrToStringUni(namePointer);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (namePointer != 0)
            {
                Ole32.CoTaskMemFree(namePointer);
            }

            // PIDL 必须用 ILFree 释放，不能走 CoTaskMemFree。
            if (itemIdList != 0)
            {
                Shell32.ILFree(itemIdList);
            }
        }
    }
}
