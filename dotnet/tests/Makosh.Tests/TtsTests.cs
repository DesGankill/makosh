using Makosh.Core;

namespace Makosh.Tests;

public class TtsVoicePickerTests
{
    [Fact]
    public void Prefers_russian_female_over_english()
    {
        var voices = new List<TtsVoice>
        {
            new() { Id = "David", Name = "Microsoft David", Culture = "en-US", Gender = "Male" },
            new() { Id = "Irina", Name = "Microsoft Irina Desktop", Culture = "ru-RU", Gender = "Female" },
            new() { Id = "Pavel", Name = "Microsoft Pavel", Culture = "ru-RU", Gender = "Male" },
        };
        var pick = TtsVoicePicker.Select(voices, "");
        Assert.Equal("Irina", pick?.Id);
    }

    [Fact]
    public void Preferred_id_wins()
    {
        var voices = new List<TtsVoice>
        {
            new() { Id = "Irina", Name = "Microsoft Irina Desktop", Culture = "ru-RU", Gender = "Female" },
            new() { Id = "David", Name = "Microsoft David", Culture = "en-US", Gender = "Male" },
        };
        Assert.Equal("David", TtsVoicePicker.Select(voices, "David")?.Id);
    }

    [Fact]
    public void Missing_russian_falls_back_to_female_then_any()
    {
        var english = new List<TtsVoice>
        {
            new() { Id = "Zira", Name = "Microsoft Zira", Culture = "en-US", Gender = "Female" },
            new() { Id = "David", Name = "Microsoft David", Culture = "en-US", Gender = "Male" },
        };
        Assert.Equal("Zira", TtsVoicePicker.Select(english, "")?.Id);
        var onlyMale = new List<TtsVoice>
        {
            new() { Id = "David", Name = "Microsoft David", Culture = "en-US", Gender = "Male" },
        };
        Assert.Equal("David", TtsVoicePicker.Select(onlyMale, "")?.Id);
        Assert.Null(TtsVoicePicker.Select([], ""));
    }

    [Fact]
    public void Modern_name_beats_older_same_locale_gender()
    {
        var voices = new List<TtsVoice>
        {
            new() { Id = "old", Name = "Microsoft Irina Desktop", Culture = "ru-RU", Gender = "Female" },
            new() { Id = "new", Name = "Microsoft Irina Neural", Culture = "ru-RU", Gender = "Female" },
        };
        Assert.Equal("new", TtsVoicePicker.Select(voices, "")?.Id);
    }

    [Fact]
    public void Prefers_silero_kseniya_over_sapi_irina()
    {
        var voices = new List<TtsVoice>
        {
            new() { Id = "Microsoft Irina Desktop", Name = "Microsoft Irina Desktop", Culture = "ru-RU", Gender = "Female", Engine = "sapi" },
            SileroVoices.Kseniya,
            SileroVoices.Baya,
        };
        Assert.Equal("kseniya", TtsVoicePicker.Select(voices, "")?.Id);
        Assert.Equal("kseniya", TtsVoicePicker.Select(voices, "kseniya")?.Id);
    }
}

public class TtsSettingsClampTests
{
    [Fact]
    public void Clamp_helpers_limit_ranges()
    {
        Assert.Equal(-10, TtsLimits.ClampRate(-99));
        Assert.Equal(10, TtsLimits.ClampRate(99));
        Assert.Equal(0, TtsLimits.ClampVolume(-1));
        Assert.Equal(100, TtsLimits.ClampVolume(1000));
        Assert.Equal(-10, TtsLimits.ClampPitch(-11));
        Assert.Equal(10, TtsLimits.ClampPitch(11));
    }

    [Fact]
    public void Environment_values_are_clamped_on_load()
    {
        var previousRate = Environment.GetEnvironmentVariable("MAKOSH_TTS_RATE");
        var previousVol = Environment.GetEnvironmentVariable("MAKOSH_TTS_VOLUME");
        var previousPitch = Environment.GetEnvironmentVariable("MAKOSH_TTS_PITCH");
        var previousEnabled = Environment.GetEnvironmentVariable("MAKOSH_TTS_ENABLED");
        try
        {
            Environment.SetEnvironmentVariable("MAKOSH_TTS_RATE", "99");
            Environment.SetEnvironmentVariable("MAKOSH_TTS_VOLUME", "-5");
            Environment.SetEnvironmentVariable("MAKOSH_TTS_PITCH", "-40");
            Environment.SetEnvironmentVariable("MAKOSH_TTS_ENABLED", "false");
            var settings = MakoshSettings.Load([]);
            Assert.Equal(10, settings.TtsRate);
            Assert.Equal(0, settings.TtsVolume);
            Assert.Equal(-10, settings.TtsPitch);
            Assert.False(settings.TtsEnabled);
            Assert.Equal("silero", MakoshSettings.Load([]).TtsEngine);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAKOSH_TTS_RATE", previousRate);
            Environment.SetEnvironmentVariable("MAKOSH_TTS_VOLUME", previousVol);
            Environment.SetEnvironmentVariable("MAKOSH_TTS_PITCH", previousPitch);
            Environment.SetEnvironmentVariable("MAKOSH_TTS_ENABLED", previousEnabled);
        }
    }

