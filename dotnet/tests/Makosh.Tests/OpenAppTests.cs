using Makosh.Core;

namespace Makosh.Tests;

public class OpenAppTests
{
    readonly FakeAppHost _host = new();

    [Theory]
    [InlineData("notepad", "notepad")]
    [InlineData("Блокнот", "notepad")]
    [InlineData("explorer", "explorer")]
    [InlineData("калькулятор", "calc")]
    public void Allowed_app_is_launched_by_name_only(string name, string executable)
    {
        var result = AppLauncher.Open(name, _host);
        Assert.Equal($"Запустил {executable}", result);
        Assert.Equal([executable], _host.Processes);
        Assert.Empty(_host.Urls);
    }

    [Fact]
    public void Browser_name_opens_a_page_without_process()
    {
        Assert.Equal("Открыл браузер", AppLauncher.Open("браузер", _host));
        Assert.Empty(_host.Processes);
        Assert.Equal(["https://google.com"], _host.Urls);
    }

    [Fact]
    public void Unknown_app_is_rejected()
    {
        var result = AppLauncher.Open("definitely-not-an-app", _host);
        Assert.Contains("нет в каталоге", result, StringComparison.Ordinal);
        Assert.Empty(_host.Processes);
        Assert.Empty(_host.Urls);
    }

    [Fact]
    public void Wrong_name_is_rejected()
    {
        Assert.Contains("нет в каталоге", AppLauncher.Open("notpad", _host), StringComparison.Ordinal);
        Assert.Empty(_host.Processes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_is_rejected(string name)
    {
        Assert.Contains("нет в каталоге", AppLauncher.Open(name, _host), StringComparison.Ordinal);
        Assert.Empty(_host.Processes);
        Assert.Empty(_host.Urls);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"..\notepad")]
    [InlineData("../notepad")]
    [InlineData(@"notepad\calc")]
    public void Path_is_not_accepted_as_an_app_name(string name)
    {
        var result = AppLauncher.Open(name, _host);
        Assert.Contains("нет в каталоге", result, StringComparison.Ordinal);
        Assert.Empty(_host.Processes);
        Assert.Empty(_host.Urls);
    }

    [Fact]
    public void Http_url_still_opens_in_the_browser()
    {
        Assert.Equal("Открыл https://example.com", AppLauncher.Open("https://example.com", _host));
        Assert.Empty(_host.Processes);
        Assert.Equal(["https://example.com"], _host.Urls);
    }

    [Fact]
    public void Cmd_is_currently_launchable()
    {
        Assert.Equal("Запустил cmd", AppLauncher.Open("cmd", _host));
        Assert.Equal(["cmd"], _host.Processes);
        Assert.Empty(_host.Urls);
    }

    [Fact]
    public void Powershell_is_rejected()
    {
        Assert.Contains("нет в каталоге", AppLauncher.Open("powershell", _host), StringComparison.Ordinal);
        Assert.Contains("нет в каталоге", AppLauncher.Open("pwsh", _host), StringComparison.Ordinal);
        Assert.Empty(_host.Processes);
    }

    [Fact]
    public void Registered_blender_alias_is_launchable()
    {
        Assert.Equal("Запустил blender", AppLauncher.Open("Blender", _host));
        Assert.Equal(["blender"], _host.Processes);
    }

    [Fact]
    public void Extra_catalog_entry_is_launchable()
    {
        var catalog = AppCatalog.From(
        [
            ..AppCatalog.DefaultApps(),
            new AppEntry { Id = "code", Aliases = ["vscode"], Kind = "process", Target = "Code" },
        ]);
        Assert.Equal("Запустил Code", AppLauncher.Open("vscode", _host, catalog));
        Assert.Equal(["Code"], _host.Processes);
    }
}
