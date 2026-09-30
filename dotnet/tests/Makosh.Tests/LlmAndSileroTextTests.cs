using Makosh.Core;

namespace Makosh.Tests;

public class SileroTextTests
{
    [Fact]
    public void Markdown_latin_and_brackets_are_spoken_without_raw_symbols()
    {
        var spoken = SileroText.Sanitize(
            "Только функция **look_screen** в режиме **vision** требует обращения к внешней модели...");
        Assert.DoesNotContain("*", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("(", spoken, StringComparison.Ordinal);
        Assert.DoesNotMatch("[A-Za-z]", spoken);
        Assert.Contains("просмотр экрана", spoken, StringComparison.Ordinal);
        Assert.Contains("зрение", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void Url_becomes_a_spoken_word()
    {
        var spoken = SileroText.Sanitize("смотри https://example.com/path");
        Assert.Contains("ссылка", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("http", spoken, StringComparison.OrdinalIgnoreCase);
    }
}

public class LlmProviderTests
{
    [Fact]
    public void Xai_default_url_is_not_openrouter()
    {
        Assert.Equal("https://api.x.ai/v1", LlmProviders.DefaultBaseUrl("grok"));
        Assert.Equal("xai", LlmProviders.Normalize("grok"));
    }

    [Fact]
    public async Task Browser_provider_explains_it_is_not_wired()
    {
        var client = LlmProviderFactory.Create(new MakoshSettings
        {
            LlmProvider = "browser",
            ApiKey = "",
        });
        Assert.NotNull(client);
        var ex = await Assert.ThrowsAsync<LlmException>(() => client!.CompleteAsync(
            new LlmRequest { Model = "x", Messages = [], Tools = [] },
            CancellationToken.None));
        Assert.Equal(LlmErrorKind.Unavailable, ex.Kind);
        Assert.Contains("Браузерный LLM ещё не подключён", ex.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failover_uses_backup_once_after_429()
    {
        var primary = new FakeChatClient(LlmException.FromHttpStatus(429));
        var fallback = new FakeChatClient(FakeChatClient.Text("запасной"));
        var chain = new FailoverChatClient(primary, fallback, TimeSpan.FromMinutes(1));
        var first = await chain.CompleteAsync(
            new LlmRequest { Model = "m", Messages = [], Tools = [] },
            CancellationToken.None);
        Assert.Equal("запасной", first.Content);
        fallback.Reset(FakeChatClient.Text("снова"));
        var second = await chain.CompleteAsync(
            new LlmRequest { Model = "m", Messages = [], Tools = [] },
            CancellationToken.None);
        Assert.Equal("снова", second.Content);
        Assert.Single(primary.Calls);
    }

    [Fact]
    public void Tool_permissions_mark_hotkeys_as_confirm_required()
    {
        Assert.Equal(ToolAccess.Safe, ToolPermissions.Level("look_screen"));
        Assert.Equal(ToolAccess.ConfirmRequired, ToolPermissions.Level("type_text"));
        Assert.Equal(ToolAccess.ConfirmRequired, ToolPermissions.Level("press_hotkey"));
        Assert.Equal(ToolAccess.ReadOnly, ToolPermissions.Level("list_files"));
    }
}
