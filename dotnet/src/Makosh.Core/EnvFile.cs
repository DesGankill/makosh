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

        var roots = new List<string>();
        if (extraSearchRoots is not null)
        {
            roots.AddRange(extraSearchRoots);
        }

        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(cwd))
        {
            roots.Add(cwd);
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            roots.Add(baseDir);
        }

        foreach (var root in roots)
        {
            for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

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
