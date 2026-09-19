namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Seam cho <see cref="GpuWindowCropper"/> — cho phép <c>FrameClassificationPipeline</c> nhận qua
/// constructor injection, để test được bằng fake cropper (throw giữa chừng sau khi ghi 1 phần dữ
/// liệu) mà không cần GPU thật (IMG-040/041 regression guard). <paramref name="fullScreenFrame"/>
/// gõ kiểu <see cref="IDisposable"/> (không phải <c>ID3D11Texture2D</c>) để interface này — và test
/// dùng nó — không cần phụ thuộc <c>Vortice.Direct3D11</c>; implementation thật tự ép kiểu lại.
/// </summary>
public interface IWindowCropper : IDisposable
{
    void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8);
}
