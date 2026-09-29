using Makosh.Core;

namespace Makosh.Tests;

public sealed class LocalToolsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "makosh-m4-tools-" + Guid.NewGuid().ToString("N"));
    readonly List<Memory> _memories = [];

    public LocalToolsTests()
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

    LocalToolServices Services(FakeAppHost apps, FakeShell shell, FakeKeyboard keyboard, DeviceRegistry devices)
    {
        var data = Path.Combine(_root, "data");
        return new LocalToolServices
        {
            Paths = new PathGuard([_root]) { Home = _root, DataDirectory = data },
            Apps = apps,
            Shell = shell,
            Keyboard = keyboard,
            Devices = devices,
        };
    }

    [Fact]
    public void Agent_create_registers_local_tools_but_not_send_file_or_look_screen_until_wired()
    {
        var memory = new Memory(Path.Combine(_root, "memory.sqlite"));
        _memories.Add(memory);
        var local = Services(new FakeAppHost(), new FakeShell(), new FakeKeyboard(), new DeviceRegistry());
        var agent = Agent.Create(memory, new MakoshSettings { ApiKey = "" }, local);
        var names = agent.Tools.Select(tool => tool.Definition.Name).ToList();
        foreach (var name in new[] { "remember", "recall", "list_devices", "list_files", "open_app", "open_path", "type_text", "press_hotkey" })
        {
            Assert.Contains(name, names);
        }

        Assert.DoesNotContain("look_screen", names);
        Assert.DoesNotContain("send_file", names);
    }

    [Fact]
    public void Look_screen_is_registered_when_screen_service_is_present()
    {
        var memory = new Memory(Path.Combine(_root, "memory-vision.sqlite"));
        _memories.Add(memory);
        var local = Services(new FakeAppHost(), new FakeShell(), new FakeKeyboard(), new DeviceRegistry());
        local = new LocalToolServices
        {
            Paths = local.Paths,
            Apps = local.Apps,
            Shell = local.Shell,
            Keyboard = local.Keyboard,
            Devices = local.Devices,
            Screen = new ScreenLook(
                new FakeScreenCapture(ScreenImage.Solid(4, 4, 0, 0, 0)),
                new FakeOcrService("x"),
                new FakeJpegEncoder(),
                new VisionRateLimiter(6),
                new MakoshSettings()),
        };
        var names = ToolCatalog.LocalTools(local).Select(tool => tool.Definition.Name).ToList();
        Assert.Contains("look_screen", names);
        var schema = ToolCatalog.LookScreen.ParametersJson;
        Assert.Contains("\"ocr\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"vision\"", schema, StringComparison.Ordinal);
        Assert.Contains("mode", schema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_files_outside_root_becomes_tool_error()
    {
        var memory = new Memory(Path.Combine(_root, "m2.sqlite"));
        _memories.Add(memory);
        var local = Services(new FakeAppHost(), new FakeShell(), new FakeKeyboard(), new DeviceRegistry());
        var outside = Path.Combine(Path.GetTempPath(), "makosh-secret-" + Guid.NewGuid().ToString("N") + ".txt");
        var fake = new FakeChatClient(
            FakeChatClient.Tool("list_files", System.Text.Json.JsonSerializer.Serialize(new { path = outside })),
            FakeChatClient.Text("отказ"));
        var tools = ToolCatalog.MemoryTools(memory).Concat(ToolCatalog.LocalTools(local));
        var agent = new Agent(memory, new MakoshSettings { ApiKey = "x", Model = "m" }, fake, tools);
        Assert.Equal("отказ", await agent.HandleAsync("файлы", "web"));
        var tool = fake.Calls[1].Messages.Single(item => item.Role == "tool");
        Assert.Contains("вне разрешённых", tool.Content, StringComparison.Ordinal);
    }
}
