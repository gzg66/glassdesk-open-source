using System.Runtime.InteropServices;

namespace GlassDesk.Platform.Interop;

/// <summary>
/// shlwapi 中用于把内存字节包装成 COM 流的入口点。
/// </summary>
/// <remarks>
/// 跳转列表文件里的 shell link 不是独立文件，而是一段嵌在大文件里的字节。
/// 系统只提供「按文件加载」（<c>IPersistFile</c>）与「按流加载」
/// （<c>IPersistStream</c>）两条路，所以要先把字节做成流。
/// 写临时文件再走 <c>IPersistFile</c> 也能work，但那会在用户机器上留下临时文件、
/// 还会被反病毒软件盯上，不如一次内存流干净。
/// </remarks>
internal static partial class Shlwapi
{
    /// <summary>
    /// 在内存上创建一个 <c>IStream</c>；失败返回 0。
    /// </summary>
    /// <remarks>
    /// 传裸指针而不是 <c>byte[]</c>：源生成的 P/Invoke 对数组参数有一套额外的封送规则，
    /// 这里只需要把一段字节读进去，用 <see cref="Marshal.AllocHGlobal"/> 复制一份更直白。
    /// 流会复制调用方给的字节，所以传完即可释放自己的副本。
    /// </remarks>
    [LibraryImport("shlwapi.dll")]
    internal static partial nint SHCreateMemStream(nint init, uint size);
}
