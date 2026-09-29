using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace Makosh.Core;

public sealed class OpenAIChatClient : IChatClient
{
    readonly ChatClient _client;

    public OpenAIChatClient(MakoshSettings settings)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) &&
            Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            options.Endpoint = endpoint;
        }

        _client = new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), options);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var messages = request.Messages.Select(ToChatMessage).ToList();
        var options = new ChatCompletionOptions
        {
            ToolChoice = ChatToolChoice.CreateAutoChoice(),
        };
        foreach (var tool in request.Tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                BinaryData.FromString(tool.ParametersJson)));
        }

        var completion = await _client.CompleteChatAsync(messages, options, cancellationToken);
        var message = completion.Value;
        var calls = new List<LlmToolCall>();
        foreach (var call in message.ToolCalls)
        {
            calls.Add(new LlmToolCall
            {
                Id = call.Id,
                Name = call.FunctionName,
                Arguments = call.FunctionArguments.ToString(),
            });
        }

        return new LlmResponse
        {
            Content = message.Content.Count == 0 ? null : message.Content[0].Text,
            ToolCalls = calls,
        };
    }

    static ChatMessage ToChatMessage(LlmMessage message) =>
        message.Role switch
        {
            "system" => new SystemChatMessage(message.Content),
            "user" => new UserChatMessage(message.Content),
            "assistant" when message.ToolCalls is { Count: > 0 } calls =>
                new AssistantChatMessage(calls.Select(call => ChatToolCall.CreateFunctionToolCall(
                    call.Id,
                    call.Name,
                    BinaryData.FromString(call.Arguments)))),
            "assistant" => new AssistantChatMessage(message.Content),
            "tool" => new ToolChatMessage(message.ToolCallId ?? "", message.Content),
            _ => new UserChatMessage(message.Content),
        };
}
