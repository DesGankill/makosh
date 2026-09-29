using Makosh.Core;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Makosh.Windows;

public sealed class WindowsOcrService : IOcrService
{
    public async Task<string> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? TryLanguage("ru")
                     ?? TryLanguage("en-US")
                     ?? TryLanguage("en");
        if (engine is null)
        {
            throw new InvalidOperationException("Windows OCR недоступен (нет языкового пакета).");
        }

        var bitmap = ToSoftwareBitmap(image);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        return string.Join("\n", result.Lines.Select(line => line.Text)).Trim();
    }

    static OcrEngine? TryLanguage(string tag)
    {
        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language(tag));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static SoftwareBitmap ToSoftwareBitmap(ScreenImage image)
    {
        var buffer = CryptographicBuffer.CreateFromByteArray(image.Bgra);
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            image.Width,
            image.Height,
            BitmapAlphaMode.Ignore);
    }
}

public sealed class WindowsJpegEncoder : IJpegEncoder
{
    public async Task<byte[]> EncodeAsync(ScreenImage image, int quality, CancellationToken cancellationToken = default)
    {
        using var stream = new InMemoryRandomAccessStream();
        var q = Math.Clamp(quality / 100f, 0.01f, 1f);
        var props = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(q, PropertyType.Single),
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, props).AsTask(cancellationToken);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)image.Width,
            (uint)image.Height,
            96,
            96,
            image.Bgra);
        await encoder.FlushAsync().AsTask(cancellationToken);
        stream.Seek(0);
        var size = (uint)stream.Size;
        var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size).AsTask(cancellationToken);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
