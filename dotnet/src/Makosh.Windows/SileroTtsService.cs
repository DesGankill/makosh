using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Makosh.Core;
using NAudio.Wave;

namespace Makosh.Windows;

public sealed class SileroTtsService : ITtsService, IDisposable
{
    public const string ModelUrl = "https://models.silero.ai/models/tts/ru/v5_5_ru.pt";
    public const long ModelBytes = 145_420_684;
    public const string ModelFileName = "v5_5_ru.pt";

    readonly MakoshSettings _settings;
    readonly Action<string>? _log;
    readonly string _root;
    readonly object _gate = new();
    readonly CancellationTokenSource _lifetime = new();
    List<TtsVoice> _voices = SileroVoices.RequiredFemale.ToList();
    Process? _worker;
    Stream? _stdin;
    Stream? _stdout;
    bool _ready;
    bool _disposed;

    public SileroTtsService(MakoshSettings settings, Action<string>? log = null)
    {
        _settings = settings;
        _log = log;
        _root = Path.Combine(settings.DataDirectory, "tts", "silero");
        if (!IsTesting)
        {
            _ = Task.Run(() => InitAsync(_lifetime.Token));
        }
    }

    public string EngineId => TtsEngines.Silero;
    public bool SupportsPitch => true;
    public bool IsAvailable => _ready && !_disposed;

    public IReadOnlyList<TtsVoice> GetVoices() => _voices;

    public async Task SpeakAsync(string text, TtsUtterance utterance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable)
        {
            throw new InvalidOperationException("Silero is not available");
        }

        var speaker = SileroVoices.PickSpeaker(
            _voices.Select(voice => voice.Id).ToList(),
            utterance.VoiceId);
        var spoken = SileroText.Sanitize(text);
        if (string.IsNullOrWhiteSpace(spoken))
        {
            return;
        }

        (short[] Samples, int SampleRate) pcm;
        try
        {
            pcm = await SynthesizeAsync(spoken, speaker, utterance, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var tight = System.Text.RegularExpressions.Regex.Replace(spoken, @"[^\u0400-\u04FF0-9\s.,!?…:-]", " ");
            tight = System.Text.RegularExpressions.Regex.Replace(tight, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(tight))
            {
                throw new InvalidOperationException("Silero не может озвучить этот текст", ex);
            }

            pcm = await SynthesizeAsync(tight, speaker, utterance, cancellationToken);
        }
        if (pcm.Samples.Length == 0)
        {
            return;
        }

        ApplyVolume(pcm.Samples, utterance.Volume);
        await PlayAsync(pcm.Samples, pcm.SampleRate, cancellationToken);
    }

    async Task InitAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var modelPath = Path.Combine(_root, ModelFileName);
            await EnsureModelAsync(modelPath, cancellationToken);
            var python = FindPython();
            if (python is null)
            {
                _log?.Invoke("Silero unavailable: Python not found, using SAPI");
                return;
            }

            var pydeps = Path.Combine(_root, "pydeps");
            if (!await EnsureTorchAsync(python, pydeps, cancellationToken))
            {
                _log?.Invoke("Silero unavailable: torch install failed, using SAPI");
                return;
            }

