namespace GlassDesk.Platform.Shell;

/// <summary>桌面上的一个可添加项。</summary>
/// <param name="Path">该项的完整路径。</param>
/// <param name="Name">界面上显示的名字（本地化优先，取不到则退回文件名）。</param>
/// <param name="IsDirectory">是文件夹而不是文件。</param>
/// <param name="FromCurrentUser">来自当前用户的桌面，而不是所有用户共用的那份。</param>
public sealed record DesktopEntry(string Path, string Name, bool IsDirectory, bool FromCurrentUser);

/// <summary>一个扫描根。</summary>
/// <param name="Path">目录；为空或不存在时会被跳过。</param>
/// <param name="FromCurrentUser">当前用户优先。</param>
public sealed record DesktopScanRoot(string Path, bool FromCurrentUser);

/// <summary>一次桌面扫描的结果。</summary>
/// <param name="Entries">去重后的条目，按显示名排序。</param>
/// <param name="ScannedItemCount">去重前看到的条目总数。</param>
/// <param name="DuplicateNameCount">因同名被覆盖掉的数量。</param>
/// <param name="Roots">实际扫描到的根目录，便于诊断。</param>
public sealed record DesktopScanResult(
    IReadOnlyList<DesktopEntry> Entries,
    int ScannedItemCount,
    int DuplicateNameCount,
    IReadOnlyList<string> Roots);

/// <summary>
/// 扫描桌面上的可添加项，供「从桌面批量添加」使用。
/// </summary>
/// <remarks>
/// 与 <see cref="StartMenuScanner"/> 保持同样的两条规则，避免同一台机器上
/// 「开始菜单导入」和「桌面导入」给出不一样的结果：
///
/// <item>扫两个根：所有用户的公共桌面 + 当前用户的桌面。桌面可以被 OneDrive
/// 重定向，所以路径一律走 <see cref="KnownFolders"/>，不拼 <c>%USERPROFILE%</c>。</item>
/// <item><b>同名按当前用户优先</b>，与 Windows 自己的覆盖规则一致。</item>
///
/// <para>
/// <b>只扫顶层。</b>桌面是平铺的，文件夹本身就是可添加项；递归进去会把
/// 「一个文件夹」变成「文件夹 + 它的每个文件」，与用户在桌面上看到的不一致。
/// </para>
///
/// <para>
/// 跳过隐藏与系统项（<c>desktop.ini</c>、带 Hidden/System 属性的文件），
/// 它们不是用户放上去的快捷方式，加进启动台只会是噪音。
/// </para>
/// </remarks>
public static class DesktopEntryScanner
{
    private const string DesktopIniName = "desktop.ini";

    /// <summary>按扩展名去扩展名显示的类型：用户认的是「记事本」，不是「notepad.exe」。</summary>
    private static readonly HashSet<string> StripExtensionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".exe", ".com", ".bat", ".cmd", ".ps1",
    };

    /// <summary>扫描默认的两个桌面目录。</summary>
    public static DesktopScanResult Scan() => ScanWith(
        [
            new DesktopScanRoot(KnownFolders.CommonDesktop ?? string.Empty, FromCurrentUser: false),
            new DesktopScanRoot(KnownFolders.Desktop ?? string.Empty, FromCurrentUser: true),
        ],
        ShellDisplayName.TryResolve);

    /// <summary>
    /// 用注入的根目录与名称解析器扫描，便于测试（不依赖真实桌面与 COM）。
    /// </summary>
    internal static DesktopScanResult ScanWith(IReadOnlyList<DesktopScanRoot> roots, Func<string, string?> displayName)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(displayName);

        var scannedRoots = new List<string>();
        var candidates = new List<DesktopEntry>();

        foreach (var root in roots)
        {
            if (root is null || string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
            {
                continue;
            }

            scannedRoots.Add(root.Path);
            Collect(root.Path, root.FromCurrentUser, candidates, displayName);
        }

        var scanned = candidates.Count;

        // 同名覆盖：后面的根（当前用户）赢。
        var byName = new Dictionary<string, DesktopEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in candidates)
        {
            byName[entry.Name] = entry;
        }

        var entries = byName.Values
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();

        return new DesktopScanResult(entries, scanned, scanned - entries.Count, scannedRoots);
    }

    private static void Collect(string root, bool fromCurrentUser, List<DesktopEntry> into, Func<string, string?> displayName)
    {
        string[] directories;
        string[] files;

        try
        {
            directories = Directory.GetDirectories(root);
            files = Directory.GetFiles(root);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (IsSystemEntry(directory, isDirectory: true))
            {
                continue;
            }

            var name = displayName(directory) ?? FallbackName(directory, isDirectory: true);
            if (!string.IsNullOrWhiteSpace(name))
            {
                into.Add(new DesktopEntry(directory, name, IsDirectory: true, fromCurrentUser));
            }
        }

        foreach (var file in files)
        {
            if (IsSystemEntry(file, isDirectory: false))
            {
                continue;
            }

            var name = displayName(file) ?? FallbackName(file, isDirectory: false);
            if (!string.IsNullOrWhiteSpace(name))
            {
                into.Add(new DesktopEntry(file, name, IsDirectory: false, fromCurrentUser));
            }
        }
    }

    private static bool IsSystemEntry(string path, bool isDirectory)
    {
        if (!isDirectory && string.Equals(Path.GetFileName(path), DesktopIniName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 读不到属性时当作系统项跳过：看不见属性就没法判断它是不是用户放的。
            return true;
        }
    }

    /// <summary>外壳给不出名字时的退路：与 <c>ItemKindDetector</c> 的推导规则一致。</summary>
    private static string FallbackName(string path, bool isDirectory)
    {
        var fileName = isDirectory
            ? new DirectoryInfo(path).Name
            : Path.GetFileName(path);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (isDirectory)
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        return StripExtensionExtensions.Contains(extension)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }
}
