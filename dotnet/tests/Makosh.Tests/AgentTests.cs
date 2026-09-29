using Makosh.Core;
using System.Text.Json;

namespace Makosh.Tests;

public sealed class AgentTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "makosh-m3-" + Guid.NewGuid().ToString("N"));
    readonly List<Memory> _memories = [];

    public AgentTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var memory in _memories)
        {
            memory.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    (Agent Agent, Memory Memory, FakeChatClient? Fake) MakeAgent(FakeChatClient? fake, IEnumerable<ITool>? extraTools = null)
    {
        var memory = new Memory(Path.Combine(_root, Guid.NewGuid().ToString("N"), "memory.sqlite"));
        _memories.Add(memory);
        var settings = new MakoshSettings { Model = "openai/gpt-4o-mini", ApiKey = fake is null ? "" : "not-used" };
        var tools = new List<ITool>(ToolCatalog.MemoryTools(memory));
        if (extraTools is not null)
        {
            tools.AddRange(extraTools);
        }

        var agent = new Agent(memory, settings, fake, tools);
        return (agent, memory, fake);
    }

    [Fact]
    public async Task Without_client_local_model_stays_off()
    {
        var (agent, memory, _) = MakeAgent(null);
        var reply = await agent.HandleAsync("ping", "web");
        Assert.Contains("OPENAI_API_KEY", reply, StringComparison.Ordinal);
        Assert.Equal(reply, memory.RecentTurns()[^1].Content);
        Assert.Equal(new Turn("user", "[web] ping"), memory.RecentTurns()[0]);
    }

    [Fact]
    public async Task Text_reply_without_tools()
    {
        var fake = new FakeChatClient(FakeChatClient.Text("Привет."));
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("здравствуй", "web");
        Assert.Equal("Привет.", reply);
        Assert.Single(fake.Calls);
        Assert.NotEmpty(fake.Calls[0].Tools);
        Assert.Equal("openai/gpt-4o-mini", fake.Calls[0].Model);
        var turns = memory.RecentTurns();
        Assert.Equal(new Turn("user", "[web] здравствуй"), turns[0]);
        Assert.Equal(new Turn("assistant", "Привет."), turns[1]);
    }

    [Fact]
    public async Task Empty_model_content_becomes_done()
    {
        var fake = new FakeChatClient(FakeChatClient.Text("  \n"));
        var (agent, _, _) = MakeAgent(fake);
        Assert.Equal("Готово.", await agent.HandleAsync("ok", "web"));
    }

    [Fact]
    public async Task Tool_call_then_final_answer()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tool("remember", """{"key":"color","value":"blue"}"""),
            FakeChatClient.Text("Запомнил синий."));
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("запомни цвет", "web");
        Assert.Equal("Запомнил синий.", reply);
        Assert.Contains("color: blue", memory.Recall("color"), StringComparison.Ordinal);
        Assert.Equal(2, fake.Calls.Count);
        var toolMessages = fake.Calls[1].Messages.Where(item => item.Role == "tool").ToList();
        Assert.Contains("Запомнил: color = blue", toolMessages[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multiple_tool_rounds()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tool("remember", """{"key":"city","value":"Riga"}"""),
            FakeChatClient.Tool("recall", """{"query":"city"}"""),
            FakeChatClient.Text("Город Riga."));
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("город", "web");
        Assert.Equal("Город Riga.", reply);
        Assert.Equal(3, fake.Calls.Count);
        Assert.Contains("city: Riga", memory.Recall("city"), StringComparison.Ordinal);
        Assert.Contains("Riga", fake.Calls[2].Messages.Last(item => item.Role == "tool").Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_after_six_tool_rounds()
    {
        var steps = Enumerable.Range(0, 6)
            .Select(i => (object)FakeChatClient.Tool("remember", $$"""{"key":"n","value":"{{i}}"}""", $"call-{i}"))
            .ToArray();
        var fake = new FakeChatClient(steps);
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("много шагов", "web");
        Assert.Equal(Agent.TooManyRoundsReply, reply);
        Assert.Equal(6, fake.Calls.Count);
        Assert.Equal(Agent.TooManyRoundsReply, memory.RecentTurns()[^1].Content);
    }

    [Fact]
    public async Task Invalid_tool_arguments_stay_in_the_loop()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tool("remember", "{not json"),
            FakeChatClient.Text("Аргументы не разобрал."));
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("запомни", "web");
        Assert.Equal("Аргументы не разобрал.", reply);
        Assert.Equal("В памяти пока пусто.", memory.Recall("color"));
        var toolMessages = fake.Calls[1].Messages.Where(item => item.Role == "tool").ToList();
        Assert.Contains("неверные аргументы", toolMessages[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_object_tool_arguments_do_not_crash()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tool("remember", "[]"),
            FakeChatClient.Text("Нужен объект."));
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("запомни", "web");
        Assert.Equal("Нужен объект.", reply);
        Assert.Equal("В памяти пока пусто.", memory.Recall(""));
    }

    [Fact]
    public async Task Missing_tool_fields_are_reported_to_the_model()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tool("remember", "{}"),
            FakeChatClient.Text("Не хватает полей."));
        var (agent, memory, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("запомни", "web");
        Assert.Equal("Не хватает полей.", reply);
        var toolMessages = fake.Calls[1].Messages.Where(item => item.Role == "tool").ToList();
        Assert.StartsWith("Ошибка инструмента remember", toolMessages[0].Content);
        Assert.Equal("В памяти пока пусто.", memory.Recall(""));
    }

    [Fact]
    public async Task Unknown_tool_is_returned_to_the_model()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tool("not_a_tool", "{}"),
            FakeChatClient.Text("Нет такого."));
        var (agent, _, _) = MakeAgent(fake);
        var reply = await agent.HandleAsync("сделай", "web");
        Assert.Equal("Нет такого.", reply);
        Assert.Equal("Неизвестный инструмент: not_a_tool", fake.Calls[1].Messages.Last(item => item.Role == "tool").Content);
    }

    [Fact]
    public async Task Tool_exception_is_returned_to_the_model()
    {
        var boom = new BoomTool();
        var fake = new FakeChatClient(
            FakeChatClient.Tool("boom", "{}"),
            FakeChatClient.Text("Сломалось."));
        var (agent, _, _) = MakeAgent(fake, [boom]);
        Assert.Equal("Сломалось.", await agent.HandleAsync("бум", "web"));
        Assert.Equal("Ошибка инструмента boom: kaboom", fake.Calls[1].Messages.Last(item => item.Role == "tool").Content);
    }

    [Fact]
    public async Task Facts_turns_and_system_prompt_are_sent_to_the_model()
    {
        var fake = new FakeChatClient(FakeChatClient.Text("ok"));
        var (agent, memory, _) = MakeAgent(fake);
        memory.Remember("editor", "cursor");
        await agent.HandleAsync("ping", "phone");

        var messages = fake.Calls[0].Messages;
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(AgentPrompts.System, messages[0].Content);
        Assert.StartsWith("Известные факты:", messages[1].Content);
        Assert.Contains("editor: cursor", messages[1].Content, StringComparison.Ordinal);
        Assert.Contains(messages, item => item.Role == "user" && item.Content == "[phone] ping");

        var names = fake.Calls[0].Tools.Select(tool => tool.Name).ToList();
        Assert.Contains("remember", names);
        Assert.Contains("recall", names);
        var remember = fake.Calls[0].Tools.Single(tool => tool.Name == "remember");
        Assert.Equal("Сохранить факт в долгую память", remember.Description);
        using var schema = JsonDocument.Parse(remember.ParametersJson);
        Assert.Equal(JsonValueKind.Object, schema.RootElement.ValueKind);
        Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("key", out _));
    }

    [Fact]
    public async Task Several_tool_calls_in_one_round()
    {
        var fake = new FakeChatClient(
            FakeChatClient.Tools(
                ("remember", """{"key":"a","value":"1"}""", "c1"),
                ("remember", """{"key":"b","value":"2"}""", "c2")),
            FakeChatClient.Text("оба."));
        var (agent, memory, _) = MakeAgent(fake);
        Assert.Equal("оба.", await agent.HandleAsync("запомни два", "web"));
        Assert.Contains("a: 1", memory.Recall("a"), StringComparison.Ordinal);
        Assert.Contains("b: 2", memory.Recall("b"), StringComparison.Ordinal);
        Assert.Equal(2, fake.Calls[1].Messages.Count(item => item.Role == "tool"));
    }

    [Fact]
    public async Task Create_without_api_key_does_not_require_a_client()
    {
        var memory = new Memory(Path.Combine(_root, "create.sqlite"));
        _memories.Add(memory);
        var agent = Agent.Create(memory, new MakoshSettings { ApiKey = "" });
        Assert.Equal(Agent.MissingApiKeyReply, await agent.HandleAsync("hi", "web"));
    }

    sealed class BoomTool : ITool
    {
        public ToolDefinition Definition { get; } = new()
        {
            Name = "boom",
            Description = "взрыв",
            ParametersJson = """{"type":"object","properties":{}}""",
        };

        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("kaboom");
    }
}
