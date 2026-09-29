using Makosh.Core;

namespace Makosh.Tests;

public sealed class FakeAppHost : IAppHost
{
    public List<string> Processes { get; } = [];
    public List<string> Urls { get; } = [];

    public void StartProcess(string fileName) => Processes.Add(fileName);

    public void OpenUrl(string url) => Urls.Add(url);
}

public sealed class FakeShell : IShell
{
    public List<string> Opened { get; } = [];

    public void OpenFileOrFolder(string path) => Opened.Add(path);
}

public sealed class FakeKeyboard : IKeyboard
{
    public List<IReadOnlyList<KeyStroke>> Sent { get; } = [];

    public void Send(IReadOnlyList<KeyStroke> strokes) => Sent.Add(strokes);
}
