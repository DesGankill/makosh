using System.Diagnostics;
using Makosh.Core;

namespace Makosh.Tests;

public sealed class PathGuardTests : IDisposable
{
    readonly string _root;
    readonly PathGuard _paths;

    public PathGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "makosh-m4-" + Guid.NewGuid().ToString("N"), "root");
        Directory.CreateDirectory(_root);
        _paths = new PathGuard([_root]);
    }

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            TryDeleteTree(parent);
        }
    }

    static void TryDeleteTree(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }

                return;
            }

            if ((attrs & FileAttributes.Directory) != 0)
            {
                foreach (var child in Directory.GetFileSystemEntries(path))
                {
                    TryDeleteTree(child);
                }

                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp junction leftovers are not part of the assertion.
        }
    }

    [Fact]
    public void File_inside_allowed_root()
    {
        var target = Path.Combine(_root, "notes", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "hello");
        Assert.Equal(Path.GetFullPath(target), _paths.ResolveSafe(target));
        Assert.Contains("a.txt", _paths.ListFiles(Path.GetDirectoryName(target)!), StringComparison.Ordinal);
    }

    [Fact]
    public void Absolute_path_inside_root()
    {
        var target = Path.Combine(_root, "b.txt");
        File.WriteAllText(target, "x");
        Assert.Equal(Path.GetFullPath(target), _paths.ResolveSafe(Path.GetFullPath(target)));
    }

    [Fact]
    public void Missing_path_inside_root_is_reported()
    {
        var missing = Path.Combine(_root, "missing.txt");
        Assert.Equal(Path.GetFullPath(missing), _paths.ResolveSafe(missing));
        Assert.Contains("Нет такого пути", _paths.ListFiles(missing), StringComparison.Ordinal);
    }

    [Fact]
    public void Parent_segments_cannot_leave_root()
    {
        var outside = Path.GetDirectoryName(_root)!;
        var secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(secret, "nope");
        var escaped = Path.Combine(_root, "nested", "..", "..", "secret.txt");
        var ex = Assert.Throws<InvalidOperationException>(() => _paths.ResolveSafe(escaped));
        Assert.Contains("вне разрешённых", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Absolute_path_outside_root_is_denied()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _paths.ResolveSafe(@"C:\Windows\System32\drivers\etc\hosts"));
        Assert.Contains("вне разрешённых", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sibling_prefix_is_not_inside_root()
    {
        var sibling = _root + "_extra";
        Directory.CreateDirectory(sibling);
        var leaked = Path.Combine(sibling, "x.txt");
        File.WriteAllText(leaked, "x");
        var ex = Assert.Throws<InvalidOperationException>(() => _paths.ResolveSafe(leaked));
        Assert.Contains("вне разрешённых", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_shortcut_is_outside_a_narrow_root()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _paths.ResolveSafe("~"));
        Assert.Contains("вне разрешённых", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_directory_is_currently_an_allowed_root()
    {
        var user = PathGuard.ForUser(Path.Combine(_root, "data"));
        Assert.Contains(user.Roots, root => Path.GetFullPath(root).Equals(
            Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Path_under_home_but_outside_documents_is_currently_allowed()
    {
        var home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var resolved = Path.GetFullPath(_root);
        if (!resolved.Equals(home, StringComparison.OrdinalIgnoreCase) &&
            !PathGuard.IsInside(resolved, home))
        {
            return;
        }

        var marker = Path.Combine(_root, "makosh-m4-boundary.txt");
        File.WriteAllText(marker, "x");
        var user = PathGuard.ForUser(Path.Combine(home, "data-makosh-test"));
        Assert.Equal(Path.GetFullPath(marker), user.ResolveSafe(marker));
    }

    [Fact]
    public void Symlink_to_outside_file_is_denied()
    {
        var outside = Path.GetDirectoryName(_root)!;
        var secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(secret, "hidden");
        var link = Path.Combine(_root, "alias.txt");
        try
        {
            File.CreateSymbolicLink(link, secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var thrown = Assert.Throws<InvalidOperationException>(() => _paths.ResolveSafe(link));
        Assert.Contains("вне разрешённых", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Directory_junction_to_outside_is_denied()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "hidden");
        var link = Path.Combine(_root, "jump");
        if (!TryJunction(link, outside))
        {
            return;
        }

        var thrown = Assert.Throws<InvalidOperationException>(() => _paths.ResolveSafe(Path.Combine(link, "secret.txt")));
        Assert.Contains("вне разрешённых", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Directory_junction_inside_root_is_allowed()
    {
        var target = Path.Combine(_root, "real");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "a.txt"), "ok");
        var link = Path.Combine(_root, "jump");
        if (!TryJunction(link, target))
        {
            return;
        }

        Assert.Equal(Path.GetFullPath(Path.Combine(target, "a.txt")), _paths.ResolveSafe(Path.Combine(link, "a.txt")));
    }

    [Fact]
    public void Open_path_reports_missing()
    {
        var shell = new FakeShell();
        var missing = Path.Combine(_root, "nope.txt");
        Assert.Contains("Не найдено", PathOpener.Open(missing, _paths, shell), StringComparison.Ordinal);
        Assert.Empty(shell.Opened);
    }

    [Fact]
    public void List_files_default_downloads_and_limit()
    {
        var home = Path.Combine(Path.GetDirectoryName(_root)!, "home");
        var downloads = Path.Combine(home, "Downloads");
        Directory.CreateDirectory(downloads);
        for (var i = 0; i < 81; i++)
        {
            File.WriteAllText(Path.Combine(downloads, $"f{i:D3}.txt"), "x");
        }

        Directory.CreateDirectory(Path.Combine(downloads, "aaa"));
        var paths = new PathGuard([home]) { Home = home };
        var listing = paths.ListFiles("");
        Assert.StartsWith(Path.GetFullPath(downloads), listing);
        Assert.Equal(80, listing.Split('\n').Length - 1);
        Assert.Contains("📁 aaa", listing, StringComparison.Ordinal);
        Assert.Contains("📄 f000.txt", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_directory_message()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Contains("Папка пустая", _paths.ListFiles(empty), StringComparison.Ordinal);
    }

    [Fact]
    public void Copy_into_inbox_copies_file_only()
    {
        var data = Path.Combine(Path.GetDirectoryName(_root)!, "data");
        var srcDir = Path.Combine(_root, "files");
        Directory.CreateDirectory(srcDir);
        var src = Path.Combine(srcDir, "note.txt");
        File.WriteAllText(src, "hello");
        var paths = PathGuard.ForUser(data, home: _root);
        var dest = paths.CopyIntoInbox(src, "phone");
        Assert.Equal("hello", File.ReadAllText(dest));
        Assert.StartsWith(Path.GetFullPath(Path.Combine(data, "inbox", "phone")), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase);
    }

    static bool TryJunction(string link, string target)
    {
        var result = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        result?.WaitForExit();
        return result is { ExitCode: 0 } && Directory.Exists(link);
    }
}
