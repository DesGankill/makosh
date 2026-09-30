namespace Makosh.Core;

public static class LlmProviders
{
    public const string OpenRouter = "openrouter";
    public const string OpenAi = "openai";
    public const string Xai = "xai";
    public const string DeepSeek = "deepseek";
    public const string Local = "local";
    public const string Browser = "browser";

    public static string Normalize(string? raw)
    {
        var value = (raw ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "" => "",
            "grok" => Xai,
            "openai_compatible" or "compatible" => OpenRouter,
            "chatgpt" => OpenAi,
            _ => value,
        };
    }

    public static bool IsBrowser(string? provider) =>
        string.Equals(Normalize(provider), Browser, StringComparison.Ordinal);

    public static string DefaultBaseUrl(string provider) =>
        Normalize(provider) switch
        {
            OpenAi => "https://api.openai.com/v1",
            Xai => "https://api.x.ai/v1",
            DeepSeek => "https://api.deepseek.com",
            Local => "http://127.0.0.1:11434/v1",
            _ => "https://openrouter.ai/api/v1",
        };

    public static string DefaultModel(string provider, string current)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        return Normalize(provider) switch
        {
            Xai => "grok-3",
            DeepSeek => "deepseek-chat",
            OpenAi => "gpt-4o-mini",
            Local => "llama3",
            _ => "openai/gpt-4o-mini",
        };
    }
}

public static class LlmProviderFactory
{
    public static IChatClient? Create(MakoshSettings settings)
    {
        var primary = CreateOne(
            settings.LlmProvider,
            settings.ApiKey,
            settings.BaseUrl,
            settings.Model);
        if (primary is null)
        {
            return null;
        }

        var fallbackKey = string.IsNullOrWhiteSpace(settings.FallbackApiKey)
            ? settings.ApiKey
            : settings.FallbackApiKey;
        var fallbackProvider = settings.FallbackLlmProvider;
        if (string.IsNullOrWhiteSpace(fallbackProvider) ||
            string.Equals(fallbackProvider, settings.LlmProvider, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        var fallbackUrl = string.IsNullOrWhiteSpace(settings.FallbackBaseUrl)
            ? LlmProviders.DefaultBaseUrl(fallbackProvider)
            : settings.FallbackBaseUrl;
        var fallbackModel = string.IsNullOrWhiteSpace(settings.FallbackModel)
            ? settings.Model
            : settings.FallbackModel;
        var fallback = CreateOne(fallbackProvider, fallbackKey, fallbackUrl, fallbackModel);
        if (fallback is null)
        {
            return primary;
        }

        return new FailoverChatClient(
            primary,
            fallback,
            TimeSpan.FromSeconds(Math.Max(1, settings.LlmRateLimitCooldownSeconds)));
    }

    static IChatClient? CreateOne(string provider, string apiKey, string baseUrl, string model)
    {
        var id = LlmProviders.Normalize(provider);
        if (LlmProviders.IsBrowser(id))
        {
            return new UnavailableLlmClient(
                LlmProviders.Browser,
                "Браузерный LLM ещё не подключён. Это отдельный провайдер с изолированным профилем, не обход API. Пока укажите MAKOSH_LLM_PROVIDER=openrouter (или openai/xai/deepseek) и ключ.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var slice = new MakoshSettings
        {
            ApiKey = apiKey,
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? LlmProviders.DefaultBaseUrl(id) : baseUrl,
            Model = LlmProviders.DefaultModel(id, model),
            LlmProvider = string.IsNullOrWhiteSpace(id) ? LlmProviders.OpenRouter : id,
        };
        return new OpenAIChatClient(slice);
    }
}

public sealed class UnavailableLlmClient : ILLMProvider
{
    readonly string _message;

    public UnavailableLlmClient(string id, string message)
    {
        Id = id;
        _message = message;
    }

    public string Id { get; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
        throw LlmException.Unavailable(_message);
}

public sealed class FailoverChatClient : ILLMProvider
{
    readonly IChatClient _primary;
    readonly IChatClient _fallback;
    readonly TimeSpan _cooldown;
    readonly object _gate = new();
    DateTimeOffset _primaryCoolUntil = DateTimeOffset.MinValue;

    public FailoverChatClient(IChatClient primary, IChatClient fallback, TimeSpan cooldown)
    {
        _primary = primary;
        _fallback = fallback;
        _cooldown = cooldown;
        Id = "failover";
    }

    public string Id { get; }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        if (PrimaryCooling())
        {
            return await _fallback.CompleteAsync(request, cancellationToken);
        }

        try
        {
            return await _primary.CompleteAsync(request, cancellationToken);
        }
        catch (LlmException ex) when (ex.Kind is LlmErrorKind.RateLimited or LlmErrorKind.Unavailable)
        {
            if (ex.Kind == LlmErrorKind.RateLimited)
            {
                lock (_gate)
                {
                    _primaryCoolUntil = DateTimeOffset.UtcNow + _cooldown;
                }
            }

            try
            {
                return await _fallback.CompleteAsync(request, cancellationToken);
            }
            catch (LlmException fallbackEx)
            {
                throw new LlmException(
                    fallbackEx.Kind,
                    ex.UserMessage + " Запасной провайдер тоже не ответил: " + fallbackEx.UserMessage,
                    fallbackEx.Message,
                    fallbackEx);
            }
        }
    }

    bool PrimaryCooling()
    {
        lock (_gate)
        {
            return DateTimeOffset.UtcNow < _primaryCoolUntil;
        }
    }
}
