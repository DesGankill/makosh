namespace Makosh.Core;

public static class AppLauncher
{
    public static readonly Dictionary<string, string?> AllowedApps = new(StringComparer.Ordinal)
    {
        ["explorer"] = "explorer",
        ["проводника"] = "explorer",
        ["notepad"] = "notepad",
        ["блокнот"] = "notepad",
        ["calc"] = "calc",
        ["калькулятор"] = "calc",
        ["browser"] = "https://",
        ["браузер"] = null,
    };

    // Matches Python allowed_bins. cmd/mspaint are launchable by binary name; do not widen this set.
    public static readonly HashSet<string> AllowedBinaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "notepad", "calc", "mspaint", "cmd",
    };

    public static string Open(string name, IAppHost host)
    {
        var key = name.Trim().ToLowerInvariant();
        var mapped = AllowedApps.TryGetValue(key, out var value) ? value : key;
        var denied = $"Приложение «{name}» не в белом списке. Можно: блокнот, проводник, калькулятор, браузер.";
        if (mapped is null || mapped == "https://")
        {
            host.OpenUrl("https://google.com");
            return "Открыл браузер";
        }

        if (mapped.StartsWith("http", StringComparison.Ordinal))
        {
            host.OpenUrl(mapped);
            return $"Открыл {mapped}";
        }

        if (key.StartsWith("http://", StringComparison.Ordinal) || key.StartsWith("https://", StringComparison.Ordinal))
        {
            host.OpenUrl(name.Trim());
            return $"Открыл {name}";
        }

        if (mapped.Contains('\\') || mapped.Contains('/') || mapped.Contains(':'))
        {
            return denied;
        }

        var target = mapped.Contains('\\') ? Path.GetFileName(mapped) : mapped;
        if (!AllowedBinaries.Contains(target) && !AllowedApps.ContainsKey(key))
        {
            return denied;
        }

        host.StartProcess(mapped);
        return $"Запустил {mapped}";
    }
}

public static class PathOpener
{
    public static string Open(string raw, PathGuard paths, IShell shell)
    {
        var path = paths.ResolveSafe(raw);
        if (!Path.Exists(path))
        {
            return $"Не найдено: {path}";
        }

        shell.OpenFileOrFolder(path);
        return $"Открыл {path}";
    }
}
