using Makosh.Core;

namespace Makosh.Tests;

public sealed class FakeScreenCapture : IScreenCapture
{
    readonly Func<ScreenImage> _capture;

    public FakeScreenCapture(ScreenImage image) : this(() => image)
    {
    }

    public FakeScreenCapture(Func<ScreenImage> capture)
    {
        _capture = capture;
    }

    public int Calls { get; private set; }

    public Task<ScreenImage> CaptureAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(_capture());
    }
}

public sealed class FakeOcrService : IOcrService
{
    readonly Func<ScreenImage, string> _ocr;

    public FakeOcrService(string text) : this(_ => text)
    {
    }

    public FakeOcrService(Func<ScreenImage, string> ocr)
    {
        _ocr = ocr;
    }

    public int Calls { get; private set; }

    public Task<OcrPage> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(OcrPage.FromText(_ocr(image)));
    }
}

public sealed class FakeJpegEncoder : IJpegEncoder
{
    public ScreenImage? LastImage { get; private set; }
    public int LastQuality { get; private set; }
    public byte[] LastBytes { get; private set; } = [0xFF, 0xD8, 0xFF, 0xD9];

    public Task<byte[]> EncodeAsync(ScreenImage image, int quality, CancellationToken cancellationToken = default)
    {
        LastImage = image;
        LastQuality = quality;
        return Task.FromResult(LastBytes);
    }
}

public sealed class FakeVisionClient : IVisionClient
{
    public string Reply { get; set; } = "на экране код";
    public bool Called { get; set; }
    public string? LastModel { get; private set; }
    public string? LastPrompt { get; private set; }
    public byte[]? LastJpeg { get; private set; }

    public Task<string> DescribeAsync(string model, string prompt, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        Called = true;
        LastModel = model;
        LastPrompt = prompt;
        LastJpeg = jpeg;
        return Task.FromResult(Reply);
    }
}
