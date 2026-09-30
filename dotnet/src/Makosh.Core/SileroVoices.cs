namespace Makosh.Core;

public static class SileroVoices
{
    public static readonly TtsVoice Baya = Female("baya", "Silero Baya");
    public static readonly TtsVoice Kseniya = Female("kseniya", "Silero Kseniya");
    public static readonly TtsVoice Xenia = Female("xenia", "Silero Xenia");

    public static IReadOnlyList<TtsVoice> RequiredFemale { get; } = [Baya, Kseniya, Xenia];

    public static TtsVoice Female(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Culture = "ru-RU",
        Gender = "Female",
        Engine = TtsEngines.Silero,
    };

    public static TtsVoice Describe(string speaker)
    {
        var id = speaker.Trim();
        var female = ContainsAny(id, "baya", "kseniya", "xenia", "natalya", "dasha", "irina", "elena", "katya", "taya");
        return new TtsVoice
        {
            Id = id,
            Name = "Silero " + Capitalize(id),
            Culture = "ru-RU",
            Gender = female ? "Female" : GuessMale(id) ? "Male" : "Female",
            Engine = TtsEngines.Silero,
        };
    }

    public static string PickSpeaker(IReadOnlyList<string> speakers, string? preferred)
    {
        if (speakers.Count == 0)
        {
            return "kseniya";
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = speakers.FirstOrDefault(name =>
                string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        foreach (var name in new[] { "kseniya", "baya", "xenia" })
        {
            var match = speakers.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return speakers[0];
    }

    static bool GuessMale(string id) => ContainsAny(id, "aidar", "eugene", "ruslan", "dmitri", "pavel");

    static bool ContainsAny(string name, params string[] parts) =>
        parts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
