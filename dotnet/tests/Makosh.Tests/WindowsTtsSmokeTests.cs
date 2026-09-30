using Makosh.Windows;

namespace Makosh.Tests;

public class WindowsTtsSmokeTests
{
    [Fact]
    [Trait("Category", "WindowsSmoke")]
    public void Lists_installed_voices_when_requested()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAKOSH_TTS_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using var tts = new WindowsTtsService();
        var voices = tts.GetVoices();
        var chosen = Makosh.Core.TtsVoicePicker.Select(voices, "");
        Console.WriteLine("TTS voices:");
        foreach (var group in voices.GroupBy(voice => voice.Culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "Russian" : "Other"))
        {
            Console.WriteLine(group.Key + ":");
            foreach (var voice in group)
            {
                Console.WriteLine($"  {voice.Name} [{voice.Culture}, {voice.Gender}]");
            }
        }

        Console.WriteLine("Selected: " + (chosen is null ? "(none)" : $"{chosen.Name} [{chosen.Culture}, {chosen.Gender}]"));
        Assert.True(tts.SupportsPitch);
    }
}
