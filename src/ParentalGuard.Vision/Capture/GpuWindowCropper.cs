using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Bước 2+3 phần GPU (Architecture/05 mục 4.2, ADR-41): crop thẳng từ frame toàn màn hình vào 1
/// staging texture CPU-accessible, tái dùng giữa các frame cùng kích thước (`PERF-040`). Zero-out
/// nội dung ngay sau <c>Map()</c>, trước <c>Unmap()</c> (`IMG-003`, bảng mục 6).
/// </summary>
public sealed class GpuWindowCropper(ID3D11Device device) : IWindowCropper
{
    private readonly ID3D11DeviceContext _context = device.ImmediateContext;
    private ID3D11Texture2D? _stagingTexture;
    private int _width;
    private int _height;

    /// <summary>
    /// Crop + readback ra <paramref name="destinationBgra8"/> (tái dùng buffer nếu đủ lớn — <c>PERF-040</c>).
    /// Trả về số byte hợp lệ đã ghi (row pitch của D3D11 có thể lớn hơn <c>width*4</c>, đã được
    /// nén khít lại thành BGRA8 chặt trong buffer đích).
    /// </summary>
    public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
    {
        ArgumentNullException.ThrowIfNull(fullScreenFrame);
        // IWindowCropper gõ kiểu IDisposable để interface (và test) không cần phụ thuộc Vortice.Direct3D11.
        var texture = (ID3D11Texture2D)fullScreenFrame;
        EnsureStagingTexture(cropRect.Width, cropRect.Height);

        var sourceBox = new Box(cropRect.X, cropRect.Y, 0, cropRect.X + cropRect.Width, cropRect.Y + cropRect.Height, 1);
        _context.CopySubresourceRegion(_stagingTexture!, 0, 0, 0, 0, texture, 0, sourceBox);

        MappedSubresource mapped = _context.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = _width * 4;
            for (int row = 0; row < _height; row++)
            {
                unsafe
                {
                    var source = new ReadOnlySpan<byte>((byte*)mapped.DataPointer + (row * mapped.RowPitch), rowBytes);
                    source.CopyTo(destinationBgra8.AsSpan(row * rowBytes, rowBytes));
                }
            }
        }
        finally
        {
            // IMG-003 (bảng mục 6): ghi đè 0 vùng con trỏ Map() trả về TRƯỚC Unmap().
            unsafe
            {
                for (int row = 0; row < _height; row++)
                {
                    new Span<byte>((byte*)mapped.DataPointer + (row * mapped.RowPitch), _width * 4).Clear();
                }
            }

            _context.Unmap(_stagingTexture!, 0);
        }
    }

    private void EnsureStagingTexture(int width, int height)
    {
        if (_stagingTexture is not null && _width == width && _height == height)
        {
            return;
        }

        // Kích thước đổi (cửa sổ resize) — texture cũ zero-out toàn 0 rồi Dispose trước khi cấp
        // phát texture mới (bảng mục 6, ADR-43) thay vì để GC dọn với dữ liệu cũ còn nguyên.
        if (_stagingTexture is not null)
        {
            ZeroOutBeforeDispose(_stagingTexture, _width, _height);
            _stagingTexture.Dispose();
        }

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        _stagingTexture = device.CreateTexture2D(description);
        _width = width;
        _height = height;
    }

    private void ZeroOutBeforeDispose(ID3D11Texture2D texture, int width, int height)
    {
        MappedSubresource mapped = _context.Map(texture, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                for (int row = 0; row < height; row++)
                {
                    new Span<byte>((byte*)mapped.DataPointer + (row * mapped.RowPitch), width * 4).Clear();
                }
            }
        }
        finally
        {
            _context.Unmap(texture, 0);
        }
    }

    public void Dispose()
    {
        if (_stagingTexture is not null)
        {
            ZeroOutBeforeDispose(_stagingTexture, _width, _height);
            _stagingTexture.Dispose();
        }
    }
}
