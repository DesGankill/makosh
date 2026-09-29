using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Makosh;
using Makosh.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Makosh.Tests;

public class HubTests : IClassFixture<MakoshWebFactory>
{
    readonly MakoshWebFactory _factory;
    readonly HttpClient _client;
    const string Token = "m1-test-token";

    public HubTests(MakoshWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    void ReadyLlm(params object[] steps) => _factory.Fake.Reset(steps.Length == 0 ? [FakeChatClient.Text("Ответ.")] : steps);

    [Fact]
    public async Task Health_is_ok_without_token()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("makosh", doc.RootElement.GetProperty("ok").GetString());
    }

    [Fact]
    public async Task Chat_requires_token()
    {
        var denied = await _client.PostAsJsonAsync("/api/chat", new { text = "ping" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("m1-test-token", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_returns_reply_with_token_and_device_name()
    {
        ReadyLlm(FakeChatClient.Text("Ответ."));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { text = "Привет", speak = false, device_name = "PC" }),
        };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Ответ.", doc.RootElement.GetProperty("reply").GetString());
        Assert.False(doc.RootElement.TryGetProperty("text", out _));
        Assert.Contains(_factory.Fake.Calls[0].Messages, item => item.Role == "user" && item.Content == "[PC] Привет");
    }

    [Fact]
    public async Task Chat_accepts_empty_text()
    {
        ReadyLlm(FakeChatClient.Text("пусто."));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { text = "" }),
        };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("пусто.", doc.RootElement.GetProperty("reply").GetString());
    }

    [Fact]
    public async Task Chat_rejects_invalid_json()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("at Makosh", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_hides_internal_errors()
    {
        ReadyLlm(new InvalidOperationException("internal-secret"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { text = "boom" }),
        };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Не получилось обработать", body, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at Makosh", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_registers_send_file_when_hub_is_wired()
    {
        var names = _factory.Services.GetRequiredService<Agent>().Tools.Select(tool => tool.Definition.Name).ToList();
        Assert.Contains("send_file", names);
        Assert.Contains("look_screen", names);
        Assert.DoesNotContain("not_a_tool", names);
    }

    [Fact]
    public async Task Websocket_rejects_bad_token()
    {
        var socket = await OpenWs();
        await SendJson(socket, new { token = "wrong", name = "phone", kind = "android" });
        var message = await ReceiveJson(socket);
        Assert.Equal("error", message.GetProperty("type").GetString());
        Assert.Contains("токен", message.GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Websocket_chat_roundtrip_and_hello()
    {
        ReadyLlm(FakeChatClient.Text("Ответ."));
        var (socket, hello) = await ConnectDevice("ws-1");
        Assert.Equal("hello", hello.GetProperty("type").GetString());
        Assert.Equal("ws-1", hello.GetProperty("device_id").GetString());
        Assert.Equal("M1-Test", hello.GetProperty("name").GetString());
        await SendJson(socket, new { type = "chat", text = "привет", speak = false });
        var reply = await ReceiveJson(socket);
        Assert.Equal("reply", reply.GetProperty("type").GetString());
        Assert.Equal("Ответ.", reply.GetProperty("text").GetString());
        Assert.False(reply.GetProperty("speak").GetBoolean());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_invalid_message_sends_error()
    {
        var (socket, _) = await ConnectDevice("ws-bad-json");
        await socket.SendAsync(Encoding.UTF8.GetBytes("{not-json"), WebSocketMessageType.Text, true, CancellationToken.None);
        var error = await ReceiveJson(socket);
        Assert.Equal("error", error.GetProperty("type").GetString());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }

    [Fact]
    public async Task Websocket_survives_llm_exception()
    {
        ReadyLlm(new InvalidOperationException("llm down"), FakeChatClient.Text("Снова на связи."));
        var (socket, _) = await ConnectDevice("ws-resilient");
        await SendJson(socket, new { type = "chat", text = "сломайся", speak = true });
        var failed = await ReceiveJson(socket);
        Assert.Equal("reply", failed.GetProperty("type").GetString());
        Assert.Equal(HubEndpoints.ChatFailureReply, failed.GetProperty("text").GetString());
        Assert.NotNull(_factory.Services.GetRequiredService<DeviceRegistry>().Get("ws-resilient"));

        await SendJson(socket, new { type = "chat", text = "ещё раз", speak = true });
        var recovered = await ReceiveJson(socket);
        Assert.Equal("Снова на связи.", recovered.GetProperty("text").GetString());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await WaitUntil(() => _factory.Services.GetRequiredService<DeviceRegistry>().Get("ws-resilient") is null);
    }

    [Fact]
    public async Task Websocket_reconnect_cleanup_does_not_drop_the_new_session()
    {
        ReadyLlm(FakeChatClient.Text("from-B"));
        const string id = "reconnect-1";
        var (socketA, _) = await ConnectDevice(id, name: "Alpha");
        var (socketB, helloB) = await ConnectDevice(id, name: "Beta");
        Assert.Equal(id, helloB.GetProperty("device_id").GetString());
        var devices = _factory.Services.GetRequiredService<DeviceRegistry>();
        Assert.Equal("Beta", devices.Get(id)?.Name);

        await socketA.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None);
        await WaitUntil(() => devices.Get(id)?.Name == "Beta" && devices.Get(id) is not null);

        Assert.Equal("Beta", devices.Get(id)?.Name);
        Assert.NotNull(devices.Get(id)?.Connection);

        await SendJson(socketB, new { type = "chat", text = "ещё тут", speak = false });
        var reply = await ReceiveJson(socketB);
        Assert.Equal("reply", reply.GetProperty("type").GetString());
        Assert.Equal("from-B", reply.GetProperty("text").GetString());
        Assert.Equal("Beta", devices.Get(id)?.Name);

        await socketB.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        await WaitUntil(() => devices.Get(id) is null);
    }

    [Fact]
    public async Task Upload_and_download_roundtrip()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("hello"u8.ToArray()), "file", "note.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/phone") { Content = content };
        request.Headers.Add("x-makosh-token", Token);
        var uploaded = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var saved = Path.Combine(_factory.DataDir, "inbox", "phone", "note.txt");
        Assert.True(File.Exists(saved));
        Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(saved));

        var downloaded = await _client.GetAsync($"/api/inbox/phone/note.txt?token={Token}");
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("hello"u8.ToArray(), await downloaded.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_requires_token()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("x"u8.ToArray()), "file", "note2.txt");
        var denied = await _client.PostAsync("/api/upload/phone", content);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.False(File.Exists(Path.Combine(_factory.DataDir, "inbox", "phone", "note2.txt")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("foo/bar.txt")]
    [InlineData("..")]
    public async Task Upload_rejects_path_in_filename(string filename)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("pwned"u8.ToArray()), "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/phone") { Content = content };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Плохой путь", doc.RootElement.GetProperty("detail").GetString());
        var leaked = Directory.Exists(_factory.DataDir)
            ? Directory.GetFiles(_factory.DataDir, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) is "outside.txt" or "bar.txt" or "pwned")
            : [];
        Assert.Empty(leaked);
    }

