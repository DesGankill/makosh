namespace Makosh.Core;

public interface IScreenCapture
{
    Task<ScreenImage> CaptureAsync(CancellationToken cancellationToken = default);
}

public sealed class OcrPage
{
    public IReadOnlyList<string> Lines { get; init; } = [];

    public string Combined => string.Join("\n", Lines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();

    public bool IsEmpty => string.IsNullOrWhiteSpace(Combined);

    public bool IsUncertain => IsEmpty || Combined.Length < 8;

    public static OcrPage FromText(string? text)
    {
        var lines = (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new OcrPage { Lines = lines };
    }

    public string FormatFacts(int maxChars)
    {
        if (IsEmpty)
        {
            return "(OCR пуст: уверенных текстовых элементов нет)";
        }

        var body = string.Join("\n", Lines.Select((line, index) => $"{index + 1}. {line}"));
        return body.Length <= maxChars ? body : body[..maxChars];
    }
}

public interface IOcrService
{
    Task<OcrPage> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken = default);
}

public interface IJpegEncoder
{
    Task<byte[]> EncodeAsync(ScreenImage image, int quality, CancellationToken cancellationToken = default);
}

public interface IVisionClient
{
    Task<string> DescribeAsync(string model, string prompt, byte[] jpeg, CancellationToken cancellationToken = default);
}

public static class VisionJpeg
{
    public const int Quality = 45;
}
