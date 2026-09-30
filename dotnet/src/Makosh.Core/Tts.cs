namespace Makosh.Core;

public sealed class TtsVoice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Culture { get; init; } = "";
    public string? Gender { get; init; }
    public string Engine { get; init; } = "";
}

public sealed class TtsUtterance
{
    public string? VoiceId { get; init; }
    public int Rate { get; init; }
    public int Volume { get; init; } = 100;
    public int Pitch { get; init; }
}

public interface ITtsService
{
    IReadOnlyList<TtsVoice> GetVoices();
    Task SpeakAsync(string text, TtsUtterance utterance, CancellationToken cancellationToken = default);
    bool SupportsPitch { get; }
    string EngineId { get; }
    bool IsAvailable { get; }
}

public static class TtsEngines
{
    public const string Silero = "silero";
    public const string Sapi = "sapi";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Sapi, StringComparison.OrdinalIgnoreCase) ? Sapi : Silero;

    public static bool IsSapi(string? value) =>
        string.Equals(Normalize(value), Sapi, StringComparison.OrdinalIgnoreCase);
}

public static class TtsLimits
{
    public const int MinRate = -10;
    public const int MaxRate = 10;
    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    public const int MinPitch = -10;
    public const int MaxPitch = 10;

    public static int ClampRate(int value) => Math.Clamp(value, MinRate, MaxRate);
    public static int ClampVolume(int value) => Math.Clamp(value, MinVolume, MaxVolume);
    public static int ClampPitch(int value) => Math.Clamp(value, MinPitch, MaxPitch);
}

public sealed class TtsRuntime
{
    public bool Enabled { get; set; } = true;
    public string Engine { get; set; } = TtsEngines.Silero;
    public string Voice { get; set; } = "";
    public int Rate { get; set; }
    public int Volume { get; set; } = 100;
    public int Pitch { get; set; }

    public static TtsRuntime FromSettings(MakoshSettings settings) => new()
    {
        Enabled = settings.TtsEnabled,
        Engine = TtsEngines.Normalize(settings.TtsEngine),
        Voice = settings.TtsVoice,
        Rate = TtsLimits.ClampRate(settings.TtsRate),
        Volume = TtsLimits.ClampVolume(settings.TtsVolume),
        Pitch = TtsLimits.ClampPitch(settings.TtsPitch),
    };

    public TtsUtterance Snapshot() => new()
    {
        VoiceId = string.IsNullOrWhiteSpace(Voice) ? null : Voice,
        Rate = TtsLimits.ClampRate(Rate),
        Volume = TtsLimits.ClampVolume(Volume),
        Pitch = TtsLimits.ClampPitch(Pitch),
    };
}

public static class TtsVoicePicker
{
    public static TtsVoice? Select(IReadOnlyList<TtsVoice> voices, string? preferred)
    {
        if (voices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = voices.FirstOrDefault(voice =>
                string.Equals(voice.Id, preferred, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voice.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return voices.OrderByDescending(Score).ThenBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase).First();
    }

    public static int Score(TtsVoice voice)
    {
        var culture = voice.Culture ?? "";
        var ru = culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        var female = string.Equals(voice.Gender, "Female", StringComparison.OrdinalIgnoreCase);
        var name = voice.Name ?? "";
        var score = 0;
        if (ru && female)
        {
            score += 1000;
        }
        else if (ru)
        {
            score += 400;
        }
        else if (female)
        {
            score += 100;
        }

        if (ContainsAny(name, "Neural", "Natural", "OneCore", "Online", "Premium"))
        {
            score += 50;
        }

        if (string.Equals(voice.Engine, TtsEngines.Silero, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (ContainsAny(name, "kseniya"))
        {
            score += 20;
        }

        if (ContainsAny(name, "baya", "xenia"))
        {
            score += 10;
        }

        if (ContainsAny(name, "Irina", "Irene", "Elena", "Katya", "Ekaterina", "Dariya", "Daria"))
        {
            score += 15;
        }

        return score;
    }

    static bool ContainsAny(string name, params string[] parts) =>
        parts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
}

public sealed class TtsPlayback
{
    readonly ITtsService _tts;
    readonly TtsRuntime _runtime;
    readonly object _gate = new();
    CancellationTokenSource? _current;

    public TtsPlayback(ITtsService tts, TtsRuntime runtime)
    {
        _tts = tts;
        _runtime = runtime;
    }

    public event Action? SpeakingStarted;
    public event Action? SpeakingEnded;

    public void SpeakIfRequested(bool speak, string text)
    {
        if (!speak || !_runtime.Enabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var utterance = _runtime.Snapshot();
        CancellationToken token;
        lock (_gate)
        {
            _current?.Cancel();
            _current?.Dispose();
            _current = new CancellationTokenSource();
            token = _current.Token;
        }

        _ = SpeakInBackground(text, utterance, token);
    }

    async Task SpeakInBackground(string text, TtsUtterance utterance, CancellationToken cancellationToken)
    {
        try
        {
            SpeakingStarted?.Invoke();
            await _tts.SpeakAsync(text, utterance, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer phrase.
        }
        catch (Exception)
        {
            // TTS must not break chat/WebSocket.
        }
        finally
        {
            SpeakingEnded?.Invoke();
        }
    }
}
