namespace Makosh.Core;

public interface IAppHost
{
    void StartProcess(string fileName);
    void OpenUrl(string url);
}

public interface IShell
{
    void OpenFileOrFolder(string path);
}

public interface IKeyboard
{
    void Send(IReadOnlyList<KeyStroke> strokes);
}

public sealed class LocalToolServices
{
    public required PathGuard Paths { get; init; }
    public required IAppHost Apps { get; init; }
    public required IShell Shell { get; init; }
    public required IKeyboard Keyboard { get; init; }
    public required DeviceRegistry Devices { get; init; }
    public IFileSender? Files { get; init; }
}
