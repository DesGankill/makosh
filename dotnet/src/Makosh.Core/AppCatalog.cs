using System.Text.Json;

namespace Makosh.Core;

public sealed class AppEntry
{
    public string Id { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string Kind { get; init; } = "process";
    public string Target { get; init; } = "";
}

public sealed class AppCatalog
{
    public const string FileName = "apps.json";

    readonly Dictionary<string, AppEntry> _byAlias;

    AppCatalog(IReadOnlyList<AppEntry> apps)
    {
        Apps = apps;
        _byAlias = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            Register(app.Id, app);
            foreach (var alias in app.Aliases)
            {
                Register(alias, app);
            }
        }
    }

    public IReadOnlyList<AppEntry> Apps { get; }

    public static AppCatalog BuiltIn() => new(DefaultApps());

    public static AppCatalog From(IEnumerable<AppEntry> apps) => new(apps.ToList());

    public static AppCatalog Load(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultJson());
            return BuiltIn();
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var extras = Parse(doc.RootElement);
            return Merge(DefaultApps(), extras);
        }
        catch (JsonException)
        {
            return BuiltIn();
        }
    }

    public AppEntry? Find(string name)
    {
        var key = NormalizeName(name);
        if (key.Length == 0)
        {
            return null;
        }

        if (_byAlias.TryGetValue(key, out var exact))
        {
            return exact;
        }

        if (key.Length < 3)
        {
            return _byAlias.TryGetValue(key, out var shortName) ? shortName : null;
        }

        AppEntry? found = null;
        foreach (var pair in _byAlias)
        {
            if (pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (found is not null && !string.Equals(found.Id, pair.Value.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                found = pair.Value;
            }
        }

        return found;
    }

    static string NormalizeName(string? name)
    {
        var key = (name ?? "").Trim().ToLowerInvariant();
        string[] prefixes = ["открой ", "открыть ", "откройте ", "запусти ", "запустить ", "запустите "];
        foreach (var prefix in prefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                key = key[prefix.Length..].Trim();
            }
        }

        if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            key = key[..^4];
        }

        return key;
    }

    public string Summary() =>
        string.Join(", ", Apps.Select(app => app.Id).Distinct(StringComparer.OrdinalIgnoreCase));

    static AppCatalog Merge(IReadOnlyList<AppEntry> defaults, IReadOnlyList<AppEntry> extras)
    {
        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in defaults.Concat(extras))
        {
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                map[app.Id] = app;
            }
        }

        return new AppCatalog(map.Values.ToList());
    }

    static IReadOnlyList<AppEntry> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("apps", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<AppEntry>();
        foreach (var item in array.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var target = item.TryGetProperty("target", out var targetEl) ? targetEl.GetString() ?? "" : "";
            var kind = item.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() ?? "process" : "process";
            var aliases = new List<string>();
            if (item.TryGetProperty("aliases", out var aliasEl) && aliasEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var alias in aliasEl.EnumerateArray())
                {
                    var text = alias.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        aliases.Add(text);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
            {
                list.Add(new AppEntry { Id = id.Trim(), Target = target.Trim(), Kind = kind.Trim(), Aliases = [.. aliases] });
            }
        }

        return list;
    }

    void Register(string alias, AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            _byAlias[alias.Trim()] = app;
        }
    }

    public static IReadOnlyList<AppEntry> DefaultApps() =>
    [
        new() { Id = "explorer", Aliases = ["проводник", "проводника"], Kind = "process", Target = "explorer" },
        new() { Id = "notepad", Aliases = ["блокнот"], Kind = "process", Target = "notepad" },
        new() { Id = "calc", Aliases = ["калькулятор"], Kind = "process", Target = "calc" },
        new() { Id = "mspaint", Aliases = ["paint", "паинт"], Kind = "process", Target = "mspaint" },
        new() { Id = "cmd", Aliases = ["командная строка"], Kind = "process", Target = "cmd" },
        new() { Id = "browser", Aliases = ["браузер"], Kind = "url", Target = "https://google.com" },
        new() { Id = "youtube", Aliases = ["ютуб", "you tube"], Kind = "url", Target = "https://www.youtube.com" },
        new() { Id = "blender", Aliases = ["блендер"], Kind = "process", Target = "blender" },
        new() { Id = "steam", Aliases = ["стим"], Kind = "process", Target = "steam" },
        new() { Id = "discord", Aliases = ["дискорд"], Kind = "process", Target = "discord" },
        new() { Id = "valheim", Aliases = ["вальхейм", "валхейм"], Kind = "process", Target = "valheim" },
    ];

    public static string DefaultJson() =>
        """
        {
          "apps": [
            { "id": "blender", "aliases": ["blender", "блендер"], "kind": "process", "target": "blender" },
            { "id": "steam", "aliases": ["steam", "стим"], "kind": "process", "target": "steam" },
            { "id": "discord", "aliases": ["discord", "дискорд"], "kind": "process", "target": "discord" },
            { "id": "valheim", "aliases": ["valheim", "вальхейм"], "kind": "process", "target": "valheim" },
            { "id": "youtube", "aliases": ["youtube", "ютуб"], "kind": "url", "target": "https://www.youtube.com" }
          ]
        }
        """;
}
