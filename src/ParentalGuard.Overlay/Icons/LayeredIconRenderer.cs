using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ParentalGuard.Overlay.Icons;

/// <summary>
/// <c>UpdateLayeredWindow</c> per-pixel alpha (ADR-64, Architecture/07-overlay-architecture.md mục
/// 4.1.1) — vẽ 1 bitmap 32bpp ARGB lên window <c>WS_EX_LAYERED</c>. Alpha phải premultiply thủ công
/// trước khi đưa vào DIB section (yêu cầu bắt buộc của <c>AC_SRC_ALPHA</c>).
/// </summary>
internal static class LayeredIconRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    private const uint _ulwAlpha = 2;
    private const byte _acSrcOver = 0;
    private const byte _acSrcAlpha = 1;
    private const uint _dibRgbColors = 0;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    /// <summary>Vẽ <paramref name="sourceBitmap"/> (32bpp ARGB) lên <paramref name="formHandle"/> tại <paramref name="screenLocation"/> tuyệt đối.</summary>
    internal static void Render(IntPtr formHandle, Bitmap sourceBitmap, Point screenLocation)
    {
        int width = sourceBitmap.Width;
        int height = sourceBitmap.Height;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);

        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // top-down DIB — khớp thứ tự scanline của Bitmap.LockBits
            Planes = 1,
            BitCount = 32,
            Compression = 0, // BI_RGB
        };

        IntPtr dibBitmap = CreateDIBSection(memDc, ref header, _dibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr previousBitmap = SelectObject(memDc, dibBitmap);
        try
        {
            WritePremultipliedPixels(sourceBitmap, bits, width, height);

            var dstPos = new NativePoint { X = screenLocation.X, Y = screenLocation.Y };
            var srcPos = new NativePoint { X = 0, Y = 0 };
            var size = new NativeSize { Cx = width, Cy = height };
            var blend = new BlendFunction { BlendOp = _acSrcOver, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = _acSrcAlpha };
            UpdateLayeredWindow(formHandle, screenDc, ref dstPos, ref size, memDc, ref srcPos, 0, ref blend, _ulwAlpha);
        }
        finally
        {
            SelectObject(memDc, previousBitmap);
            DeleteObject(dibBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void WritePremultipliedPixels(Bitmap source, IntPtr destBits, int width, int height)
    {
        BitmapData data = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * stride), row, 0, stride);
                for (int x = 0; x < width; x++)
                {
                    int offset = x * 4;
                    byte b = row[offset];
                    byte g = row[offset + 1];
                    byte r = row[offset + 2];
                    byte a = row[offset + 3];
                    row[offset] = (byte)(b * a / 255);
                    row[offset + 1] = (byte)(g * a / 255);
                    row[offset + 2] = (byte)(r * a / 255);
                    row[offset + 3] = a;
                }

                Marshal.Copy(row, 0, destBits + (y * stride), stride);
            }
        }
        finally
        {
            source.UnlockBits(data);
        }
    }
}
