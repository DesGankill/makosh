namespace Makosh.Core;

static class EnvFile
{
    public static IReadOnlyDictionary<string, string> ReadNearest(IEnumerable<string>? extraSearchRoots)
    {
        var path = Find(extraSearchRoots);
        return path is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : Parse(File.ReadAllLines(path));
    }

    static string? Find(IEnumerable<string>? extraSearchRoots)
    {
        var explicitPath = Environment.GetEnvironmentVariable("MAKOSH_ENV_FILE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var besideApp = Path.Combine(AppContext.BaseDirectory, ".env");
        if (File.Exists(besideApp))
        {
            return besideApp;
        }

        if (extraSearchRoots is not null)
        {
            foreach (var root in extraSearchRoots)
            {
                var found = WalkParents(root);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        return WalkParents(Directory.GetCurrentDirectory());
    }

    static string? WalkParents(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        for (var dir = new DirectoryInfo(Path.GetFullPath(root)); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string? Locate(IEnumerable<string>? extraSearchRoots) => Find(extraSearchRoots);

    static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            var split = line.Split('=', 2);
            var key = split[0].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            var value = split[1].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}
