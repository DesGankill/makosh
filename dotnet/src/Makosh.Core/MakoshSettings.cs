namespace Makosh.Core;

/// <summary>
/// Settings from the process environment, then the nearest <c>.env</c>.
/// Environment variables win, matching python-dotenv defaults.
/// </summary>
public sealed class MakoshSettings
{
    public string Host { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8787;
    public string Token { get; init; } = "change-me-now";
    public string DeviceName { get; init; } = "PC";
    public string ApiKey { get; init; } = "";
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string Model { get; init; } = "openai/gpt-4o-mini";
    public string VisionModel { get; init; } = "openai/gpt-4o-mini";
    public int VisionMaxPerHour { get; init; } = 6;
    public int VisionMaxWidth { get; init; } = 768;
    public string DataDirectory { get; init; } = "data";

    public string InboxDirectory => Path.Combine(DataDirectory, "inbox");

    public bool HasChatModel => !string.IsNullOrWhiteSpace(ApiKey);

    public static MakoshSettings Load(IEnumerable<string>? extraSearchRoots = null)
    {
        var fileValues = EnvFile.ReadNearest(extraSearchRoots);
        var model = Read("OPENAI_MODEL", fileValues, "openai/gpt-4o-mini");

        return new MakoshSettings
        {
            Host = Read("MAKOSH_HOST", fileValues, "0.0.0.0"),
            Port = ParsePort(Read("MAKOSH_PORT", fileValues, "8787")),
            Token = Read("MAKOSH_TOKEN", fileValues, "change-me-now"),
            DeviceName = Read("MAKOSH_DEVICE_NAME", fileValues, "PC"),
            ApiKey = Read("OPENAI_API_KEY", fileValues, ""),
            BaseUrl = Read("OPENAI_BASE_URL", fileValues, "https://openrouter.ai/api/v1"),
            Model = model,
            VisionModel = Read("VISION_MODEL", fileValues, model),
            VisionMaxPerHour = ParsePositive(Read("VISION_MAX_PER_HOUR", fileValues, "6"), 6),
            VisionMaxWidth = ParsePositive(Read("VISION_MAX_WIDTH", fileValues, "768"), 768),
            DataDirectory = ResolveDataDirectory(extraSearchRoots),
        };
    }

    static string ResolveDataDirectory(IEnumerable<string>? extraSearchRoots)
    {
        var explicitDir = Environment.GetEnvironmentVariable("MAKOSH_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            return Path.GetFullPath(explicitDir.Trim());
        }

        var envFile = EnvFile.Locate(extraSearchRoots);
        if (envFile is not null)
        {
            var repo = Path.GetDirectoryName(Path.GetFullPath(envFile));
            if (!string.IsNullOrEmpty(repo))
            {
                return Path.Combine(repo, "data");
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
    }

    public static bool TokenMatches(string? provided, string expected) =>
        !string.IsNullOrEmpty(expected) && string.Equals(provided, expected, StringComparison.Ordinal);

    static string Read(string key, IReadOnlyDictionary<string, string> fileValues, string fallback)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        if (fileValues.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile))
        {
            return fromFile.Trim();
        }

        return fallback;
    }

    static int ParsePort(string raw) =>
        int.TryParse(raw, out var port) && port is > 0 and < 65536 ? port : 8787;

    static int ParsePositive(string raw, int fallback) =>
        int.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
