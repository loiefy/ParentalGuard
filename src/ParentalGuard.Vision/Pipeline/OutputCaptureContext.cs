using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Architecture/05 mục 3.5 (`BE-082`, ADR-65): capture + cropper riêng cho 1 <c>IDXGIOutput</c>, sở
/// hữu <see cref="IFrameCapture"/>/<see cref="IWindowCropper"/> của đúng output đó — tạo lazy
/// (<see cref="Pipeline.CaptureLoopWorker"/>) khi có candidate window trên output, dispose sau
/// <c>_idleDisposeThreshold</c> chu kỳ liên tiếp không dùng tới.
/// </summary>
public sealed class OutputCaptureContext(IFrameCapture capture, IWindowCropper cropper) : IDisposable
{
    public IFrameCapture Capture { get; } = capture;

    public IWindowCropper Cropper { get; } = cropper;

    /// <summary>Số chu kỳ liên tiếp không có candidate nào trên output này — reset về 0 mỗi lần dùng.</summary>
    public int IdleCycles { get; set; }

    public void Dispose()
    {
        Cropper.Dispose();
        Capture.Dispose();
    }
}
