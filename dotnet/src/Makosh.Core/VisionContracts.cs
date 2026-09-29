namespace Makosh.Core;

public interface IScreenCapture
{
    Task<ScreenImage> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IOcrService
{
    Task<string> RecognizeAsync(ScreenImage image, CancellationToken cancellationToken = default);
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
