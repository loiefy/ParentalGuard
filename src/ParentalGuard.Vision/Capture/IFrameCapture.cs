namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Seam cho <see cref="DesktopDuplicationCapture"/> — cho phép <c>FrameClassificationPipeline</c>
/// nhận qua constructor injection, để test bất biến zero-out (IMG-003/040) không cần Desktop
/// Duplication API/GPU thật. Trả về <see cref="IDisposable"/> (không phải <c>ID3D11Texture2D</c>)
/// để interface này — và test dùng nó — không cần phụ thuộc <c>Vortice.Direct3D11</c>.
/// </summary>
public interface IFrameCapture : IDisposable
{
    IDisposable? AcquireNextFrame(int adapterIndex, int outputIndex, uint timeoutMs);

    void ReleaseFrame();
}
