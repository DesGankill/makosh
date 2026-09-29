using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace Makosh.Core;

public sealed class OpenAIChatClient : IChatClient, IVisionClient
{
    readonly ChatClient _client;
    readonly MakoshSettings _settings;
    readonly OpenAIClientOptions _options;
    readonly ApiKeyCredential _credential;

    public OpenAIChatClient(MakoshSettings settings)
    {
        _settings = settings;
        _options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) &&
            Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            _options.Endpoint = endpoint;
        }

        _credential = new ApiKeyCredential(settings.ApiKey);
        _client = new ChatClient(settings.Model, _credential, _options);
    }

    public async Task<string> DescribeAsync(string model, string prompt, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        var visionModel = string.IsNullOrWhiteSpace(model) ? _settings.VisionModel : model;
        var client = new ChatClient(visionModel, _credential, _options);
        var message = new UserChatMessage(
            ChatMessageContentPart.CreateTextPart(prompt),
            ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(jpeg), "image/jpeg"));
        var completion = await client.CompleteChatAsync([message], cancellationToken: cancellationToken);
        var parts = completion.Value.Content;
        return parts.Count == 0 ? "" : parts[0].Text ?? "";
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
