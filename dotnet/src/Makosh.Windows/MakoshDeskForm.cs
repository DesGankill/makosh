using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Makosh.Core;

namespace Makosh.Windows;

public sealed class MakoshDeskForm : Form
{
    readonly MakoshSettings _settings;
    readonly Action? _onClosed;
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly TextBox _input = new() { Dock = DockStyle.Fill };
    readonly Label _status = new() { AutoSize = true, Text = "подключение…" };
    readonly CheckBox _speak = new() { Text = "Говорить ответы", Checked = true, AutoSize = true };
    readonly Button _mic = new() { Text = "Голос", AutoSize = true };
    ClientWebSocket? _ws;
    readonly TtsPlayback? _tts;
    CancellationTokenSource? _receive;
    WakeWordListener? _wake;

    public MakoshDeskForm(MakoshSettings settings, Action? onClosed = null, TtsPlayback? tts = null)
    {
        _settings = settings;
        _onClosed = onClosed;
        _tts = tts;
        Text = "Makosh";
        Width = 640;
        Height = 480;
        MinimizeBox = true;
        MaximizeBox = true;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
        top.Controls.Add(_status);
        top.Controls.Add(_speak);
        top.Controls.Add(_mic);
        var send = new Button { Text = "Отправить", AutoSize = true };
        send.Click += (_, _) => SendChat(_input.Text);
        _mic.Click += (_, _) => _wake?.ListenCommandNow();
        top.Controls.Add(send);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        _input.Parent = bottom;
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SendChat(_input.Text);
            }
        };

        Controls.Add(_log);
        Controls.Add(bottom);
        Controls.Add(top);
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) =>
        {
            if (_tts is not null)
            {
                _tts.SpeakingStarted -= OnTtsStarted;
                _tts.SpeakingEnded -= OnTtsEnded;
            }
            _wake?.Dispose();
            _receive?.Cancel();
            _ws?.Dispose();
            _onClosed?.Invoke();
        };
    }

    async Task StartAsync()
    {
        _wake = new WakeWordListener();
        _wake.StatusChanged += text => BeginInvoke(() => _status.Text = text);
        _wake.Woke += () => BeginInvoke(() =>
        {
            _mic.BackColor = Color.LightGreen;
            _status.Text = "слушаю";
        });
        _wake.CommandReady += text => BeginInvoke(() =>
        {
            _mic.BackColor = SystemColors.Control;
            SendChat(text);
        });
        if (_tts is not null)
        {
            _tts.SpeakingStarted += OnTtsStarted;
            _tts.SpeakingEnded += OnTtsEnded;
        }
        if (!_wake.IsAvailable)
        {
            _status.Text = "окно готово, wake word недоступен";
        }

        try
        {
            _ws = new ClientWebSocket();
            var host = string.Equals(_settings.Host, "0.0.0.0", StringComparison.Ordinal) ? "127.0.0.1" : BindHost(_settings.Host);
            await _ws.ConnectAsync(new Uri($"ws://{host}:{_settings.Port}/ws"), CancellationToken.None);
            await SendJsonAsync(new
            {
                token = _settings.Token,
                name = "ПК",
                kind = "pc",
                device_id = "makosh-desktop",
            });
            _receive = new CancellationTokenSource();
            _ = ReceiveLoop(_receive.Token);
            _status.Text = _wake.IsAvailable ? "онлайн, жду «Макош»" : "онлайн";
        }
        catch (Exception ex)
        {
            Append("система", "WS: " + ex.Message);
            _status.Text = "нет связи с Hub";
        }
    }

    async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (_ws is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "";
                var text = root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                BeginInvoke(() =>
                {
                    if (type == "hello")
                    {
                        _status.Text = "онлайн, жду «Макош»";
                    }
                    else if (type == "reply")
                    {
                        Append("Makosh", text);
                        if (_status.Text == "обрабатываю")
                        {
                            _status.Text = _speak.Checked ? "говорю" : "жду «Макош»";
                        }
                    }
                    else if (type == "error")
                    {
                        _status.Text = string.IsNullOrEmpty(text) ? "ошибка" : text;
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            BeginInvoke(() => _status.Text = "WS закрыт: " + ex.Message);
        }
    }

    async void SendChat(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0 || _ws is not { State: WebSocketState.Open })
        {
            return;
        }

        _input.Clear();
        Append("ты", value);
        _status.Text = "обрабатываю";
        try
        {
            await SendJsonAsync(new { type = "chat", text = value, speak = _speak.Checked });
        }
        catch (Exception ex)
        {
            Append("система", ex.Message);
        }
    }

    Task SendJsonAsync(object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    void Append(string who, string text)
    {
        _log.AppendText(who + ": " + text + Environment.NewLine);
    }

    void OnTtsStarted()
    {
        _wake?.Pause();
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() => _status.Text = "говорю");
        }
    }

    void OnTtsEnded()
    {
        _wake?.Resume();
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() =>
            {
                _mic.BackColor = SystemColors.Control;
                if (_status.Text == "говорю")
                {
                    _status.Text = "жду «Макош»";
                }
            });
        }
    }

    static string BindHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;
}
