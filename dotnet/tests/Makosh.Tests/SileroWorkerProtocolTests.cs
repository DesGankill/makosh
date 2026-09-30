using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Makosh.Windows;

namespace Makosh.Tests;

public class SileroWorkerProtocolTests
{
    [Fact]
    public void Synth_command_is_one_utf8_json_line()
    {
        var bytes = SileroWorkerProtocol.EncodeLine(new
        {
            op = "synth",
            speaker = "kseniya",
            text = "Привет, я Макош.",
            rate = 0,
            pitch = 0,
            sample_rate = 48000,
        });

        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains((byte)0xD0, bytes);

        var parsed = SileroWorkerProtocol.ParseLine(bytes);
        Assert.Equal("synth", parsed.GetProperty("op").GetString());
        Assert.Equal("kseniya", parsed.GetProperty("speaker").GetString());
        Assert.Equal("Привет, я Макош.", parsed.GetProperty("text").GetString());
        Assert.Equal(48000, parsed.GetProperty("sample_rate").GetInt32());
    }

    [Fact]
    public void Python_json_loads_accepts_encoded_synth_line()
    {
        string? python = null;
        try
        {
            var probe = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                ArgumentList = { "-c", "import sys; print(sys.executable)" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (probe is not null)
            {
                python = probe.StandardOutput.ReadToEnd().Trim();
                probe.WaitForExit(5000);
                if (probe.ExitCode != 0)
                {
                    python = null;
                }
            }
        }
        catch (Exception)
        {
            python = null;
        }

        if (string.IsNullOrWhiteSpace(python) || !File.Exists(python))
        {
            return;
        }

        var bytes = SileroWorkerProtocol.EncodeLine(new
        {
            op = "synth",
            speaker = "kseniya",
            text = "Привет, я Макош.",
            rate = 0,
            pitch = 0,
            sample_rate = 48000,
        });

        var psi = new ProcessStartInfo
        {
            FileName = python,
            ArgumentList =
            {
                "-c",
                "import json,sys; msg=json.loads(sys.stdin.readline()); sys.stdout.write(json.dumps({'ok': True, 'op': msg['op'], 'speaker': msg['speaker'], 'text': msg['text']}, ensure_ascii=False)+'\\n')",
            },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = SileroWorkerProtocol.Utf8,
            StandardOutputEncoding = SileroWorkerProtocol.Utf8,
        };
        psi.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("python");
        process.StandardInput.BaseStream.Write(bytes);
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.Close();
        var reply = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        Assert.Equal(0, process.ExitCode);
        using var doc = JsonDocument.Parse(reply.Trim());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("synth", doc.RootElement.GetProperty("op").GetString());
        Assert.Equal("kseniya", doc.RootElement.GetProperty("speaker").GetString());
        Assert.Equal("Привет, я Макош.", doc.RootElement.GetProperty("text").GetString());
    }
}
