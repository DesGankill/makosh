using Makosh.Core;

namespace Makosh.Tests;

public sealed class FakeTtsService : ITtsService
{
    public List<TtsVoice> Voices { get; } =
    [
        new() { Id = "Microsoft Irina Desktop", Name = "Microsoft Irina Desktop", Culture = "ru-RU", Gender = "Female", Engine = "sapi" },
        new() { Id = "Microsoft David Desktop", Name = "Microsoft David Desktop", Culture = "en-US", Gender = "Male", Engine = "sapi" },
    ];

    public List<string> Spoken { get; } = [];
    public List<string> Cancelled { get; } = [];
    public List<TtsUtterance> Utterances { get; } = [];
    public TimeSpan Delay { get; set; }
    public bool SupportsPitch { get; set; } = true;
    public string EngineId { get; set; } = "fake";
    public bool IsAvailable { get; set; } = true;

    public IReadOnlyList<TtsVoice> GetVoices() => Voices;

    public Exception? SpeakError { get; set; }

    public async Task SpeakAsync(string text, TtsUtterance utterance, CancellationToken cancellationToken = default)
    {
        if (SpeakError is not null)
        {
            throw SpeakError;
        }
        if (!IsAvailable)
        {
            throw new InvalidOperationException("unavailable");
        }

        try
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Spoken.Add(text);
            Utterances.Add(utterance);
        }
        catch (OperationCanceledException)
        {
            Cancelled.Add(text);
            throw;
        }
    }

    public void Reset()
    {
        Spoken.Clear();
        Cancelled.Clear();
        Utterances.Clear();
    }
}