    [Fact]
    public void Engine_sapi_is_normalized()
    {
        var previous = Environment.GetEnvironmentVariable("MAKOSH_TTS_ENGINE");
        try
        {
            Environment.SetEnvironmentVariable("MAKOSH_TTS_ENGINE", "SAPI");
            Assert.Equal("sapi", MakoshSettings.Load([]).TtsEngine);
            Environment.SetEnvironmentVariable("MAKOSH_TTS_ENGINE", "nope");
            Assert.Equal("silero", MakoshSettings.Load([]).TtsEngine);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAKOSH_TTS_ENGINE", previous);
        }
    }
}

public class TtsPlaybackTests
{
    [Fact]
    public async Task Newer_phrase_cancels_the_previous_one()
    {
        var fake = new FakeTtsService { Delay = TimeSpan.FromMilliseconds(250) };
        var runtime = new TtsRuntime { Enabled = true };
        var playback = new TtsPlayback(fake, runtime);
        playback.SpeakIfRequested(true, "первая");
        await Task.Delay(40);
        playback.SpeakIfRequested(true, "вторая");
        await Task.Delay(400);
        Assert.Contains("первая", fake.Cancelled);
        Assert.Contains("вторая", fake.Spoken);
        Assert.DoesNotContain("первая", fake.Spoken);
    }

    [Fact]
    public async Task Disabled_or_speak_false_does_not_call_engine()
    {
        var fake = new FakeTtsService();
        var runtime = new TtsRuntime { Enabled = false };
        var playback = new TtsPlayback(fake, runtime);
        playback.SpeakIfRequested(true, "тихо");
        runtime.Enabled = true;
        playback.SpeakIfRequested(false, "тоже тихо");
        await Task.Delay(50);
        Assert.Empty(fake.Spoken);
    }
}

public class CompositeTtsTests
{
    static (FakeTtsService silero, FakeTtsService sapi, CompositeTtsService composite, TtsRuntime runtime) Create()
    {
        var silero = new FakeTtsService { EngineId = "silero" };
        silero.Voices.Clear();
        silero.Voices.Add(SileroVoices.Kseniya);
        silero.Voices.Add(SileroVoices.Baya);
        silero.Voices.Add(SileroVoices.Xenia);
        var sapi = new FakeTtsService { EngineId = "sapi" };
        var runtime = new TtsRuntime { Enabled = true, Engine = "silero", Voice = "kseniya", Rate = 2, Volume = 80 };
        return (silero, sapi, new CompositeTtsService(silero, sapi, runtime), runtime);
    }

    [Fact]
    public async Task Silero_engine_uses_silero_voice()
    {
        var (silero, sapi, composite, runtime) = Create();
        await composite.SpeakAsync("привет", runtime.Snapshot());
        Assert.Equal(["привет"], silero.Spoken);
        Assert.Empty(sapi.Spoken);
        Assert.Equal("kseniya", silero.Utterances[0].VoiceId);
        Assert.Equal(2, silero.Utterances[0].Rate);
        Assert.Equal(80, silero.Utterances[0].Volume);
        Assert.Contains(composite.GetVoices(), voice => voice.Engine == "silero" && voice.Id == "xenia");
        Assert.Contains(composite.GetVoices(), voice => voice.Engine == "sapi");
    }

    [Fact]
    public async Task Switching_to_sapi_uses_windows_backend()
    {
        var (silero, sapi, composite, runtime) = Create();
        runtime.Engine = "sapi";
        runtime.Voice = "Microsoft Irina Desktop";
        await composite.SpeakAsync("sapi", runtime.Snapshot());
        Assert.Empty(silero.Spoken);
        Assert.Equal(["sapi"], sapi.Spoken);
    }

    [Fact]
    public async Task Unavailable_silero_falls_back_to_sapi()
    {
        var (silero, sapi, composite, runtime) = Create();
        silero.IsAvailable = false;
        await composite.SpeakAsync("fallback", runtime.Snapshot());
        Assert.Empty(silero.Spoken);
        Assert.Equal(["fallback"], sapi.Spoken);
    }

    [Fact]
    public void PickSpeaker_falls_back_to_required_female()
    {
        Assert.Equal("kseniya", SileroVoices.PickSpeaker(["aidar", "kseniya", "baya"], "missing"));
        Assert.Equal("baya", SileroVoices.PickSpeaker(["aidar", "baya"], "nope"));
        Assert.Equal("aidar", SileroVoices.PickSpeaker(["aidar"], ""));
    }

    [Fact]
    public async Task Silero_synth_failure_does_not_switch_to_sapi()
    {
        var (silero, sapi, composite, runtime) = Create();
        silero.SpeakError = new InvalidOperationException("KeyError: l");
        await composite.SpeakAsync("look_screen", runtime.Snapshot());
        Assert.Empty(silero.Spoken);
        Assert.Empty(sapi.Spoken);
    }
}