    [Fact]
    public async Task Upload_rejects_device_id_traversal()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("nope"u8.ToArray()), "file", "escape.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/foo%5cbar") { Content = content };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Download_requires_token_and_404s_missing_file()
    {
        var denied = await _client.GetAsync("/api/inbox/phone/missing.txt");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var missing = await _client.GetAsync($"/api/inbox/phone/missing.txt?token={Token}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var doc = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("Нет файла", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Download_rejects_traversal()
    {
        var response = await _client.GetAsync($"/api/inbox/phone/%2e%2e%5coutside.txt?token={Token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var encoded = await _client.GetAsync($"/api/inbox/foo%5cbar/note.txt?token={Token}");
        Assert.Equal(HttpStatusCode.BadRequest, encoded.StatusCode);
    }

    [Fact]
    public async Task Send_file_offline_copies_to_unclaimed()
    {
        var src = Path.Combine(_factory.DataDir, "payload-offline.txt");
        File.WriteAllText(src, "offline-bytes");
        var args = JsonSerializer.Serialize(new { path = src, device = "ghost" });
        ReadyLlm(FakeChatClient.Tool("send_file", args), FakeChatClient.Text("положил."));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { text = "отправь", device_name = "web" }),
        };
        request.Headers.Add("x-makosh-token", Token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("положил.", doc.RootElement.GetProperty("reply").GetString());
        var dest = Path.Combine(_factory.DataDir, "inbox", "unclaimed", "payload-offline.txt");
        Assert.True(File.Exists(dest));
        Assert.Equal("offline-bytes", File.ReadAllText(dest));
        var tool = _factory.Fake.Calls[1].Messages.Last(item => item.Role == "tool").Content;
        Assert.Contains("не в сети", tool, StringComparison.Ordinal);
        Assert.Contains("unclaimed", tool, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_file_online_copies_and_emits_websocket_file_event()
    {
        var src = Path.Combine(_factory.DataDir, "payload-online.txt");
        File.WriteAllText(src, "online-bytes");
        var (socket, _) = await ConnectDevice("phone-online", name: "Pixel", kind: "android");
        var args = JsonSerializer.Serialize(new { path = src, device = "phone" });
        ReadyLlm(FakeChatClient.Tool("send_file", args), FakeChatClient.Text("отправил."));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { text = "отправь", device_name = "web" }),
        };
        request.Headers.Add("x-makosh-token", Token);
        var httpTask = _client.SendAsync(request);
        var fileEvt = await ReceiveJson(socket);
        Assert.Equal("file", fileEvt.GetProperty("type").GetString());
        Assert.Equal("payload-online.txt", fileEvt.GetProperty("name").GetString());
        Assert.Equal("/api/inbox/phone-online/payload-online.txt", fileEvt.GetProperty("url").GetString());
        using var http = await httpTask;
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var dest = Path.Combine(_factory.DataDir, "inbox", "phone-online", "payload-online.txt");
        Assert.True(File.Exists(dest));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }

    async Task<WebSocket> OpenWs()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        return await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress!, "/ws"), CancellationToken.None);
    }

    async Task<(WebSocket Socket, JsonElement Hello)> ConnectDevice(string deviceId, string name = "phone", string kind = "android")
    {
        var socket = await OpenWs();
        await SendJson(socket, new { token = Token, name, kind, device_id = deviceId });
        return (socket, await ReceiveJson(socket));
    }

    static Task SendJson(WebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    static async Task<JsonElement> ReceiveJson(WebSocket socket)
    {
        var chunk = new byte[64 * 1024];
        using var buffer = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(chunk, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("websocket closed while waiting for json");
            }

            buffer.Write(chunk, 0, result.Count);
        } while (!result.EndOfMessage);

        buffer.Position = 0;
        using var doc = await JsonDocument.ParseAsync(buffer);
        return doc.RootElement.Clone();
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("timed out waiting for websocket cleanup");
    }
}
