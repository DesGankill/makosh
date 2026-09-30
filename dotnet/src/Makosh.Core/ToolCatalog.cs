using System.Text.Json;

namespace Makosh.Core;

/// <summary>
/// OpenAI function schemas matching <c>makosh/agent.py</c> TOOL_SCHEMAS.
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
        Description = "Локальный OCR экрана (не картинка). mode=ocr|vision|skip. query — вопрос пользователя, на него и отвечай.",
        ParametersJson =
            """
            {"type":"object","properties":{"mode":{"type":"string","enum":["ocr","vision","skip"]},"skip_capture":{"type":"boolean"},"query":{"type":"string"}},"required":["mode"]}
            """,
    };

    public static ToolDefinition OpenApp { get; } = new()
    {
        Name = "open_app",
        Description = "Открыть приложение по короткому имени из каталога (blender, steam, discord, youtube, calc). Не передавай путь к exe.",
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

    /// <summary>
    /// Local PC tools. <see cref="LookScreen"/> is registered when <see cref="LocalToolServices.Screen"/> is set.
    /// </summary>
    public static IReadOnlyList<ITool> LocalTools(LocalToolServices local)
    {
        var tools = new List<ITool>
        {
            new DelegateTool(ListDevices, _ => local.Devices.ListText()),
            new DelegateTool(ListFiles, args => local.Paths.ListFiles(args.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "")),
            new DelegateTool(OpenApp, args => AppLauncher.Open(
                args.GetProperty("name").GetString() ?? "",
                local.Apps,
                local.AppsCatalog)),
            new DelegateTool(OpenPath, args => PathOpener.Open(args.GetProperty("path").GetString() ?? "", local.Paths, local.Shell)),
            new DelegateTool(TypeText, args => KeyboardComposer.TypeText(args.GetProperty("text").GetString() ?? "", local.Keyboard)),
            new DelegateTool(PressHotkey, args => KeyboardComposer.PressHotkey(args.GetProperty("keys").GetString() ?? "", local.Keyboard)),
        };
        if (local.Files is not null)
        {
            tools.Add(new AsyncDelegateTool(SendFile, (args, ct) => local.Files.SendAsync(
                args.GetProperty("path").GetString() ?? "",
                args.GetProperty("device").GetString() ?? "",
                ct)));
        }

        if (local.Screen is not null)
        {
            tools.Add(new AsyncDelegateTool(LookScreen, (args, ct) =>
            {
                var mode = args.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() ?? "ocr" : "ocr";
                var skip = args.TryGetProperty("skip_capture", out var skipEl) &&
                           skipEl.ValueKind is JsonValueKind.True;
                var query = args.TryGetProperty("query", out var queryEl) ? queryEl.GetString() : null;
                return local.Screen.LookAsync(mode, skip, query, ct);
            }));
        }

        return tools;
    }

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

    sealed class DelegateTool(ToolDefinition definition, Func<JsonElement, string> execute) : ITool
    {
        public ToolDefinition Definition => definition;

        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) =>
            Task.FromResult(execute(arguments));
    }

    sealed class AsyncDelegateTool(ToolDefinition definition, Func<JsonElement, CancellationToken, Task<string>> execute) : ITool
    {
        public ToolDefinition Definition => definition;

        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) =>
            execute(arguments, cancellationToken);
    }
}
