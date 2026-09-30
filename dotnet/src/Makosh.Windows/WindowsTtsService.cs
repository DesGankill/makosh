using System.Globalization;
using System.Security;
using Makosh.Core;
using Sapi = System.Speech.Synthesis;

namespace Makosh.Windows;

public sealed class WindowsTtsService : ITtsService, IDisposable
{
    readonly Sapi.SpeechSynthesizer _synth = new();
    readonly object _gate = new();

    public bool SupportsPitch => true;
    public string EngineId => TtsEngines.Sapi;
    public bool IsAvailable => true;

    public IReadOnlyList<TtsVoice> GetVoices()
    {
        lock (_gate)
        {
            return _synth.GetInstalledVoices()
                .Where(static voice => voice.Enabled)
                .Select(static voice =>
                {
                    var info = voice.VoiceInfo;
                    return new TtsVoice
                    {
                        Id = info.Name,
                        Name = info.Name,
                        Culture = info.Culture?.Name ?? "",
                        Gender = info.Gender.ToString(),
                        Engine = TtsEngines.Sapi,
                    };
                })
                .ToList();
        }
    }

    public Task SpeakAsync(string text, TtsUtterance utterance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(object? sender, Sapi.SpeakCompletedEventArgs args)
        {
            _synth.SpeakCompleted -= OnCompleted;
            if (args.Cancelled || cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            else if (args.Error is not null)
            {
                tcs.TrySetException(args.Error);
            }
            else
            {
                tcs.TrySetResult();
            }
        }

        CancellationTokenRegistration registration = default;
        lock (_gate)
        {
            _synth.SpeakAsyncCancelAll();
            _synth.Rate = TtsLimits.ClampRate(utterance.Rate);
            _synth.Volume = TtsLimits.ClampVolume(utterance.Volume);
            TrySelect(utterance.VoiceId);
            _synth.SpeakCompleted += OnCompleted;
            registration = cancellationToken.Register(() =>
            {
                lock (_gate)
                {
                    _synth.SpeakAsyncCancelAll();
                }
            });

            if (utterance.Pitch != 0)
            {
                _synth.SpeakSsmlAsync(BuildSsml(text, utterance));
            }
            else
            {
                _synth.SpeakAsync(text);
            }
        }

        return AwaitSpeak(tcs.Task, registration);
    }

    static async Task AwaitSpeak(Task speak, CancellationTokenRegistration registration)
    {
        using (registration)
        {
            await speak.ConfigureAwait(false);
        }
    }

    void TrySelect(string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            try
            {
                _synth.SelectVoiceByHints(Sapi.VoiceGender.Female, Sapi.VoiceAge.NotSet, 0, new CultureInfo("ru-RU"));
            }
            catch (ArgumentException)
            {
                // Keep synthesizer default.
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        try
        {
            _synth.SelectVoice(voiceId);
        }
        catch (ArgumentException)
        {
            TrySelect(null);
        }
        catch (InvalidOperationException)
        {
            TrySelect(null);
        }
    }

    static string BuildSsml(string text, TtsUtterance utterance)
    {
        var xml = SecurityElement.Escape(text) ?? "";
        var voice = SecurityElement.Escape(utterance.VoiceId ?? "") ?? "";
        var percent = TtsLimits.ClampPitch(utterance.Pitch) * 5;
        var pitch = percent == 0 ? "default" : $"{percent:+0;-0}%";
        var voiceTag = string.IsNullOrEmpty(voice) ? "" : $"<voice name=\"{voice}\">";
        var voiceEnd = string.IsNullOrEmpty(voice) ? "" : "</voice>";
        return
            $"""
            <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="ru-RU">
              {voiceTag}<prosody pitch="{pitch}">{xml}</prosody>{voiceEnd}
            </speak>
            """;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _synth.SpeakAsyncCancelAll();
            _synth.Dispose();
        }
    }
}
