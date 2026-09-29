using System.ComponentModel;
using System.Runtime.InteropServices;
using Makosh.Core;

namespace Makosh.Windows;

public sealed class WindowsScreenCapture : IScreenCapture
{
    const int SmCxScreen = 0;
    const int SmCyScreen = 1;
    const uint SrcCopy = 0x00CC0020;
    const uint CaptureBlt = 0x40000000;
    const uint DibRgbColors = 0;

    public Task<ScreenImage> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CapturePrimary());
    }

    static ScreenImage CapturePrimary()
    {
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Не удалось определить размер основного монитора.");
        }

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed.");
        }

        var hdcMem = IntPtr.Zero;
        var hbmp = IntPtr.Zero;
        var old = IntPtr.Zero;
        try
        {
            hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC failed.");
            }

            hbmp = CreateCompatibleBitmap(hdcScreen, width, height);
            if (hbmp == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleBitmap failed.");
            }

            old = SelectObject(hdcMem, hbmp);
            if (!BitBlt(hdcMem, 0, 0, width, height, hdcScreen, 0, 0, SrcCopy | CaptureBlt))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed.");
            }

            var bgra = new byte[width * height * 4];
            var info = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };

            var copied = GetDIBits(hdcMem, hbmp, 0, (uint)height, bgra, ref info, DibRgbColors);
            if (copied == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDIBits failed.");
            }

            return new ScreenImage(width, height, bgra);
        }
        finally
        {
            if (old != IntPtr.Zero)
            {
                SelectObject(hdcMem, old);
            }

            if (hbmp != IntPtr.Zero)
            {
                DeleteObject(hbmp);
            }

            if (hdcMem != IntPtr.Zero)
            {
                DeleteDC(hdcMem);
            }

            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint cLines, byte[] lpvBits, ref BitmapInfoHeader lpbmi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }
}
