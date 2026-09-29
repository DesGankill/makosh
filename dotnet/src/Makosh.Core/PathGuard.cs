namespace Makosh.Core;

public sealed class PathGuard
{
    public const int ListLimit = 80;

    readonly IReadOnlyList<string> _roots;

    public PathGuard(IEnumerable<string> roots)
    {
        _roots = roots.Select(root => NormalizeExistingOrFull(root)).ToList();
    }

    public IReadOnlyList<string> Roots => _roots;

    public string Home { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string DataDirectory { get; init; } = "data";

    public string InboxDirectory => Path.Combine(DataDirectory, "inbox");

    public string Downloads => Path.Combine(Home, "Downloads");

    public static PathGuard ForUser(string dataDirectory, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Path.GetFullPath(dataDirectory);
        return new PathGuard(
        [
            home,
            data,
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Documents"),
        ])
        {
            Home = home,
            DataDirectory = data,
        };
    }

    public string ResolveSafe(string raw)
    {
        var expanded = Expand(raw);
        var resolved = FollowLinks(Path.GetFullPath(expanded));
        foreach (var root in _roots)
        {
            if (IsInside(resolved, root))
            {
                return resolved;
            }
        }

        throw new InvalidOperationException($"Путь вне разрешённых папок: {resolved}");
    }

    public string ListFiles(string raw)
    {
        var path = ResolveSafe(string.IsNullOrEmpty(raw) ? Downloads : raw);
        if (!Path.Exists(path))
        {
            return $"Нет такого пути: {path}";
        }

        if (File.Exists(path))
        {
            return path;
        }

        var names = Directory.EnumerateFileSystemEntries(path)
            .Select(entry => (FileSystemInfo)(Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry)))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Take(ListLimit)
            .ToList();
        if (names.Count == 0)
        {
            return $"Папка пустая: {path}";
        }

        var lines = names.Select(info => $"{(info is DirectoryInfo ? "📁" : "📄")} {info.Name}");
        return path + "\n" + string.Join("\n", lines);
    }

    public string CopyIntoInbox(string src, string deviceId)
    {
        var path = ResolveSafe(src);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Это не файл: {path}");
        }

        var destDir = Path.Combine(InboxDirectory, deviceId);
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, Path.GetFileName(path));
        File.Copy(path, dest, overwrite: true);
        return dest;
    }

    public static bool IsInside(string path, string root)
    {
        var fullPath = NormalizeExistingOrFull(path);
        var fullRoot = NormalizeExistingOrFull(root);
        if (fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    static string Expand(string raw)
    {
        var expanded = Environment.ExpandEnvironmentVariables(raw);
        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal) ||
                expanded.StartsWith("~\\", StringComparison.Ordinal))
            {
                expanded = home + expanded[1..];
            }
        }

        return expanded;
    }

    static string FollowLinks(string full)
    {
        var native = NativeWinPath.TryFinalPath(full);
        if (!string.IsNullOrEmpty(native))
        {
            return native;
        }

        try
        {
            FileSystemInfo? info = File.Exists(full)
                ? new FileInfo(full)
                : Directory.Exists(full)
                    ? new DirectoryInfo(full)
                    : null;
            if (info is null)
            {
                return full;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? full : Path.GetFullPath(target.FullName);
        }
        catch (IOException)
        {
            return full;
        }
        catch (UnauthorizedAccessException)
        {
            return full;
        }
    }

    static string NormalizeExistingOrFull(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            if (File.Exists(full) || Directory.Exists(full))
            {
                return FollowLinks(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch
        {
            // Use the normalized path even if the target is inaccessible.
        }

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
