using Makosh.Core;
using Makosh.Windows;

namespace Makosh.Tests;

public class WindowsVisionSmokeTests
{
    [Fact]
    public async Task Real_capture_jpeg_and_ocr_when_explicitly_requested()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAKOSH_VISION_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var image = await new WindowsScreenCapture().CaptureAsync();
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
        Assert.Equal(image.Width * image.Height * 4, image.Bgra.Length);

        var again = await new WindowsScreenCapture().CaptureAsync();
        Assert.Equal(image.Width, again.Width);
        Assert.Equal(image.Height, again.Height);

        var jpeg = await new WindowsJpegEncoder().EncodeAsync(image.FitMaxWidth(768), VisionJpeg.Quality);
        Assert.True(jpeg.Length > 2);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);

        try
        {
            var text = await new WindowsOcrService().RecognizeAsync(image);
            Assert.NotNull(text);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("OCR", StringComparison.Ordinal))
        {
            // Language pack may be missing on a fresh Windows image.
        }
    }
}
