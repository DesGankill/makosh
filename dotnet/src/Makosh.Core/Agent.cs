using System.Text.Json;

namespace Makosh.Core;

public sealed class Agent
{
    public const int MaxToolRounds = 6;
    public const string MissingApiKeyReply =
        "Нет OPENAI_API_KEY в .env — облачная модель выключена. Могу только локальные команды позже.";
    public const string EmptyReply = "Готово.";
    public const string TooManyRoundsReply = "Слишком длинная цепочка действий, остановился.";

    readonly Memory _memory;
    readonly MakoshSettings _settings;
    readonly IChatClient? _chat;
    readonly IReadOnlyList<ITool> _tools;
    readonly Dictionary<string, ITool> _byName;

    public Agent(Memory memory, MakoshSettings settings, IChatClient? chatClient, IEnumerable<ITool> tools)
    {
        _memory = memory;
        _settings = settings;
        _chat = chatClient;
        _tools = tools.ToList();
        _byName = new Dictionary<string, ITool>(StringComparer.Ordinal);
        foreach (var tool in _tools)
        {
            _byName[tool.Definition.Name] = tool;
        }
    }

    public IReadOnlyList<ITool> Tools => _tools;

    public static Agent Create(Memory memory, MakoshSettings settings, LocalToolServices? local = null, IEnumerable<ITool>? extraTools = null)
    {
        var tools = new List<ITool>(ToolCatalog.MemoryTools(memory));
        if (local is not null)
        {
            tools.AddRange(ToolCatalog.LocalTools(local));
        }

        if (extraTools is not null)
        {
            tools.AddRange(extraTools);
        }

        IChatClient? chat = null;
        if (settings.HasChatModel)
        {
            chat = new OpenAIChatClient(settings);
        }

        return new Agent(memory, settings, chat, tools);
    }

    public async Task<string> HandleAsync(string text, string speaker, CancellationToken cancellationToken = default)
    {
        _memory.AddTurn("user", $"[{speaker}] {text}");
        if (_chat is null)
        {
            _memory.AddTurn("assistant", MissingApiKeyReply);
            return MissingApiKeyReply;
        }

        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = AgentPrompts.System },
            new() { Role = "system", Content = $"Известные факты:\n{_memory.Recall("")}" },
        };
        foreach (var turn in _memory.RecentTurns())
        {
            messages.Add(new LlmMessage { Role = turn.Role, Content = turn.Content });
        }

        var toolDefs = _tools.Select(tool => tool.Definition).ToList();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _chat.CompleteAsync(
                new LlmRequest
                {
                    Model = _settings.Model,
                    Messages = messages,
                    Tools = toolDefs,
                },
                cancellationToken);

            if (response.ToolCalls.Count == 0)
            {
                var reply = string.IsNullOrWhiteSpace(response.Content) ? EmptyReply : response.Content.Trim();
                _memory.AddTurn("assistant", reply);
                return reply;
            }

            messages.Add(new LlmMessage
            {
                Role = "assistant",
                Content = response.Content ?? "",
                ToolCalls = response.ToolCalls,
            });

            foreach (var call in response.ToolCalls)
            {
                var result = await InvokeToolAsync(call.Name, call.Arguments, cancellationToken);
                messages.Add(new LlmMessage
                {
                    Role = "tool",
                    Content = result,
                    ToolCallId = call.Id,
                });
            }
        }

        _memory.AddTurn("assistant", TooManyRoundsReply);
        return TooManyRoundsReply;
    }

    async Task<string> InvokeToolAsync(string name, string? rawArguments, CancellationToken cancellationToken)
    {
        JsonElement args;
        try
        {
            var raw = string.IsNullOrEmpty(rawArguments) ? "{}" : rawArguments;
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return $"Ошибка инструмента {name}: неверные аргументы";
            }

            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return $"Ошибка инструмента {name}: неверные аргументы";
        }

        if (!_byName.TryGetValue(name, out var tool))
        {
            return $"Неизвестный инструмент: {name}";
        }

        try
        {
            return await tool.ExecuteAsync(args, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Ошибка инструмента {name}: {ex.Message}";
        }
    }
}
