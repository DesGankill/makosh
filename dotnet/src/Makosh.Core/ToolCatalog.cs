using System.Text.Json;

namespace Makosh.Core;

/// <summary>
/// OpenAI function schemas matching <c>makosh/agent.py</c> TOOL_SCHEMAS.
/// Windows tools are registered in later milestones; memory tools are created here.
/// </summary>
public static class ToolCatalog
{
    public static ToolDefinition Remember { get; } = new()
    {
        Name = "remember",
        Description = "Сохранить факт в долгую память",
        ParametersJson =
            """
            {"type":"object","properties":{"key":{"type":"string"},"value":{"type":"string"}},"required":["key","value"]}
            """,
    };

    public static ToolDefinition Recall { get; } = new()
    {
        Name = "recall",
        Description = "Найти факты в памяти",
        ParametersJson =
            """
            {"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}
            """,
    };

    public static ToolDefinition ListDevices { get; } = new()
    {
        Name = "list_devices",
        Description = "Список подключённых устройств",
        ParametersJson = """{"type":"object","properties":{}}""",
    };

    public static ToolDefinition ListFiles { get; } = new()
    {
        Name = "list_files",
        Description = "Список файлов в папке (Рабочий стол, Загрузки, Документы)",
        ParametersJson =
            """
            {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
            """,
    };

    public static ToolDefinition SendFile { get; } = new()
    {
        Name = "send_file",
        Description = "Скопировать файл в inbox устройства и отправить ссылку",
        ParametersJson =
            """
            {"type":"object","properties":{"path":{"type":"string"},"device":{"type":"string","description":"телефон, android, имя устройства"}},"required":["path","device"]}
            """,
    };

    public static ToolDefinition LookScreen { get; } = new()
    {
        Name = "look_screen",
        Description = "Посмотреть экран ПК. mode=ocr (бесплатно) или vision (облако, лимит)",
        ParametersJson =
            """
            {"type":"object","properties":{"mode":{"type":"string","enum":["ocr","vision"]}},"required":["mode"]}
            """,
    };

    public static ToolDefinition OpenApp { get; } = new()
    {
        Name = "open_app",
        Description = "Открыть приложение из белого списка",
        ParametersJson =
            """
            {"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}
            """,
    };

    public static ToolDefinition OpenPath { get; } = new()
    {
        Name = "open_path",
        Description = "Открыть файл или папку",
        ParametersJson =
            """
            {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
            """,
    };

    public static ToolDefinition TypeText { get; } = new()
    {
        Name = "type_text",
        Description = "Напечатать текст в активном окне ПК",
        ParametersJson =
            """
            {"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}
            """,
    };

    public static ToolDefinition PressHotkey { get; } = new()
    {
        Name = "press_hotkey",
        Description = "Нажать комбинацию, например ctrl c",
        ParametersJson =
            """
            {"type":"object","properties":{"keys":{"type":"string"}},"required":["keys"]}
            """,
    };

    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        Remember, Recall, ListDevices, ListFiles, SendFile, LookScreen, OpenApp, OpenPath, TypeText, PressHotkey,
    ];

    public static IReadOnlyList<ITool> MemoryTools(Memory memory) =>
    [
        new RememberTool(memory),
        new RecallTool(memory),
    ];

    sealed class RememberTool(Memory memory) : ITool
    {
        public ToolDefinition Definition => Remember;

        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var key = arguments.GetProperty("key").GetString() ?? "";
            var value = arguments.GetProperty("value").GetString() ?? "";
            return Task.FromResult(memory.Remember(key, value));
        }
    }

    sealed class RecallTool(Memory memory) : ITool
    {
        public ToolDefinition Definition => Recall;

        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            return Task.FromResult(memory.Recall(query));
        }
    }
}
