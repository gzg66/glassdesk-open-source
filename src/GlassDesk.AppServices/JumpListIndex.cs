using GlassDesk.Core.Models;
using GlassDesk.Platform.Shell;

namespace GlassDesk.AppServices;

/// <summary>
/// 把跳转列表任务按可执行文件聚类，并匹配到应用库里的条目。
/// </summary>
/// <remarks>
/// 这一层刻意是纯函数：不读文件、不碰 COM、不缓存。所有分支都能被穷举测试，
/// 而「哪个任务属于哪个程序」这种容易挂错程序的判断必须被测住。
///
/// <para>
/// <b>归属只按任务自己的目标 exe 走，不做「文件主 exe」推断。</b>
/// 实测一个程序（VS Code / Cursor / Trae）的跳转列表里会混着指向
/// <c>explorer.exe</c> 的条目；推断会把它们算到编辑器头上。
/// </para>
/// </remarks>
internal static class JumpListIndex
{
    /// <summary>一个程序最多显示几条任务。菜单太高会超出屏幕，Windows 自己也有限制。</summary>
    internal const int MaxTasksPerApp = 8;

    /// <summary>
    /// 把路径规范成索引键；不是可执行文件时返回空串。
    /// </summary>
    /// <remarks>
    /// 只做「展开环境变量 + 取完整路径」。<b>不</b>做 8.3 短名还原：实测系统
    /// <c>IShellLink::GetPath</c> 给的就是长名（Edge 那份带 <c>PROGRA~2</c> 的文件读出来仍是
    /// <c>Program Files (x86)</c>），而剩下的短名差异由「文件名唯一」回退兜住。
    /// </remarks>
    internal static string NormalizeExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return string.Empty;

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 建索引：键是规范化后的 exe 路径，值是该程序的任务（已去重、已排序）。
    /// </summary>
    /// <remarks>
    /// 去重按<strong>标题</strong>而不是「标题+参数」：实测同一台机器上 Chrome / Edge 会有多份
    /// 跳转列表（普通档案夹 + puppeteer / Playwright 的临时档案夹），它们注册的是同一个任务，
    /// 只是参数里多了一个 <c>--user-data-dir</c>。按标题去重并保留<strong>参数最短</strong>那条，
    /// 得到的就是用户平时点的那个（临时档案夹的参数必然更长）。
    /// </remarks>
    internal static Dictionary<string, IReadOnlyList<JumpListTask>> Build(IEnumerable<JumpListTask> tasks)
    {
        var grouped = new Dictionary<string, List<JumpListTask>>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks ?? [])
        {
            var key = NormalizeExecutable(task.ExecutablePath);
            if (key.Length == 0) continue;

            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(task);
        }

        var index = new Dictionary<string, IReadOnlyList<JumpListTask>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in grouped)
        {
            index[pair.Key] = pair.Value
                .OrderByDescending(task => task.SourceFileWriteTimeUtc)
                .GroupBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(task => task.Arguments.Length)
                    .ThenByDescending(task => task.SourceFileWriteTimeUtc)
                    .First())
                .ToList();
        }

        return index;
    }

    /// <summary>
    /// 找出属于 <paramref name="entry"/> 的任务。
    /// </summary>
    /// <param name="entry">应用库里的条目。</param>
    /// <param name="index">由 <see cref="Build"/> 建立的索引。</param>
    /// <param name="resolveShortcut">
    /// 把 <c>.lnk</c> 解成真实目标；为 null 时用 <see cref="ShortcutResolver"/>。
    /// </param>
    /// <remarks>
    /// 五级回退，命中即止，每一级都要求<strong>唯一</strong>：
    /// 精确路径 → 快捷方式解出的目标 → 文件名唯一 → 去掉 <c>_proxy</c> 后唯一。
    /// 为什么需要回退：Edge 的目标可能写成 <c>%ProgramFiles(x86)%</c>，iTunes 装在
    /// 带版本号的 <c>WindowsApps</c> 目录里（升级后路径就变），而 Edge / Chrome 还有
    /// <c>msedge_proxy.exe</c> / <c>chrome_proxy.exe</c> 这种代理 exe 的先例。
    /// 为什么要求唯一：机器上常有多个 <c>chrome.exe</c>（本体、Chrome for Testing、Playwright），
    /// 歧义时宁可没有任务，也不能把别的程序的任务挂上来。
    /// <para>
    /// 刻意<strong>没有</strong>「同目录下唯一就算」这一级：同一目录常常住着另一个程序
    /// （<c>powershell.exe</c> 与 <c>powershell_ise.exe</c> 都在 System32 那个目录里），
    /// 按目录猜会把邻居的任务挂过来——用户看到的是「右键 A，点出来的却是 B」。
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<JumpListTask> Match(
        AppEntry? entry,
        IReadOnlyDictionary<string, IReadOnlyList<JumpListTask>> index,
        Func<string, string?>? resolveShortcut = null)
    {
        if (entry is null || index.Count == 0) return [];

        var candidate = ResolveCandidate(entry.Target, resolveShortcut);
        if (candidate.Length == 0) return [];

        if (index.TryGetValue(candidate, out var exact)) return Cap(exact);

        var fileName = Path.GetFileName(candidate);

        var byFileName = index
            .Where(pair => string.Equals(Path.GetFileName(pair.Key), fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byFileName.Count == 1) return Cap(byFileName[0].Value);

        var stem = StripProxySuffix(Path.GetFileNameWithoutExtension(candidate));
        var byProxy = index
            .Where(pair => string.Equals(
                StripProxySuffix(Path.GetFileNameWithoutExtension(pair.Key)),
                stem,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return byProxy.Count == 1 ? Cap(byProxy[0].Value) : [];
    }

    /// <summary>把条目目标解成可执行文件路径；解不出来返回空串。</summary>
    private static string ResolveCandidate(string? target, Func<string, string?>? resolveShortcut)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;

        var normalized = NormalizeExecutable(target);
        if (normalized.Length > 0) return normalized;

        if (!target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;

        var resolve = resolveShortcut ?? (path => ShortcutResolver.Resolve(path)?.Target);

        try
        {
            return NormalizeExecutable(resolve(target));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _ = ex;
            return string.Empty;
        }
    }

    private static string StripProxySuffix(string fileName) =>
        fileName.EndsWith("_proxy", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^"_proxy".Length]
            : fileName;

    private static IReadOnlyList<JumpListTask> Cap(IReadOnlyList<JumpListTask> tasks) =>
        tasks.Count <= MaxTasksPerApp ? tasks : tasks.Take(MaxTasksPerApp).ToList();
}
