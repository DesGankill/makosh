using Makosh.Core;

namespace Makosh.Tests;

public class ScreenImageTests
{
    [Fact]
    public void FitMaxWidth_shrinks_wide_images_and_does_not_upscale()
    {
        var wide = ScreenImage.Solid(1920, 1080, 0, 0, 255);
        var fitted = wide.FitMaxWidth(768);
        Assert.Equal(768, fitted.Width);
        Assert.Equal(432, fitted.Height);

        var small = ScreenImage.Solid(320, 200, 255, 0, 0);
        Assert.Same(small, small.FitMaxWidth(768));
        Assert.Equal(320, small.FitMaxWidth(768).Width);
    }
}

public class VisionRateLimiterTests
{
    [Fact]
    public void Rolling_hour_allows_max_then_rejects_until_expiry()
    {
        var now = 1_000.0;
        var limiter = new VisionRateLimiter(2, () => now);
        Assert.Equal(2, limiter.CallsLeft());
        Assert.True(limiter.TryConsume());
        Assert.Equal(1, limiter.CallsLeft());
        Assert.True(limiter.TryConsume());
        Assert.False(limiter.TryConsume());
        Assert.Equal(0, limiter.CallsLeft());
        now += 3601;
        Assert.Equal(2, limiter.CallsLeft());
        Assert.True(limiter.TryConsume());
    }

    [Fact]
    public void Six_cloud_slots_then_seventh_rejected()
    {
        var limiter = new VisionRateLimiter(6, () => 50);
        for (var i = 0; i < 6; i++)
        {
            Assert.True(limiter.TryConsume());
        }

        Assert.False(limiter.TryConsume());
        Assert.Equal(0, limiter.CallsLeft());
    }
}

public class ScreenLookTests
{
    static MakoshSettings Settings(
        string apiKey = "k",
        string baseUrl = "https://openrouter.ai/api/v1",
        string visionModel = "openai/gpt-4o-mini",
        int maxPerHour = 6,
        int maxWidth = 768) =>
        new()
        {
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            Model = "openai/gpt-4o-mini",
            VisionModel = visionModel,
            VisionMaxPerHour = maxPerHour,
            VisionMaxWidth = maxWidth,
        };

    static ScreenLook Make(
        out FakeScreenCapture capture,
        out FakeOcrService ocr,
        out FakeJpegEncoder jpeg,
        out FakeVisionClient vision,
        out VisionRateLimiter limiter,
        MakoshSettings? settings = null,
        IScreenCapture? captureOverride = null,
        IOcrService? ocrOverride = null,
        int maxPerHour = 6)
    {
        capture = new FakeScreenCapture(ScreenImage.Solid(1000, 20, 10, 20, 30));
        ocr = new FakeOcrService("Привет Makosh");
        jpeg = new FakeJpegEncoder();
        vision = new FakeVisionClient();
        limiter = new VisionRateLimiter(maxPerHour, () => 10);
        var look = new ScreenLook(
            captureOverride ?? capture,
            ocrOverride ?? ocr,
            jpeg,
            limiter,
            settings ?? Settings(maxPerHour: maxPerHour),
            vision);
        return look;
    }

    [Fact]
    public async Task Ocr_mode_returns_local_text_and_never_sends_an_image()
    {
        var look = Make(out _, out _, out var jpeg, out var vision, out var limiter);
        var text = await look.LookAsync("ocr");
        Assert.Contains("без облака", text, StringComparison.Ordinal);
        Assert.Contains("Привет Makosh", text, StringComparison.Ordinal);
        Assert.False(vision.Called);
        Assert.Null(jpeg.LastImage);
        Assert.Equal(6, limiter.CallsLeft());
    }

