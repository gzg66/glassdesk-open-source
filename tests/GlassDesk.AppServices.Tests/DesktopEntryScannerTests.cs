using GlassDesk.Platform.Shell;

namespace GlassDesk.AppServices.Tests;

/// <summary>
/// 桌面批量添加的扫描规则：只扫顶层、跳过系统项、同名按当前用户优先、
/// 名称优先用外壳给的本地化显示名。
/// </summary>
public sealed class DesktopEntryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GlassDesk-DesktopScan-" + Guid.NewGuid().ToString("N"));

    public DesktopEntryScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string MakeRoot(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string path, string content = "noop")
    {
        File.WriteAllText(path, content);
    }

    private static DesktopScanResult Scan(string common, string user, Func<string, string?>? resolver = null)
        => DesktopEntryScanner.ScanWith(
            [new DesktopScanRoot(common, FromCurrentUser: false), new DesktopScanRoot(user, FromCurrentUser: true)],
            resolver ?? (_ => null));

    [Fact]
    public void Scan_ignores_desktop_ini_and_hidden_files()
    {
        var common = MakeRoot("common");
        var user = MakeRoot("user");
        Write(Path.Combine(common, "desktop.ini"));
        Write(Path.Combine(common, "公开文档.txt"));
        var hidden = Path.Combine(user, "机密.txt");
        Write(hidden);
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        Write(Path.Combine(user, "普通文件.txt"));

        var result = Scan(common, user);

        Assert.DoesNotContain(result.Entries, e => e.Name.Equals("desktop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Entries, e => e.Name.Contains("机密"));
        Assert.Contains(result.Entries, e => e.Name == "公开文档.txt");
        Assert.Contains(result.Entries, e => e.Name == "普通文件.txt");
    }

    [Fact]
    public void Scan_includes_top_level_folders_but_not_their_contents()
    {
        var common = MakeRoot("common");
        var user = MakeRoot("user");
        var folder = Path.Combine(user, "工具");
        Directory.CreateDirectory(folder);
        Write(Path.Combine(folder, "内部文件.txt"));

        var result = Scan(common, user);

        var entry = Assert.Single(result.Entries, e => e.Path == folder);
        Assert.True(entry.IsDirectory);
        Assert.Equal("工具", entry.Name);
        Assert.True(entry.FromCurrentUser);
        Assert.DoesNotContain(result.Entries, e => e.Path.EndsWith("内部文件.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_prefers_current_user_for_same_display_name()
    {
        var common = MakeRoot("common");
        var user = MakeRoot("user");
        var shared = Path.Combine(common, "记事本.lnk");
        var personal = Path.Combine(user, "记事本.lnk");
        Write(shared);
        Write(personal);

        var result = Scan(common, user);

        var entry = Assert.Single(result.Entries, e => e.Name == "记事本");
        Assert.Equal(personal, entry.Path);
        Assert.True(entry.FromCurrentUser);
        Assert.Equal(1, result.DuplicateNameCount);
        Assert.Equal(2, result.ScannedItemCount);
    }

    [Fact]
    public void Scan_uses_resolver_name_when_available()
    {
        var common = MakeRoot("common");
        var user = MakeRoot("user");
        var shortcut = Path.Combine(common, "notepad.lnk");
        Write(shortcut);

        var result = Scan(common, user, path =>
            path.Equals(shortcut, StringComparison.OrdinalIgnoreCase) ? "记事本" : null);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("记事本", entry.Name);
        Assert.Equal(shortcut, entry.Path);
    }

    [Fact]
    public void Scan_falls_back_to_shortcut_file_name_and_keeps_document_extension()
    {
        var common = MakeRoot("common");
        var user = MakeRoot("user");
        Write(Path.Combine(common, "Visual Studio Code.lnk"));
        Write(Path.Combine(common, "季度报告.xlsx"));

        var result = Scan(common, user);

        Assert.Contains(result.Entries, e => e.Name == "Visual Studio Code");
        Assert.Contains(result.Entries, e => e.Name == "季度报告.xlsx");
    }

    [Fact]
    public void Scan_reports_roots_that_actually_existed()
    {
        var common = MakeRoot("common");
        var user = MakeRoot("user");

        var result = Scan(common, user);

        Assert.Equal([common, user], result.Roots);
    }
}
