namespace Makosh.Core;

public sealed class ScreenImage
{
    public ScreenImage(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Screenshot size must be positive.");
        }

        var expected = checked(width * height * 4);
        if (bgra.Length != expected)
        {
            throw new ArgumentException($"BGRA buffer length {bgra.Length} does not match {width}x{height}.", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public ScreenImage FitMaxWidth(int maxWidth)
    {
        if (maxWidth <= 0 || Width <= maxWidth)
        {
            return this;
        }

        var ratio = maxWidth / (double)Width;
        var newWidth = maxWidth;
        var newHeight = Math.Max(1, (int)(Height * ratio));
        var dest = new byte[newWidth * newHeight * 4];
        for (var y = 0; y < newHeight; y++)
        {
            var srcY = Math.Min(Height - 1, y * Height / newHeight);
            for (var x = 0; x < newWidth; x++)
            {
                var srcX = Math.Min(Width - 1, x * Width / newWidth);
                var from = (srcY * Width + srcX) * 4;
                var to = (y * newWidth + x) * 4;
                dest[to] = Bgra[from];
                dest[to + 1] = Bgra[from + 1];
                dest[to + 2] = Bgra[from + 2];
                dest[to + 3] = Bgra[from + 3];
            }
        }

        return new ScreenImage(newWidth, newHeight, dest);
    }

    public static ScreenImage Solid(int width, int height, byte b, byte g, byte r, byte a = 255)
    {
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = a;
        }

        return new ScreenImage(width, height, data);
    }
}
