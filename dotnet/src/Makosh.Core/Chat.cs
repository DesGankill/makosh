using System.Text.Json;

namespace Makosh.Core;

public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersJson { get; init; }
}

public interface ITool
{
    ToolDefinition Definition { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}

public sealed class LlmToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Arguments { get; init; }
}

public sealed class LlmMessage
{
    public required string Role { get; init; }
    public string Content { get; init; } = "";
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
}

public sealed class LlmRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<LlmMessage> Messages { get; init; }
    public required IReadOnlyList<ToolDefinition> Tools { get; init; }
}

public sealed class LlmResponse
{
    public string? Content { get; init; }
    public IReadOnlyList<LlmToolCall> ToolCalls { get; init; } = [];
}

public interface IChatClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Chat backend. Agent talks only to this surface, not to a vendor.
/// </summary>
public interface ILLMProvider : IChatClient
{
    string Id { get; }
}

public enum LlmErrorKind
{
    RateLimited,
    Unauthorized,
    Network,
    Unavailable,
    Other,
}

public sealed class LlmException : Exception
{
    public LlmErrorKind Kind { get; }
    public string UserMessage { get; }

    public LlmException(LlmErrorKind kind, string userMessage, string? technical = null, Exception? inner = null)
        : base(technical ?? userMessage, inner)
    {
        Kind = kind;
        UserMessage = userMessage;
    }

    public static LlmException FromHttpStatus(int status, string? detail = null)
    {
        var hint = string.IsNullOrWhiteSpace(detail) ? "" : " " + detail.Trim();
        return status switch
        {
            429 => new LlmException(
                LlmErrorKind.RateLimited,
                "Модель временно отказала: слишком много запросов (429). Подождите минуту или смените MAKOSH_LLM_PROVIDER / запасной ключ. Makosh при этом не сломана.",
                "HTTP 429" + hint),
            401 or 403 => new LlmException(
                LlmErrorKind.Unauthorized,
                "Ключ API отклонён. Проверьте OPENAI_API_KEY и выбранный MAKOSH_LLM_PROVIDER.",
                "HTTP " + status + hint),
            >= 500 => new LlmException(
                LlmErrorKind.Unavailable,
                "Сервер модели сейчас недоступен. Можно подождать или переключить провайдера.",
                "HTTP " + status + hint),
            _ => new LlmException(
                LlmErrorKind.Other,
                "Модель вернула ошибку " + status + ". Подробности в логе сервера.",
                "HTTP " + status + hint),
        };
    }

    public static LlmException Network(Exception inner) =>
        new(
            LlmErrorKind.Network,
            "Сеть недоступна, модель не ответила. Проверьте интернет и OPENAI_BASE_URL.",
            inner.Message,
            inner);

    public static LlmException Unavailable(string userMessage, string? technical = null) =>
        new(LlmErrorKind.Unavailable, userMessage, technical);
}
