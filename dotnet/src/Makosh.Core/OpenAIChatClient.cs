using System.ClientModel;
using System.Net.Http;
using OpenAI;
using OpenAI.Chat;

namespace Makosh.Core;

public sealed class OpenAIChatClient : ILLMProvider, IVisionClient
{
    readonly ChatClient _client;
    readonly MakoshSettings _settings;
    readonly OpenAIClientOptions _options;
    readonly ApiKeyCredential _credential;

    public string Id { get; }

    public OpenAIChatClient(MakoshSettings settings)
    {
        Id = string.IsNullOrWhiteSpace(settings.LlmProvider) ? LlmProviders.OpenRouter : settings.LlmProvider;
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
        try
        {
            var completion = await client.CompleteChatAsync([message], cancellationToken: cancellationToken);
            var parts = completion.Value.Content;
            return parts.Count == 0 ? "" : parts[0].Text ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Map(ex);
        }
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

        ChatCompletion message;
        try
        {
            var completion = await _client.CompleteChatAsync(messages, options, cancellationToken);
            message = completion.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Map(ex);
        }
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

    static LlmException Map(Exception ex)
    {
        if (ex is LlmException llm)
        {
            return llm;
        }

        if (ex is ClientResultException client)
        {
            return LlmException.FromHttpStatus(client.Status, client.Message);
        }

        if (ex is HttpRequestException or IOException)
        {
            return LlmException.Network(ex);
        }

        var text = ex.Message ?? "";
        if (text.Contains("429", StringComparison.Ordinal) ||
            text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
        {
            return LlmException.FromHttpStatus(429, text);
        }

        return new LlmException(LlmErrorKind.Other, "Модель не ответила. Подробности в логе сервера.", text, ex);
    }
}