    [Fact]
    public async Task Empty_ocr_explains_that_only_local_text_is_available()
    {
        var capture = new FakeScreenCapture(ScreenImage.Solid(8, 8, 255, 255, 255));
        var look = new ScreenLook(
            capture,
            new FakeOcrService(""),
            new FakeJpegEncoder(),
            new VisionRateLimiter(6),
            Settings(),
            new FakeVisionClient());
        var text = await look.LookAsync("ocr");
        Assert.Contains("OCR ничего не разобрал", text, StringComparison.Ordinal);
        Assert.Contains("облачное зрение недоступно", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepSeek_backend_does_not_send_an_image()
    {
        var look = Make(
            out _,
            out _,
            out var jpeg,
            out var vision,
            out _,
            Settings(baseUrl: "https://api.deepseek.com/v1"));
        var text = await look.LookAsync("vision");
        Assert.False(vision.Called);
        Assert.Null(jpeg.LastImage);
        Assert.Contains("не умеет смотреть картинки", text, StringComparison.Ordinal);
        Assert.Contains("Привет Makosh", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepSeek_model_name_blocks_images()
    {
        var look = Make(
            out _,
            out _,
            out _,
            out var vision,
            out _,
            Settings(visionModel: "deepseek/deepseek-chat"));
        var text = await look.LookAsync("vision");
        Assert.False(vision.Called);
        Assert.Contains("не умеет смотреть картинки", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cloud_vision_resizes_and_encodes_jpeg_quality_45()
    {
        var look = Make(out _, out _, out var jpeg, out var vision, out _);
        var text = await look.LookAsync("vision");
        Assert.True(vision.Called);
        Assert.Equal(768, jpeg.LastImage!.Width);
        Assert.Equal(VisionJpeg.Quality, jpeg.LastQuality);
        Assert.Equal(jpeg.LastBytes, vision.LastJpeg);
        Assert.Equal(0xFF, vision.LastJpeg![0]);
        Assert.Equal(0xD8, vision.LastJpeg[1]);
        Assert.Contains("на экране код", text, StringComparison.Ordinal);
        Assert.Contains("осталось в этом часе: 5", text, StringComparison.Ordinal);
        Assert.Contains("Привет Makosh", vision.LastPrompt, StringComparison.Ordinal);
        Assert.Equal("openai/gpt-4o-mini", vision.LastModel);
    }

    [Fact]
    public async Task Missing_api_key_does_not_send_an_image()
    {
        var look = Make(out _, out _, out var jpeg, out var vision, out _, Settings(apiKey: ""));
        var text = await look.LookAsync("vision");
        Assert.Equal("Нет ключа API для зрения.", text);
        Assert.False(vision.Called);
        Assert.NotNull(jpeg.LastImage);
    }

    [Fact]
    public async Task Rate_limit_keeps_ocr_and_ocr_mode_does_not_consume_quota()
    {
        var look = Make(out _, out _, out _, out var vision, out var limiter, maxPerHour: 1);
        Assert.True(limiter.TryConsume());
        vision.Called = false;
        var limited = await look.LookAsync("vision");
        Assert.False(vision.Called);
        Assert.Contains("Лимит облачного зрения исчерпан", limited, StringComparison.Ordinal);
        Assert.Contains("Привет Makosh", limited, StringComparison.Ordinal);

        var ocr = await look.LookAsync("ocr");
        Assert.Contains("без облака", ocr, StringComparison.Ordinal);
        Assert.False(vision.Called);
        Assert.Equal(0, limiter.CallsLeft());
    }

    [Fact]
    public async Task Six_vision_calls_then_seventh_is_rejected()
    {
        var look = Make(out _, out _, out _, out var vision, out _, maxPerHour: 6);
        for (var i = 0; i < 6; i++)
        {
            await look.LookAsync("vision");
        }

        vision.Called = false;
        var seventh = await look.LookAsync("vision");
        Assert.False(vision.Called);
        Assert.Contains("Лимит облачного зрения исчерпан (6/час)", seventh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capture_failure_bubbles_for_the_agent_tool_wrapper()
    {
        IScreenCapture boom = new ThrowingCapture();
        var look = Make(out _, out _, out _, out _, out _, captureOverride: boom);
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => look.LookAsync("ocr"));
        Assert.Equal("screenshot missing", ex.Message);
    }

    [Fact]
    public async Task Ocr_failure_bubbles_for_the_agent_tool_wrapper()
    {
        var look = Make(out _, out _, out _, out _, out _, ocrOverride: new ThrowingOcr());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => look.LookAsync("ocr"));
        Assert.Equal("ocr failed", ex.Message);
    }

    sealed class ThrowingCapture : IScreenCapture
    {
        public Task<ScreenImage> CaptureAsync(CancellationToken cancellationToken = default) =>
            throw new FileNotFoundException("screenshot missing");
    }

    sealed class ThrowingOcr : IOcrService
    {
        public Task<string> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("ocr failed");
    }
}

public sealed class LookScreenAgentTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "makosh-m6-agent-" + Guid.NewGuid().ToString("N"));
    readonly List<Memory> _memories = [];

    public LookScreenAgentTests()
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

    [Fact]
    public async Task Agent_look_screen_ocr_feeds_tool_result_to_the_model()
    {
        var memory = new Memory(Path.Combine(_root, "memory.sqlite"));
        _memories.Add(memory);
        var local = new LocalToolServices
        {
            Paths = new PathGuard([_root]) { Home = _root, DataDirectory = Path.Combine(_root, "data") },
            Apps = new FakeAppHost(),
            Shell = new FakeShell(),
            Keyboard = new FakeKeyboard(),
            Devices = new DeviceRegistry(),
            Screen = new ScreenLook(
                new FakeScreenCapture(ScreenImage.Solid(16, 16, 1, 2, 3)),
                new FakeOcrService("Привет Makosh"),
                new FakeJpegEncoder(),
                new VisionRateLimiter(6),
                new MakoshSettings { ApiKey = "x", VisionModel = "openai/gpt-4o-mini" }),
        };
        var fake = new FakeChatClient(
            FakeChatClient.Tool("look_screen", """{"mode":"ocr"}"""),
            FakeChatClient.Text("видел."));
        var agent = new Agent(
            memory,
            new MakoshSettings { ApiKey = "x", Model = "m" },
            fake,
            ToolCatalog.MemoryTools(memory).Concat(ToolCatalog.LocalTools(local)));
        Assert.Equal("видел.", await agent.HandleAsync("посмотри экран", "web"));
        var tool = fake.Calls[1].Messages.Last(item => item.Role == "tool");
        Assert.Contains("Привет Makosh", tool.Content, StringComparison.Ordinal);
        Assert.Contains("без облака", tool.Content, StringComparison.Ordinal);
        Assert.Contains("look_screen", fake.Calls[0].Tools.Select(item => item.Name));
    }

    [Fact]
    public async Task Agent_turns_screenshot_failure_into_tool_error()
    {
        var memory = new Memory(Path.Combine(_root, "err.sqlite"));
        _memories.Add(memory);
        var local = new LocalToolServices
        {
            Paths = new PathGuard([_root]) { Home = _root, DataDirectory = Path.Combine(_root, "data") },
            Apps = new FakeAppHost(),
            Shell = new FakeShell(),
            Keyboard = new FakeKeyboard(),
            Devices = new DeviceRegistry(),
            Screen = new ScreenLook(
                new FakeScreenCapture(() => throw new FileNotFoundException("screenshot missing")),
                new FakeOcrService("x"),
                new FakeJpegEncoder(),
                new VisionRateLimiter(6),
                new MakoshSettings { ApiKey = "x" }),
        };
        var fake = new FakeChatClient(
            FakeChatClient.Tool("look_screen", """{"mode":"ocr"}"""),
            FakeChatClient.Text("не вышло."));
        var agent = new Agent(
            memory,
            new MakoshSettings { ApiKey = "x", Model = "m" },
            fake,
            ToolCatalog.MemoryTools(memory).Concat(ToolCatalog.LocalTools(local)));
        Assert.Equal("не вышло.", await agent.HandleAsync("экран", "web"));
        var tool = fake.Calls[1].Messages.Last(item => item.Role == "tool");
        Assert.StartsWith("Ошибка инструмента look_screen", tool.Content);
        Assert.Contains("screenshot missing", tool.Content, StringComparison.Ordinal);
    }
}
