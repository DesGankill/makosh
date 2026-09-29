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
