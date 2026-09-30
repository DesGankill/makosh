namespace Makosh.Core;

public sealed class CompositeTtsService : ITtsService, IDisposable
{
    readonly ITtsService _silero;
    readonly ITtsService _sapi;
    readonly TtsRuntime _runtime;
    readonly Action<string>? _warn;
    int _fallbackLogged;

    public CompositeTtsService(ITtsService silero, ITtsService sapi, TtsRuntime runtime, Action<string>? warn = null)
    {
        _silero = silero;
        _sapi = sapi;
        _runtime = runtime;
        _warn = warn;
    }

    public string EngineId => TtsEngines.Normalize(_runtime.Engine);

    public bool IsAvailable => Preferred().IsAvailable || _sapi.IsAvailable;

    public bool SupportsPitch => Preferred().SupportsPitch;

    public IReadOnlyList<TtsVoice> GetVoices()
    {
        var voices = new List<TtsVoice>();
        voices.AddRange(_silero.GetVoices());
        voices.AddRange(_sapi.GetVoices());
        return voices;
    }

    public async Task SpeakAsync(string text, TtsUtterance utterance, CancellationToken cancellationToken = default)
    {
        var useSilero = ShouldUseSilero(utterance);
        if (useSilero && _silero.IsAvailable)
        {
            try
            {
                await _silero.SpeakAsync(text, utterance, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogFallback(
                    "Silero failed; keeping Silero as the selected engine (no SAPI switch):" + Environment.NewLine +
                    "type=" + (ex.GetType().FullName ?? ex.GetType().Name) + Environment.NewLine +
                    "message=" + ex.Message + Environment.NewLine +
                    "inner=" + (ex.InnerException?.Message ?? "") + Environment.NewLine +
                    ex.ToString());
                return;
            }
        }
        else if (useSilero && !_silero.IsAvailable)
        {
            LogFallback("Silero is not ready, falling back to SAPI until the worker is up");
        }

        await _sapi.SpeakAsync(text, utterance, cancellationToken);
    }

    public bool ShouldUseSilero(TtsUtterance utterance)
    {
        if (TtsEngines.IsSapi(_runtime.Engine))
        {
            return false;
        }

        var id = utterance.VoiceId ?? "";
        if (id.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    ITtsService Preferred() => ShouldUseSilero(_runtime.Snapshot()) && _silero.IsAvailable ? _silero : _sapi;

    void LogFallback(string message)
    {
        if (Interlocked.Exchange(ref _fallbackLogged, 1) == 1)
        {
            return;
        }

        _warn?.Invoke(message);
    }

    public void Dispose()
    {
        (_silero as IDisposable)?.Dispose();
        (_sapi as IDisposable)?.Dispose();
    }
}
