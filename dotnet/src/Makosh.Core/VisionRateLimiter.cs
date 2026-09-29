namespace Makosh.Core;

public sealed class VisionRateLimiter
{
    readonly int _maxPerHour;
    readonly Func<double> _unixSeconds;
    readonly object _gate = new();
    readonly Queue<double> _times = new();

    public VisionRateLimiter(int maxPerHour, Func<double>? unixSeconds = null)
    {
        _maxPerHour = Math.Max(0, maxPerHour);
        _unixSeconds = unixSeconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
    }

    public int CallsLeft()
    {
        lock (_gate)
        {
            Trim();
            return Math.Max(0, _maxPerHour - _times.Count);
        }
    }

    public bool TryConsume()
    {
        lock (_gate)
        {
            Trim();
            if (_times.Count >= _maxPerHour)
            {
                return false;
            }

            _times.Enqueue(_unixSeconds());
            return true;
        }
    }

    void Trim()
    {
        var now = _unixSeconds();
        while (_times.Count > 0 && now - _times.Peek() > 3600)
        {
            _times.Dequeue();
        }
    }
}
