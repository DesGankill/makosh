using System.Text.Json;
using Makosh.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Makosh.Tests;

public class HealthTests : IClassFixture<MakoshWebFactory>
{
    readonly HttpClient _client;

    public HealthTests(MakoshWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_does_not_require_token_or_api_key()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("makosh", root.GetProperty("ok").GetString());
        Assert.Equal("M1-Test", root.GetProperty("device").GetString());
        Assert.Equal("dotnet", root.GetProperty("runtime").GetString());
    }

    [Fact]
    public async Task Token_check_rejects_missing_and_wrong_token()
    {
        var missing = await _client.GetAsync("/api/token-check");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missing.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/token-check");
        wrong.Headers.Add("x-makosh-token", "nope");
        var denied = await _client.SendAsync(wrong);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Token_check_accepts_configured_token()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/token-check");
        request.Headers.Add("x-makosh-token", "m1-test-token");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Root_serves_python_compatible_ui()
    {
        var html = await _client.GetStringAsync("/");
        Assert.Contains("Makosh", html, StringComparison.Ordinal);
        Assert.Contains("MAKOSH_TOKEN", html, StringComparison.Ordinal);
        Assert.Contains("/ws", html, StringComparison.Ordinal);
    }
}

public class SettingsTests
{
    [Fact]
    public void Environment_overrides_env_file()
    {
        var previous = Environment.GetEnvironmentVariable("MAKOSH_DEVICE_NAME");
        try
        {
            Environment.SetEnvironmentVariable("MAKOSH_DEVICE_NAME", "FromEnv");
            var settings = MakoshSettings.Load();
            Assert.Equal("FromEnv", settings.DeviceName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAKOSH_DEVICE_NAME", previous);
        }
    }

    [Fact]
    public void Env_file_is_read_when_variable_is_absent()
    {
        var previousFile = Environment.GetEnvironmentVariable("MAKOSH_ENV_FILE");
        var previousDevice = Environment.GetEnvironmentVariable("MAKOSH_DEVICE_NAME");
        var temp = Path.Combine(Path.GetTempPath(), "makosh-m1-settings.env");
        File.WriteAllText(temp, "MAKOSH_DEVICE_NAME=FromFile\n");
        try
        {
            Environment.SetEnvironmentVariable("MAKOSH_DEVICE_NAME", null);
            Environment.SetEnvironmentVariable("MAKOSH_ENV_FILE", temp);
            var settings = MakoshSettings.Load();
            Assert.Equal("FromFile", settings.DeviceName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAKOSH_ENV_FILE", previousFile);
            Environment.SetEnvironmentVariable("MAKOSH_DEVICE_NAME", previousDevice);
            File.Delete(temp);
        }
    }

    [Fact]
    public void Vision_limits_come_from_environment()
    {
        var previousModel = Environment.GetEnvironmentVariable("VISION_MODEL");
        var previousHour = Environment.GetEnvironmentVariable("VISION_MAX_PER_HOUR");
        var previousWidth = Environment.GetEnvironmentVariable("VISION_MAX_WIDTH");
        var previousChat = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_MODEL", "openai/gpt-4o-mini");
            Environment.SetEnvironmentVariable("VISION_MODEL", "openai/gpt-4o");
            Environment.SetEnvironmentVariable("VISION_MAX_PER_HOUR", "3");
            Environment.SetEnvironmentVariable("VISION_MAX_WIDTH", "512");
            var settings = MakoshSettings.Load();
            Assert.Equal("openai/gpt-4o", settings.VisionModel);
            Assert.Equal(3, settings.VisionMaxPerHour);
            Assert.Equal(512, settings.VisionMaxWidth);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISION_MODEL", previousModel);
            Environment.SetEnvironmentVariable("VISION_MAX_PER_HOUR", previousHour);
            Environment.SetEnvironmentVariable("VISION_MAX_WIDTH", previousWidth);
            Environment.SetEnvironmentVariable("OPENAI_MODEL", previousChat);
        }
    }
}

public class MakoshWebFactory : WebApplicationFactory<Program>
{
    public FakeChatClient Fake { get; } = new(FakeChatClient.Text("Ответ."));
    public string DataDir { get; }

    public MakoshWebFactory()
    {
        DataDir = Path.Combine(Path.GetTempPath(), "makosh-m5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("MAKOSH_TOKEN", "m1-test-token");
        Environment.SetEnvironmentVariable("MAKOSH_DEVICE_NAME", "M1-Test");
        Environment.SetEnvironmentVariable("MAKOSH_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "");
        Environment.SetEnvironmentVariable("MAKOSH_DATA_DIR", DataDir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(sp =>
            {
                var memory = sp.GetRequiredService<Memory>();
                var settings = sp.GetRequiredService<MakoshSettings>();
                var local = sp.GetRequiredService<LocalToolServices>();
                return new Agent(memory, settings, Fake, ToolCatalog.MemoryTools(memory).Concat(ToolCatalog.LocalTools(local)));
            });
        });
    }
}
