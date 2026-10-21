using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using GlassDesk.Platform.Interop;

namespace GlassDesk.Platform.Shell;

/// <summary>
/// 一个程序在 Windows 跳转列表里注册的任务（「新建窗口」这类）。
/// </summary>
/// <param name="ExecutablePath">任务要启动的可执行文件（link 里的目标，环境变量已展开）。</param>
/// <param name="Arguments">命令行参数，原样保留，不做转义或重排。</param>
/// <param name="WorkingDirectory">工作目录；link 没写时为空串。</param>
/// <param name="Title">任务显示名，是程序自己写进去的、已本地化的文本。</param>
/// <param name="SourceFile">这条任务来自哪个 <c>*.customDestinations-ms</c>。</param>
/// <param name="SourceFileWriteTimeUtc">来源文件的最后写入时间，用于合并多份档案时排序。</param>
public sealed record JumpListTask(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string Title,
    string SourceFile,
    DateTime SourceFileWriteTimeUtc);

/// <summary>
/// 读 Windows 跳转列表的「任务」部分。
/// </summary>
/// <remarks>
/// 数据来源是用户配置目录下的
/// <c>%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms</c>，
/// <strong>不是程序的安装目录</strong>：任务由程序运行时自己写进这份存储。
/// 因此「每个程序都有」并不成立——记事本、计算器、多数 UWP 本来就没有，这是
/// Windows 的行为而不是读取失败。
///
/// <para>
/// link 里的目标、参数、工作目录交给系统 <c>IShellLink</c> 解析（经
/// <c>IPersistStream</c> 从内存流加载），不自己解 MS-SHLLINK 的编码分支；
/// 标题由 <see cref="JumpListFormat"/> 按存储块锚点取。任何一步失败都只跳过该条目，
/// 绝不抛出——一个坏文件不该让右键菜单打不开。
/// </para>
/// </remarks>
public static class JumpListTaskReader
{
    /// <summary>跳转列表存储文件的扩展名。</summary>
    private const string FilePattern = "*.customDestinations-ms";

    /// <summary>取字符串用的缓冲区字符数，与 <see cref="ShortcutResolver"/> 一致。</summary>
    private const int PathCapacity = 1024;

    /// <summary>单个 item 区域最多交给 COM 的字节数，防一份异常文件拖垮解析。</summary>
    private const int MaxRegionBytes = 256 * 1024;

    /// <summary>
    /// 单个跳转列表文件的大小上限。实测最大的文件不到 20KB；超过这个量级说明文件坏了。
    /// </summary>
    /// <remarks>
    /// 必须有这道闸：<see cref="File.ReadAllBytes(string)"/> 读一个异常大的文件会抛
    /// <see cref="OutOfMemoryException"/>，而这条路径上所有的 catch 都刻意放行
    /// <c>OutOfMemoryException</c>（进程已不可信），于是它会一路穿到 UI 线程上把程序带崩。
    /// </remarks>
    private const long MaxFileBytes = 8L * 1024 * 1024;

