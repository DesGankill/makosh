namespace Makosh.Core;

public sealed class Device
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public IDeviceConnection? Connection { get; init; }
    public DateTimeOffset LastSeen { get; init; } = DateTimeOffset.UtcNow;
}

public interface IDeviceConnection
{
    Task SendJsonAsync(object payload, CancellationToken cancellationToken = default);
}

public interface IFileSender
{
    Task<string> SendAsync(string path, string deviceHint, CancellationToken cancellationToken = default);
}

public sealed class DeviceRegistry
{
    readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    readonly object _gate = new();

    static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["телефон"] = "android",
        ["андроид"] = "android",
        ["phone"] = "android",
        ["пк"] = "pc",
        ["комп"] = "pc",
        ["компьютер"] = "pc",
    };

    public void Register(Device device)
    {
        lock (_gate)
        {
            _devices[device.DeviceId] = device;
        }
    }

    public void Drop(string deviceId)
    {
        lock (_gate)
        {
            _devices.Remove(deviceId);
        }
    }

    public bool DropIfSession(string deviceId, Guid sessionId)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var current) || current.SessionId != sessionId)
            {
                return false;
            }

            _devices.Remove(deviceId);
            return true;
        }
    }

    public async Task<bool> SendJsonAsync(string deviceId, object payload, CancellationToken cancellationToken = default)
    {
        IDeviceConnection? connection;
        lock (_gate)
        {
            connection = _devices.TryGetValue(deviceId, out var device) ? device.Connection : null;
        }

        if (connection is null)
        {
            return false;
        }

        await connection.SendJsonAsync(payload, cancellationToken);
        return true;
    }

    public Device? Get(string deviceId)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(deviceId, out var device) ? device : null;
        }
    }

    public Device? Find(string hint)
    {
        var h = hint.Trim().ToLowerInvariant();
        lock (_gate)
        {
            foreach (var d in _devices.Values)
            {
                if (d.DeviceId.ToLowerInvariant() == h || d.Name.ToLowerInvariant() == h || d.Kind.ToLowerInvariant() == h)
                {
                    return d;
                }

                if (d.Name.ToLowerInvariant().Contains(h) || d.Kind.ToLowerInvariant().Contains(h))
                {
                    return d;
                }
            }

            if (Aliases.TryGetValue(h, out var kind))
            {
                foreach (var d in _devices.Values)
                {
                    if (d.Kind == kind)
                    {
                        return d;
                    }
                }
            }
        }

        return null;
    }

    public string ListText()
    {
        lock (_gate)
        {
            if (_devices.Count == 0)
            {
                return "Сейчас никто не подключён, кроме этого хаба.";
            }

            return string.Join("\n", _devices.Values.Select(d => $"- {d.Name} ({d.Kind}, id={d.DeviceId})"));
        }
    }
}
