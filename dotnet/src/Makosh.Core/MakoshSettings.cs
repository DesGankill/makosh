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
    public string LlmProvider { get; init; } = LlmProviders.OpenRouter;
    public string BrowserLlmProvider { get; init; } = "";
    public string BaseUrl { get; init; } = LlmProviders.DefaultBaseUrl(LlmProviders.OpenRouter);
    public string Model { get; init; } = "openai/gpt-4o-mini";
    public string FallbackLlmProvider { get; init; } = "";
    public string FallbackApiKey { get; init; } = "";
    public string FallbackBaseUrl { get; init; } = "";
    public string FallbackModel { get; init; } = "";
    public int LlmRateLimitCooldownSeconds { get; init; } = 60;
    public string VisionModel { get; init; } = "openai/gpt-4o-mini";
    public int VisionMaxPerHour { get; init; } = 6;
    public int VisionMaxWidth { get; init; } = 768;
    public bool TtsEnabled { get; init; } = true;
    public string TtsEngine { get; init; } = TtsEngines.Silero;
    public string TtsVoice { get; init; } = "kseniya";
    public string TtsDevice { get; init; } = "cpu";
    public int TtsRate { get; init; }
    public int TtsVolume { get; init; } = 100;
    public int TtsPitch { get; init; }
    public string DataDirectory { get; init; } = "data";

    public string InboxDirectory => Path.Combine(DataDirectory, "inbox");

    public bool HasChatModel =>
        LlmProviders.IsBrowser(LlmProvider) || !string.IsNullOrWhiteSpace(ApiKey);

    public static MakoshSettings Load(IEnumerable<string>? extraSearchRoots = null)
    {
        var fileValues = EnvFile.ReadNearest(extraSearchRoots);
        var model = Read("OPENAI_MODEL", fileValues, "openai/gpt-4o-mini");
        var provider = LlmProviders.Normalize(Read("MAKOSH_LLM_PROVIDER", fileValues, LlmProviders.OpenRouter));
        var explicitBase = Read("OPENAI_BASE_URL", fileValues, "");
        var fallbackProvider = LlmProviders.Normalize(Read("MAKOSH_LLM_FALLBACK", fileValues, ""));

        return new MakoshSettings
        {
            Host = Read("MAKOSH_HOST", fileValues, "0.0.0.0"),
            Port = ParsePort(Read("MAKOSH_PORT", fileValues, "8787")),
            Token = Read("MAKOSH_TOKEN", fileValues, "change-me-now"),
            DeviceName = Read("MAKOSH_DEVICE_NAME", fileValues, "PC"),
            ApiKey = Read("OPENAI_API_KEY", fileValues, ""),
            LlmProvider = provider,
            BrowserLlmProvider = LlmProviders.Normalize(Read("MAKOSH_BROWSER_LLM_PROVIDER", fileValues, "")),
            BaseUrl = string.IsNullOrWhiteSpace(explicitBase)
                ? LlmProviders.DefaultBaseUrl(provider)
                : explicitBase,
            Model = model,
            FallbackLlmProvider = fallbackProvider,
            FallbackApiKey = Read("MAKOSH_LLM_FALLBACK_API_KEY", fileValues, ""),
            FallbackBaseUrl = Read("MAKOSH_LLM_FALLBACK_BASE_URL", fileValues, ""),
            FallbackModel = Read("MAKOSH_LLM_FALLBACK_MODEL", fileValues, ""),
            LlmRateLimitCooldownSeconds = ParsePositive(
                Read("MAKOSH_LLM_RATE_LIMIT_COOLDOWN", fileValues, "60"),
                60),
            VisionModel = Read("VISION_MODEL", fileValues, model),
            VisionMaxPerHour = ParsePositive(Read("VISION_MAX_PER_HOUR", fileValues, "6"), 6),
            VisionMaxWidth = ParsePositive(Read("VISION_MAX_WIDTH", fileValues, "768"), 768),
            TtsEnabled = ParseBool(Read("MAKOSH_TTS_ENABLED", fileValues, "true"), true),
            TtsEngine = TtsEngines.Normalize(Read("MAKOSH_TTS_ENGINE", fileValues, TtsEngines.Silero)),
            TtsVoice = Read("MAKOSH_TTS_VOICE", fileValues, "kseniya"),
            TtsDevice = Read("MAKOSH_TTS_DEVICE", fileValues, "cpu"),
            TtsRate = TtsLimits.ClampRate(ParseSigned(Read("MAKOSH_TTS_RATE", fileValues, "0"), 0)),
            TtsVolume = TtsLimits.ClampVolume(ParseSigned(Read("MAKOSH_TTS_VOLUME", fileValues, "100"), 100)),
            TtsPitch = TtsLimits.ClampPitch(ParseSigned(Read("MAKOSH_TTS_PITCH", fileValues, "0"), 0)),
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

    static int ParseSigned(string raw, int fallback) =>
        int.TryParse(raw, out var value) ? value : fallback;

    static bool ParseBool(string raw, bool fallback)
    {
        if (string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }
}
