using System.Net.WebSockets;
using System.Text.Json;
using Makosh.Core;
using Makosh.Windows;
using Microsoft.Extensions.Logging;

namespace Makosh;

public static class HubEndpoints
{
    public const string ChatFailureReply = "Не получилось обработать сообщение. Подробности в логе сервера.";

    public static WebApplication MapMakoshHub(this WebApplication app)
    {
        app.MapGet("/api/health", (MakoshSettings settings) => Results.Json(new
        {
            ok = "makosh",
            device = settings.DeviceName,
            runtime = "dotnet",
            windows = WindowsRuntime.TargetFrameworkMoniker,
        }));

        app.MapGet("/api/token-check", (HttpContext http, MakoshSettings settings) =>
        {
            if (!Authorized(http, settings))
            {
                return Unauthorized();
            }

            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/chat", async (ChatBody? body, HttpContext http, Agent agent, MakoshSettings settings, TtsPlayback tts) =>
        {
            if (!Authorized(http, settings))
            {
                return Unauthorized();
            }

            if (body is null)
            {
                return Results.Json(new { detail = "Некорректный JSON" }, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var speaker = string.IsNullOrWhiteSpace(body.DeviceName) ? "web" : body.DeviceName;
                var reply = await agent.HandleAsync(body.Text ?? "", speaker);
                tts.SpeakIfRequested(body.Speak, reply);
                return Results.Json(new { reply });
            }
            catch (Exception ex)
            {
                LogHub(http).LogError(ex, "Ошибка обработки сообщения");
                return Results.Json(new { detail = ChatFailureReply }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapGet("/api/tts/voices", (HttpContext http, MakoshSettings settings, ITtsService tts, TtsRuntime runtime) =>
        {
            if (!Authorized(http, settings))
            {
                return Unauthorized();
            }

            var voices = tts.GetVoices();
            var selected = TtsVoicePicker.Select(voices, runtime.Voice);
            return Results.Json(new
            {
                enabled = runtime.Enabled,
                engine = TtsEngines.Normalize(runtime.Engine),
                selected = selected?.Id ?? runtime.Voice,
                pitchSupported = tts.SupportsPitch,
                rate = runtime.Rate,
                volume = runtime.Volume,
                pitch = runtime.Pitch,
                voices,
            });
        });

        app.MapPost("/api/tts/settings", (TtsSettingsBody? body, HttpContext http, MakoshSettings settings, TtsRuntime runtime, ITtsService tts) =>
        {
            if (!Authorized(http, settings))
            {
                return Unauthorized();
            }

            if (body is null)
            {
                return Results.Json(new { detail = "Некорректный JSON" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (body.Enabled is not null)
            {
                runtime.Enabled = body.Enabled.Value;
            }

            if (body.Engine is not null)
            {
                runtime.Engine = TtsEngines.Normalize(body.Engine);
            }

            if (body.Voice is not null)
            {
                runtime.Voice = body.Voice;
            }

            if (body.Rate is not null)
            {
                runtime.Rate = TtsLimits.ClampRate(body.Rate.Value);
            }

            if (body.Volume is not null)
            {
                runtime.Volume = TtsLimits.ClampVolume(body.Volume.Value);
            }

            if (body.Pitch is not null)
            {
                runtime.Pitch = TtsLimits.ClampPitch(body.Pitch.Value);
            }

            var selected = TtsVoicePicker.Select(tts.GetVoices(), runtime.Voice);
            return Results.Json(new
            {
                enabled = runtime.Enabled,
                engine = TtsEngines.Normalize(runtime.Engine),
                selected = selected?.Id ?? runtime.Voice,
                rate = runtime.Rate,
                volume = runtime.Volume,
                pitch = runtime.Pitch,
                pitchSupported = tts.SupportsPitch,
            });
        });

        app.MapPost("/api/tts/speak", (TtsSpeakBody? body, HttpContext http, MakoshSettings settings, TtsPlayback tts) =>
        {
            if (!Authorized(http, settings))
            {
                return Unauthorized();
            }

            var text = body?.Text ?? "";
            tts.SpeakIfRequested(true, text);
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/upload/{deviceId}", async (string deviceId, HttpContext http, MakoshSettings settings) =>
        {
            if (!Authorized(http, settings))
            {
                return Unauthorized();
            }

            if (!http.Request.HasFormContentType)
            {
                return Results.Json(new { detail = "Нужен файл" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var form = await http.Request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.Json(new { detail = "Нужен файл" }, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var dest = InboxPaths.Destination(settings.InboxDirectory, deviceId, file.FileName);
                await using var stream = File.Create(dest);
                await file.CopyToAsync(stream);
                return Results.Json(new { saved = dest });
            }
            catch (InboxPathException)
            {
                return BadPath();
            }
        }).DisableAntiforgery();

        app.MapGet("/api/inbox/{deviceId}/{name}", (string deviceId, string name, string? token, MakoshSettings settings) =>
        {
            if (!MakoshSettings.TokenMatches(token, settings.Token))
            {
                return Unauthorized();
            }

            try
            {
                var path = InboxPaths.ExistingFile(settings.InboxDirectory, deviceId, name);
                if (!File.Exists(path))
                {
                    return Results.Json(new { detail = "Нет файла" }, statusCode: StatusCodes.Status404NotFound);
                }

                return Results.File(path, fileDownloadName: name);
            }
            catch (InboxPathException)
            {
                return BadPath();
            }
        });

        app.Map("/ws", HandleWebSocketAsync);
        return app;
    }

    static bool Authorized(HttpContext http, MakoshSettings settings) =>
        MakoshSettings.TokenMatches(http.Request.Headers["x-makosh-token"].ToString(), settings.Token);

    static IResult Unauthorized() =>
        Results.Json(new { detail = "Неверный токен" }, statusCode: StatusCodes.Status401Unauthorized);

    static IResult BadPath() =>
        Results.Json(new { detail = "Плохой путь" }, statusCode: StatusCodes.Status400BadRequest);

    static async Task HandleWebSocketAsync(HttpContext http)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var settings = http.RequestServices.GetRequiredService<MakoshSettings>();
        var devices = http.RequestServices.GetRequiredService<DeviceRegistry>();
        var agent = http.RequestServices.GetRequiredService<Agent>();
        var tts = http.RequestServices.GetRequiredService<TtsPlayback>();
        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        var sessionId = Guid.Empty;
        var deviceId = "";
        try
        {
            JsonDocument? helloDoc;
            try
            {
                helloDoc = await ReadJsonAsync(socket, http.RequestAborted);
            }
            catch (HubJsonException)
            {
                await SendJsonAsync(socket, new { type = "error", text = "Некорректное сообщение" }, http.RequestAborted);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "json", http.RequestAborted);
                return;
            }

            if (helloDoc is null)
            {
                return;
            }

            using var helloOwned = helloDoc;
            var hello = helloOwned.RootElement;
            var token = hello.TryGetProperty("token", out var tokenEl) ? tokenEl.GetString() : null;
            if (!MakoshSettings.TokenMatches(token, settings.Token))
            {
                await SendJsonAsync(socket, new { type = "error", text = "Неверный токен" }, http.RequestAborted);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "auth", http.RequestAborted);
                return;
            }

            deviceId = hello.TryGetProperty("device_id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
            }

            var name = hello.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "device" : "device";
            var kind = hello.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() ?? "web" : "web";
            var connection = new WebSocketDeviceConnection(socket);
            var device = new Device
            {
                DeviceId = deviceId,
                Name = name,
                Kind = kind,
                SessionId = Guid.NewGuid(),
                Connection = connection,
            };
            sessionId = device.SessionId;
            devices.Register(device);
            await SendJsonAsync(socket, new Dictionary<string, string>
            {
                ["type"] = "hello",
                ["device_id"] = deviceId,
                ["name"] = settings.DeviceName,
            }, http.RequestAborted);

            while (socket.State == WebSocketState.Open)
            {
                JsonDocument? messageDoc;
                try
                {
                    messageDoc = await ReadJsonAsync(socket, http.RequestAborted);
                }
                catch (HubJsonException)
                {
                    await SendJsonAsync(socket, new { type = "error", text = "Некорректное сообщение" }, http.RequestAborted);
                    continue;
                }

                if (messageDoc is null)
                {
                    break;
                }

                using (messageDoc)
                {
                    var msg = messageDoc.RootElement;
                    var type = msg.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                    if (type != "chat")
                    {
                        continue;
                    }

                    var text = msg.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                    var speak = !msg.TryGetProperty("speak", out var speakEl) || speakEl.ValueKind != JsonValueKind.False;
                    string reply;
                    try
                    {
                        reply = await agent.HandleAsync(text, name, http.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        LogHub(http).LogError(ex, "Ошибка обработки сообщения");
                        reply = ChatFailureReply;
                    }

                    await SendJsonAsync(socket, new Dictionary<string, object?>
                    {
                        ["type"] = "reply",
                        ["text"] = reply,
                        ["speak"] = speak,
                    }, http.RequestAborted);
                    tts.SpeakIfRequested(speak, reply);
                }
            }
        }
        catch (WebSocketException)
        {
            // Client dropped the socket.
        }
        catch (OperationCanceledException)
        {
            // Host shutdown or abort.
        }
        catch (HubJsonException)
        {
            // Invalid payload after the socket was already closing.
        }
        finally
        {
            if (!string.IsNullOrEmpty(deviceId) && sessionId != Guid.Empty)
            {
                devices.DropIfSession(deviceId, sessionId);
            }
        }
    }

    static async Task<JsonDocument?> ReadJsonAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                return null;
            }

            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage)
            {
                buffer.Position = 0;
                try
                {
                    return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
                }
                catch (JsonException)
                {
                    throw new HubJsonException();
                }
            }
        }

        return null;
    }

    static ILogger LogHub(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("makosh.hub");

    static Task SendJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}

sealed class HubJsonException : Exception;
