using System.Globalization;
using System.Speech.Recognition;

namespace Makosh.Windows;

/// <summary>
/// Local SAPI: wake «Макош», then dictation of the next phrase. Does not call Agent.
/// </summary>
public sealed class WakeWordListener : IDisposable
{
    public static readonly string[] Names = ["макош", "макаш", "макошь", "makosh"];

    readonly object _sync = new();
    readonly SpeechRecognitionEngine? _engine;
    readonly CultureInfo _culture;
    bool _commandMode;
    bool _paused;
    bool _disposed;
    bool _busy;
    bool _loadDictation;
    bool _loadWake;
    System.Threading.Timer? _commandTimeout;

    public event Action? Woke;
    public event Action<string>? CommandReady;
    public event Action<string>? StatusChanged;

    public bool IsAvailable => _engine is not null;

    public WakeWordListener()
    {
        var info = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(item => item.Culture.TwoLetterISOLanguageName == "ru")
            ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();
        if (info is null)
        {
            _culture = CultureInfo.CurrentCulture;
            StatusChanged?.Invoke("нет Windows Speech Recognition");
            return;
        }

        _culture = info.Culture;
        try
        {
            _engine = new SpeechRecognitionEngine(info);
            _engine.SetInputToDefaultAudioDevice();
            _engine.BabbleTimeout = TimeSpan.FromSeconds(2);
            _engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(800);
            _engine.SpeechRecognized += OnRecognized;
            _engine.RecognizeCompleted += OnRecognizeCompleted;
            LoadWakeGrammars();
            StartListening();
            StatusChanged?.Invoke("жду «Макош»");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("микрофон/распознавание: " + ex.Message);
            _engine?.Dispose();
            _engine = null;
        }
    }

    public void ListenCommandNow() => RequestCommandMode();

    public void Pause()
    {
        lock (_sync)
        {
            _paused = true;
        }

        StopListening();
    }

    public void Resume()
    {
        lock (_sync)
        {
            _paused = false;
            _loadWake = !_commandMode;
            _loadDictation = _commandMode;
        }

        StopListening();
    }

    void RequestCommandMode()
    {
        lock (_sync)
        {
            if (_engine is null || _disposed)
            {
                return;
            }

            _commandMode = true;
            _loadDictation = true;
            ArmCommandTimeout();
        }

        StatusChanged?.Invoke("слушаю команду");
        Woke?.Invoke();
        StopListening();
    }

    void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (_paused || e.Result is null || e.Result.Confidence < 0.30f)
        {
            return;
        }

        var raw = (e.Result.Text ?? "").Trim();
        if (raw.Length == 0)
        {
            return;
        }

        var (woke, command) = Split(raw);
        string? emit = null;
        var wokeOnly = false;
        lock (_sync)
        {
            if (_paused)
            {
                return;
            }

            if (!_commandMode)
            {
                if (!woke)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(command))
                {
                    _commandMode = true;
                    _loadDictation = true;
                    ArmCommandTimeout();
                    wokeOnly = true;
                }
                else
                {
                    emit = command;
                    _loadWake = true;
                }
            }
            else
            {
                if (woke && string.IsNullOrWhiteSpace(command))
                {
                    ArmCommandTimeout();
                    return;
                }

                var text = string.IsNullOrWhiteSpace(command) ? raw : command;
                if (IsOnlyWake(text))
                {
                    ArmCommandTimeout();
                    return;
                }

                _commandMode = false;
                _commandTimeout?.Change(Timeout.Infinite, Timeout.Infinite);
                _loadWake = true;
                emit = text;
            }
        }

        if (wokeOnly)
        {
            StatusChanged?.Invoke("слушаю команду");
            Woke?.Invoke();
            StopListening();
            return;
        }

        if (emit is not null)
        {
            StatusChanged?.Invoke("обрабатываю");
            CommandReady?.Invoke(emit);
            StopListening();
        }
    }

    void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        _busy = false;
        if (_disposed || _engine is null)
        {
            return;
        }

        lock (_sync)
        {
            if (_paused)
            {
                return;
            }

            if (_commandMode && e.InitialSilenceTimeout)
            {
                _commandMode = false;
                _loadWake = true;
                StatusChanged?.Invoke("жду «Макош»");
            }

            if (_loadDictation)
            {
                LoadDictationGrammar();
                _loadDictation = false;
            }
            else if (_loadWake)
            {
                LoadWakeGrammars();
                _loadWake = false;
            }
        }

        StartListening();
    }

    void LoadWakeGrammars()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.UnloadAllGrammars();
        _engine.InitialSilenceTimeout = TimeSpan.FromSeconds(30);
        _engine.LoadGrammar(BuildWakeGrammar(_culture));
        try
        {
            _engine.LoadGrammar(BuildWakeCommandGrammar(_culture));
        }
        catch
        {
        }
    }

    void LoadDictationGrammar()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.UnloadAllGrammars();
        _engine.InitialSilenceTimeout = TimeSpan.FromSeconds(7);
        _engine.LoadGrammar(new DictationGrammar());
    }

    void StartListening()
    {
        if (_engine is null || _disposed || _paused || _busy)
        {
            return;
        }

        try
        {
            _busy = true;
            _engine.RecognizeAsync(RecognizeMode.Multiple);
        }
        catch
        {
            _busy = false;
        }
    }

    void StopListening()
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            _engine.RecognizeAsyncStop();
        }
        catch
        {
            _busy = false;
        }
    }

    void ArmCommandTimeout()
    {
        _commandTimeout ??= new System.Threading.Timer(_ =>
        {
            lock (_sync)
            {
                if (!_commandMode || _paused || _disposed)
                {
                    return;
                }

                _commandMode = false;
                _loadWake = true;
            }

            StatusChanged?.Invoke("жду «Макош»");
            StopListening();
        });
        _commandTimeout.Change(TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
    }

    public static (bool Woke, string Command) Split(string text)
    {
        var value = text.Trim();
        foreach (var name in Names.OrderByDescending(item => item.Length))
        {
            if (value.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return (true, "");
            }

            if (value.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                (value.Length == name.Length || !char.IsLetterOrDigit(value[name.Length])))
            {
                return (true, value[name.Length..].Trim(' ', ',', '.', '!', '?', ':', '—', '-'));
            }
        }

        return (false, value);
    }

    static bool IsOnlyWake(string text)
    {
        var (woke, command) = Split(text);
        return woke && string.IsNullOrWhiteSpace(command);
    }

    static Grammar BuildWakeGrammar(CultureInfo culture)
    {
        var builder = new GrammarBuilder { Culture = culture };
        builder.Append(new Choices(Names));
        return new Grammar(builder) { Name = "wake" };
    }

    static Grammar BuildWakeCommandGrammar(CultureInfo culture)
    {
        var builder = new GrammarBuilder { Culture = culture };
        builder.Append(new Choices(Names));
        builder.AppendDictation();
        return new Grammar(builder) { Name = "wake-cmd" };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _commandTimeout?.Dispose();
        if (_engine is not null)
        {
            try
            {
                _engine.RecognizeAsyncStop();
            }
            catch
            {
            }

            _engine.SpeechRecognized -= OnRecognized;
            _engine.RecognizeCompleted -= OnRecognizeCompleted;
            _engine.Dispose();
        }
    }
}
