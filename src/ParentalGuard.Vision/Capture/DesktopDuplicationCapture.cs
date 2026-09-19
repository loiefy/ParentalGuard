using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Bước 1 thật (Architecture/05 mục 4.1): sở hữu 1 <see cref="ID3D11Device"/> (tạo 1 lần lúc
/// Vision khởi động) + 1 <see cref="IDXGIOutputDuplication"/> theo output hiện tại. Ném
/// <see cref="CaptureInitializationException"/> khi khởi tạo lần đầu thất bại — Program.cs phân
/// loại đây là tín hiệu "có thể do Integrity Level" (mục 8.1 bước 3, exit code 17).
/// </summary>
public sealed class DesktopDuplicationCapture : IFrameCapture
{
    private readonly ID3D11Device _device;
    private IDXGIOutputDuplication? _duplication;
    private int _currentAdapterIndex = -1;
    private int _currentOutputIndex = -1;

    public DesktopDuplicationCapture()
    {
        try
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                out ID3D11Device? device,
                out _,
                out ID3D11DeviceContext? context).CheckError();
            context?.Dispose();
            _device = device!;
        }
        catch (SharpGenException ex)
        {
            throw new CaptureInitializationException("D3D11CreateDevice failed.", ex);
        }
    }

    /// <summary>
    /// Trả về <see cref="ID3D11Texture2D"/> toàn màn hình (sở hữu bởi OS/DWM — mục 6) hoặc
    /// <c>null</c> nếu timeout (không có thay đổi màn hình — hành vi bình thường, không phải lỗi).
    /// Caller phải gọi <see cref="ReleaseFrame"/> ngay sau khi copy xong (ADR-42).
    /// </summary>
    public IDisposable? AcquireNextFrame(int adapterIndex, int outputIndex, uint timeoutMs)
    {
        EnsureDuplication(adapterIndex, outputIndex);

        Result result = _duplication!.AcquireNextFrame(timeoutMs, out OutduplFrameInfo _, out IDXGIResource? resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return null;
        }

        if (result == Vortice.DXGI.ResultCode.AccessLost)
        {
            // Đổi độ phân giải/khoá màn hình/secure desktop (mục 4.1) — không phải lỗi quyền.
            _duplication.Dispose();
            _duplication = null;
            return null;
        }

        result.CheckError();
        using (resource)
        {
            return resource!.QueryInterface<ID3D11Texture2D>();
        }
    }

    public void ReleaseFrame()
    {
        try
        {
            _duplication?.ReleaseFrame();
        }
        catch (SharpGenException)
        {
            // Có thể đã bị Dispose do AccessLost ở lần AcquireNextFrame vừa rồi — vô hại, bỏ qua.
        }
    }

    public ID3D11Device Device => _device;

    private void EnsureDuplication(int adapterIndex, int outputIndex)
    {
        if (_duplication is not null && _currentAdapterIndex == adapterIndex && _currentOutputIndex == outputIndex)
        {
            return;
        }

        _duplication?.Dispose();
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        if (!factory.EnumAdapters1((uint)adapterIndex, out IDXGIAdapter1? adapter).Success)
        {
            throw new CaptureInitializationException($"EnumAdapters1({adapterIndex}) failed.", new InvalidOperationException());
        }

        using (adapter)
        {
            if (!adapter!.EnumOutputs((uint)outputIndex, out IDXGIOutput? output).Success)
            {
                throw new CaptureInitializationException($"EnumOutputs({outputIndex}) failed.", new InvalidOperationException());
            }

            using (output)
            using (IDXGIOutput1 output1 = output!.QueryInterface<IDXGIOutput1>())
            {
                try
                {
                    _duplication = output1.DuplicateOutput(_device);
                }
                catch (SharpGenException ex)
                {
                    throw new CaptureInitializationException("IDXGIOutput1.DuplicateOutput failed.", ex);
                }
            }
        }

        _currentAdapterIndex = adapterIndex;
        _currentOutputIndex = outputIndex;
    }

    public void Dispose()
    {
        _duplication?.Dispose();
        _device.Dispose();
    }
}

/// <summary>Architecture/05 mục 8.1 bước 3: bất kỳ lỗi nào ở lần khởi tạo Desktop Duplication đầu tiên đều coi là "có thể do quyền".</summary>
public sealed class CaptureInitializationException(string message, Exception innerException) : Exception(message, innerException);