            StartWorker(python, pydeps, modelPath);
            _log?.Invoke("Silero v5.5 RU ready on " + _settings.TtsDevice);
        }
        catch (Exception ex)
        {
            _log?.Invoke("Silero unavailable: " + ex.GetType().Name + ", using SAPI");
        }
    }

    async Task EnsureModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        if (File.Exists(modelPath) && new FileInfo(modelPath).Length == ModelBytes)
        {
            return;
        }

        _log?.Invoke("Downloading Silero v5.5 RU (" + (ModelBytes / (1024 * 1024)) + " MB) once...");
        var part = modelPath + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(part))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (File.Exists(modelPath))
        {
            File.Delete(modelPath);
        }

        File.Move(part, modelPath);
    }

    async Task<bool> EnsureTorchAsync(string python, string pydeps, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(pydeps, "torch", "__init__.py")))
        {
            return true;
        }

        Directory.CreateDirectory(pydeps);
        _log?.Invoke("Installing CPU torch into local Silero cache (once)...");
        var psi = new ProcessStartInfo
        {
            FileName = python,
            ArgumentList =
            {
                "-m", "pip", "install", "--disable-pip-version-check", "-q",
                "--target", pydeps,
                "torch", "numpy",
                "--index-url", "https://download.pytorch.org/whl/cpu",
            },
            WorkingDirectory = _root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 && File.Exists(Path.Combine(pydeps, "torch", "__init__.py"));
    }

    void StartWorker(string python, string pydeps, string modelPath)
    {
        var script = FindWorkerScript();
        if (script is null)
        {
            throw new FileNotFoundException("silero_worker.py");
        }

        var psi = new ProcessStartInfo
        {
            FileName = python,
            ArgumentList = { "-u", script },
            WorkingDirectory = _root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = SileroWorkerProtocol.Utf8,
            StandardInputEncoding = SileroWorkerProtocol.Utf8,
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONPATH"] = pydeps;
        psi.Environment["MAKOSH_SILERO_MODEL"] = modelPath;
        psi.Environment["MAKOSH_TTS_DEVICE"] = string.Equals(_settings.TtsDevice, "cuda", StringComparison.OrdinalIgnoreCase)
            ? "cuda"
            : "cpu";

        var process = Process.Start(psi) ?? throw new InvalidOperationException("silero worker");
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _log?.Invoke(args.Data);
            }
        };
        process.BeginErrorReadLine();
        _worker = process;
        _stdin = process.StandardInput.BaseStream;
        _stdout = process.StandardOutput.BaseStream;
        var hello = ReadJsonLine(_stdout, CancellationToken.None) ?? throw new InvalidOperationException("Silero worker failed to start");
        if (!hello.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            throw new InvalidOperationException("Silero worker failed to start");
        }

        if (hello.TryGetProperty("speakers", out var speakersEl) && speakersEl.ValueKind == JsonValueKind.Array)
        {
            var listed = new List<TtsVoice>();
            foreach (var item in speakersEl.EnumerateArray())
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    listed.Add(SileroVoices.Describe(name));
                }
            }

            foreach (var required in SileroVoices.RequiredFemale)
            {
                if (!listed.Any(voice => string.Equals(voice.Id, required.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    listed.Insert(0, required);
                }
            }

            if (listed.Count > 0)
            {
                _voices = listed;
            }
        }

        var device = hello.TryGetProperty("device", out var deviceEl) ? deviceEl.GetString() : "cpu";
        _log?.Invoke("Silero worker device=" + device + " voices=" + _voices.Count);
        _ready = true;
    }

    async Task<(short[] Samples, int SampleRate)> SynthesizeAsync(
        string text,
        string speaker,
        TtsUtterance utterance,
        CancellationToken cancellationToken)
    {
        byte[] buffer;
        int sampleRate;
        lock (_gate)
        {
            if (_stdin is null || _stdout is null)
            {
                throw new InvalidOperationException("Silero worker is not running");
            }

            SileroWorkerProtocol.WriteLine(_stdin, new
            {
                op = "synth",
                speaker,
                text,
                rate = TtsLimits.ClampRate(utterance.Rate),
                pitch = TtsLimits.ClampPitch(utterance.Pitch),
                sample_rate = 48000,
            });
            var header = ReadJsonLine(_stdout, cancellationToken) ?? throw new InvalidOperationException("Silero synth failed: empty");
            if (!header.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = header.TryGetProperty("error", out var err) ? err.GetString() : "synth";
                throw new InvalidOperationException("Silero synth failed: " + error);
            }

            var bytes = header.GetProperty("bytes").GetInt32();
            sampleRate = header.TryGetProperty("sr", out var sr) ? sr.GetInt32() : 48000;
            buffer = new byte[bytes];
            var read = 0;
            while (read < bytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = _stdout.Read(buffer, read, bytes - read);
                if (n <= 0)
                {
                    throw new EndOfStreamException("Silero PCM");
                }

                read += n;
            }
        }

        var samples = new short[buffer.Length / 2];
        Buffer.BlockCopy(buffer, 0, samples, 0, buffer.Length);
        await Task.CompletedTask;
        return (samples, sampleRate);
    }

    static async Task PlayAsync(short[] samples, int sampleRate, CancellationToken cancellationToken)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        using var raw = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(sampleRate, 16, 1));
        using var output = new WaveOutEvent();
        output.Init(raw);
        output.Play();
        try
        {
            while (output.PlaybackState == PlaybackState.Playing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(40, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            output.Stop();
            throw;
        }
    }

    static void ApplyVolume(short[] samples, int volume)
    {
        var scaled = TtsLimits.ClampVolume(volume) / 100f;
        if (Math.Abs(scaled - 1f) < 0.01f)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var value = (int)(samples[i] * scaled);
            samples[i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }
    }

    static JsonElement? ReadJsonLine(Stream stream, CancellationToken cancellationToken)
    {
        var line = new MemoryStream();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var b = stream.ReadByte();
            if (b < 0)
            {
                return null;
            }

            if (b == '\n')
            {
                break;
            }

            if (b != '\r')
            {
                line.WriteByte((byte)b);
            }
        }

        if (line.Length == 0)
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonElement>(line.ToArray());
    }

    static string? FindPython()
    {
        var configured = Environment.GetEnvironmentVariable("MAKOSH_TTS_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        foreach (var name in new[] { "python", "python3" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = name,
                    ArgumentList = { "-c", "import sys; print(sys.executable)" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process is null)
                {
                    continue;
                }

                var path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                if (process.ExitCode == 0 && File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // try next
            }
        }

        return null;
    }

    static string? FindWorkerScript()
    {
        var bases = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(SileroTtsService).Assembly.Location) ?? "",
        };
        foreach (var root in bases)
        {
            var candidate = Path.Combine(root, "silero", "silero_worker.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    static bool IsTesting =>
        string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Testing", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _disposed = true;
        _ready = false;
        _lifetime.Cancel();
        try
        {
            if (_stdin is not null)
            {
                SileroWorkerProtocol.WriteLine(_stdin, new { op = "quit" });
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (_worker is { HasExited: false })
            {
                _worker.Kill(true);
            }
        }
        catch (Exception)
        {
        }

        _stdin?.Dispose();
        _worker?.Dispose();
        _lifetime.Dispose();
    }
}
