using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Makosh.Windows;

static class SileroWorkerProtocol
{
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] EncodeLine(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Utf8.GetBytes(json + "\n");
    }

    public static void WriteLine(Stream stream, object payload)
    {
        var bytes = EncodeLine(payload);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static JsonElement ParseLine(byte[] utf8)
    {
        var end = utf8.Length;
        while (end > 0 && (utf8[end - 1] == (byte)'\n' || utf8[end - 1] == (byte)'\r'))
        {
            end--;
        }

        return JsonSerializer.Deserialize<JsonElement>(utf8.AsSpan(0, end));
    }
}
