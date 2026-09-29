using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Makosh.Core;

namespace Makosh;

public sealed class HubFileSender : IFileSender
{
    readonly DeviceRegistry _devices;
    readonly PathGuard _paths;

    public HubFileSender(DeviceRegistry devices, PathGuard paths)
    {
        _devices = devices;
        _paths = paths;
    }

    public async Task<string> SendAsync(string path, string deviceHint, CancellationToken cancellationToken = default)
    {
        var target = _devices.Find(deviceHint);
        if (target is null)
        {
            var dest = _paths.CopyIntoInbox(path, "unclaimed");
            return $"Устройство «{deviceHint}» не в сети. Файл лежит в inbox: {dest}";
        }

        var saved = _paths.CopyIntoInbox(path, target.DeviceId);
        var name = Path.GetFileName(saved);
        var url = $"/api/inbox/{target.DeviceId}/{name}";
        await _devices.SendJsonAsync(target.DeviceId, new Dictionary<string, string>
        {
            ["type"] = "file",
            ["name"] = name,
            ["url"] = url,
        }, cancellationToken);
        return $"Отправил {name} на {target.Name}. Ссылка: {url}";
    }
}

public sealed class WebSocketDeviceConnection : IDeviceConnection
{
    readonly WebSocket _socket;
    readonly SemaphoreSlim _send = new(1, 1);

    public WebSocketDeviceConnection(WebSocket socket)
    {
        _socket = socket;
    }

    public async Task SendJsonAsync(object payload, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _send.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _send.Release();
        }
    }
}

public static class HubJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

public sealed class ChatBody
{
    public string Text { get; set; } = "";
    public bool Speak { get; set; } = true;
    [System.Text.Json.Serialization.JsonPropertyName("device_name")]
    public string DeviceName { get; set; } = "web";
}