    /// <summary>一个进程内共享的包装器；调用一律带 <c>UniqueInstance</c>，不做缓存。</summary>
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>
    /// 读整个用户最近的跳转列表目录。
    /// </summary>
    /// <param name="recentFolder"><c>...\Microsoft\Windows\Recent</c>（不含 <c>CustomDestinations</c>）。</param>
    public static IReadOnlyList<JumpListTask> ReadAll(string recentFolder)
    {
        if (string.IsNullOrWhiteSpace(recentFolder)) return [];

        var directory = Path.Combine(recentFolder, "CustomDestinations");
        string[] files;

        try
        {
            if (!Directory.Exists(directory)) return [];
            files = Directory.GetFiles(directory, FilePattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        // 越新的文件排在前面：一个程序可能有多份档案（不同配置文件/不同安装路径），
        // 新档案里的任务更贴近用户现在的用法。
        var ordered = files
            .Select(path => (Path: path, WriteTime: TryReadWriteTime(path)))
            .OrderByDescending(item => item.WriteTime)
            .ToList();

        var tasks = new List<JumpListTask>();
        foreach (var file in ordered)
        {
            tasks.AddRange(ReadFile(file.Path));
        }

        return tasks;
    }

    /// <summary>
    /// 读单个跳转列表文件；读不出来返回空列表。
    /// </summary>
    public static IReadOnlyList<JumpListTask> ReadFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return [];

        byte[] bytes;
        DateTime writeTime;

        try
        {
            if (new FileInfo(filePath).Length > MaxFileBytes) return [];

            bytes = File.ReadAllBytes(filePath);
            writeTime = File.GetLastWriteTimeUtc(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }

        if (bytes.Length == 0) return [];

        var offsets = JumpListFormat.FindLinkOffsets(bytes);
        if (offsets.Count == 0) return [];

        // 只认「任务」分类（无名分类）里的条目。不筛的话，右键 Chrome 会得到几十条
        // puppeteer / Playwright 临时档案夹的「最近关闭」——那是数据不是任务。
        var taskOffsets = JumpListFormat.FindTaskLinkOffsets(bytes, offsets);
        if (taskOffsets.Count == 0) return [];
        var taskSet = new HashSet<int>(taskOffsets);

        var tasks = new List<JumpListTask>(taskOffsets.Count);

        for (var index = 0; index < offsets.Count; index++)
        {
            var start = offsets[index];
            if (!taskSet.Contains(start)) continue;

            var end = index + 1 < offsets.Count ? offsets[index + 1] : bytes.Length;
            var regionLength = end - start;

            if (regionLength <= 0) continue;

            var title = JumpListFormat.TryReadTitle(bytes.AsSpan(start, regionLength));
            if (title is null) continue;

            if (!TryReadLink(bytes, start, Math.Min(regionLength, MaxRegionBytes), out var path, out var arguments, out var workingDirectory))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(path)) continue;

            tasks.Add(new JumpListTask(path, arguments, workingDirectory, title, filePath, writeTime));
        }

        return tasks;
    }

    /// <summary>用系统 <c>IShellLink</c> 解析一段嵌在文件里的 link 字节。</summary>
    private static bool TryReadLink(
        byte[] bytes,
        int offset,
        int length,
        out string path,
        out string arguments,
        out string workingDirectory)
    {
        path = string.Empty;
        arguments = string.Empty;
        workingDirectory = string.Empty;

        IShellLinkW? link = null;
        IPersistStream? persist = null;
        nint linkPointer = 0;
        nint persistPointer = 0;
        nint streamPointer = 0;
        nint source = 0;

        try
        {
            var hr = Ole32.CoCreateInstance(
                in ShellComIds.ShellLinkClass,
                0,
                Ole32.ClsCtxInprocServer,
                in ShellComIds.ShellLinkW,
                out linkPointer);

            if (hr != Ole32.SoK || linkPointer == 0) return false;

            link = (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(linkPointer, CreateObjectFlags.UniqueInstance);

            var query = Marshal.QueryInterface(linkPointer, in ShellComIds.PersistStream, out persistPointer);
            if (query != Ole32.SoK || persistPointer == 0) return false;

            persist = (IPersistStream)Wrappers.GetOrCreateObjectForComInstance(persistPointer, CreateObjectFlags.UniqueInstance);

            source = Marshal.AllocHGlobal(length);
            Marshal.Copy(bytes, offset, source, length);

            streamPointer = Shlwapi.SHCreateMemStream(source, (uint)length);
            if (streamPointer == 0) return false;

            // 加载失败会抛异常（源生成的互操作把 HRESULT 映射成 BCL 异常）；
            // 截断或坏掉的 link 就走这条路径，由外面的 catch 统一跳过。
            persist.Load(streamPointer);

            path = Read(link, static (instance, buffer, capacity) => instance.GetPath(buffer, capacity, 0, 0));
            arguments = Read(link, static (instance, buffer, capacity) => instance.GetArguments(buffer, capacity));
            workingDirectory = Read(link, static (instance, buffer, capacity) => instance.GetWorkingDirectory(buffer, capacity));

            return !string.IsNullOrWhiteSpace(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 与 ShortcutResolver 同样的取舍：源生成的互操作映射面很宽（实测一个不存在的
            // 目标会抛 FileNotFoundException 而不是 COMException），列举类型必输。
            // 契约是「读不出来就跳过这一条」，不是「把整个菜单带走」。
            _ = ex;
            return false;
        }
        finally
        {
            if (streamPointer != 0) Marshal.Release(streamPointer);
            if (source != 0) Marshal.FreeHGlobal(source);

            if (persist is IDisposable disposablePersist) disposablePersist.Dispose();
            if (link is IDisposable disposableLink) disposableLink.Dispose();

            if (persistPointer != 0) Marshal.Release(persistPointer);
            if (linkPointer != 0) Marshal.Release(linkPointer);
        }
    }

    /// <summary>在非托管缓冲区上调用一个「写字符串」的方法，再把它读回托管字符串。</summary>
    private static string Read(IShellLinkW link, Action<IShellLinkW, nint, int> read)
    {
        var byteCount = PathCapacity * sizeof(char);
        var buffer = Marshal.AllocHGlobal(byteCount);

        try
        {
            // 先清零：方法什么都没写时读回来的是空串而不是垃圾。
            unsafe
            {
                new Span<byte>((void*)buffer, byteCount).Clear();
            }

            read(link, buffer, PathCapacity);

            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static DateTime TryReadWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }
}
