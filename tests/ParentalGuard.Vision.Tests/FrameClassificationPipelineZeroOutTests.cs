using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Regression guard cho bug 2026-09-18 (security-privacy-auditor FAIL TEST-001): trước đây nhiều
/// finally tách rời theo bước bỏ sót zero-out khi exception xảy ra giữa chừng — buffer/tensor dirty
/// bay ra ngoài <c>Process</c>/<c>ProcessFrame</c>. Test dùng seam <see cref="IWindowCropper"/>/
/// <see cref="INsfwClassifier"/> để throw có kiểm soát tại đúng các điểm nghi ngờ, không cần
/// DWM/DXGI/GPU thật (<see cref="IFrameCapture"/> cũng fake, tham số frame gõ <see cref="IDisposable"/>
/// nên test này không cần phụ thuộc <c>Vortice.Direct3D11</c>).
/// </summary>
public class FrameClassificationPipelineZeroOutTests
{
    private static WindowRect SmallRect() => new(X: 0, Y: 0, Width: 2, Height: 2);

    private sealed class FakeFrame : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public void ProcessFrame_CropperThrowsAfterPartialWrite_PixelBufferAndTensorAreFullyZeroed()
    {
        var auditor = new RecordingFrameBufferAuditor();
        var cropper = new ThrowingAfterPartialWriteCropper();
        var classifier = new NotCalledClassifier();
        var pipeline = new FrameClassificationPipeline(classifier, auditor);

        Assert.Throws<InvalidOperationException>(() =>
            pipeline.ProcessFrame(new NoOpFrameCapture(), cropper, SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 0));

        Assert.True(cropper.WroteNonZeroData, "Precondition: cropper phải ghi dữ liệu thật trước khi throw để test có ý nghĩa.");
        Assert.All(pipeline.PixelBufferForTest, b => Assert.Equal(0, b));
        Assert.All(pipeline.InputTensorForTest.Buffer.Span.ToArray(), f => Assert.Equal(0f, f));
        Assert.Equal(1, auditor.ZeroedCount("pixel_buffer_bgra8"));
        Assert.Equal(1, auditor.ZeroedCount("input_tensor"));
    }

    [Fact]
    public void ProcessFrame_ClassifierThrowsAfterRealResize_InputTensorIsFullyZeroed()
    {
        var auditor = new RecordingFrameBufferAuditor();
        var cropper = new SucceedingCropper();
        var classifier = new ThrowingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier, auditor);

        Assert.Throws<InvalidOperationException>(() =>
            pipeline.ProcessFrame(new NoOpFrameCapture(), cropper, SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 0));

        // Resize thật đã chạy (classifier throw ở bước sau) nên tensor chắc chắn từng dirty trước finally.
        Assert.All(pipeline.PixelBufferForTest, b => Assert.Equal(0, b));
        Assert.All(pipeline.InputTensorForTest.Buffer.Span.ToArray(), f => Assert.Equal(0f, f));
    }

    [Fact]
    public void ProcessFrame_SuccessPath_StillZeroesBothBuffersExactlyOnce()
    {
        var auditor = new RecordingFrameBufferAuditor();
        var cropper = new SucceedingCropper();
        var classifier = new SucceedingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier, auditor);

        pipeline.ProcessFrame(new NoOpFrameCapture(), cropper, SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 0);

        Assert.All(pipeline.PixelBufferForTest, b => Assert.Equal(0, b));
        Assert.All(pipeline.InputTensorForTest.Buffer.Span.ToArray(), f => Assert.Equal(0f, f));
        Assert.Equal(1, auditor.AllocatedCount("input_tensor"));
        Assert.Equal(1, auditor.AllocatedCount("pixel_buffer_bgra8"));
        Assert.Equal(1, auditor.ZeroedCount("pixel_buffer_bgra8"));
        Assert.Equal(1, auditor.ZeroedCount("input_tensor"));
    }

    /// <summary>
    /// Multi-window trong 1 chu kỳ capture (Architecture/05 mục 3.5, ADR-64 — `CaptureLoopWorker`
    /// gọi <c>Process</c>/<c>ProcessFrame</c> TUẦN TỰ cho từng candidate, dùng chung 1
    /// <see cref="FrameClassificationPipeline"/>/<c>_pixelBuffer</c>/<c>_inputTensor</c>, ADR-43): buffer của
    /// cửa sổ thứ i phải zero xong TRƯỚC KHI cửa sổ thứ i+1 bắt đầu ghi — không có 2 buffer dirty
    /// cùng lúc trong bộ nhớ.
    /// </summary>
    [Fact]
    public void ProcessFrame_TwoSequentialWindowsSameCycle_SecondWindowSeesFullyZeroedBufferFromFirst()
    {
        var auditor = new RecordingFrameBufferAuditor();
        var classifier = new SucceedingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier, auditor);
        var firstWindowCropper = new SucceedingCropper();
        var secondWindowCropper = new AssertsDestinationStartsZeroCropper();

        pipeline.ProcessFrame(new NoOpFrameCapture(), firstWindowCropper, SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 0);
        pipeline.ProcessFrame(new NoOpFrameCapture(), secondWindowCropper, SmallRect(), new FakeFrame(), hwnd: 2, outputIndex: 1, frameId: 2, capturedAtUnixMs: 0);

        Assert.True(secondWindowCropper.SawZeroedBufferOnEntry, "Buffer cửa sổ 1 phải zero xong trước khi cửa sổ 2 ghi vào.");
        Assert.All(pipeline.PixelBufferForTest, b => Assert.Equal(0, b));
        Assert.All(pipeline.InputTensorForTest.Buffer.Span.ToArray(), f => Assert.Equal(0f, f));
        Assert.Equal(2, auditor.ZeroedCount("pixel_buffer_bgra8"));
        Assert.Equal(2, auditor.ZeroedCount("input_tensor"));
    }

    private sealed class AssertsDestinationStartsZeroCropper : IWindowCropper
    {
        public bool SawZeroedBufferOnEntry { get; private set; }

        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
        {
            SawZeroedBufferOnEntry = destinationBgra8.All(b => b == 0);
            for (int i = 0; i < destinationBgra8.Length; i++)
            {
                destinationBgra8[i] = (byte)((i * 53) % 256);
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoOpFrameCapture : IFrameCapture
    {
        public IDisposable? AcquireNextFrame(int adapterIndex, int outputIndex, uint timeoutMs) => null;

        public void ReleaseFrame()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingAfterPartialWriteCropper : IWindowCropper
    {
        public bool WroteNonZeroData { get; private set; }

        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
        {
            // Ghi 1 phần dữ liệu giả (giống hành vi CopyTo dòng ảnh thật của GpuWindowCropper)
            // TRƯỚC KHI throw — mô phỏng đúng bug đã tìm thấy: exception giữa vòng lặp copy.
            destinationBgra8[0] = 200;
            destinationBgra8[1] = 150;
            WroteNonZeroData = true;
            throw new InvalidOperationException("Simulated GPU copy failure mid-crop.");
        }

        public void Dispose()
        {
        }
    }

    private sealed class SucceedingCropper : IWindowCropper
    {
        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
        {
            for (int i = 0; i < destinationBgra8.Length; i++)
            {
                destinationBgra8[i] = (byte)((i * 37) % 256);
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class NotCalledClassifier : INsfwClassifier
    {
        public TensorLayout InputLayout => TensorLayout.Nhwc;

        public NsfwClassProbabilities Classify(DenseTensor<float> input) =>
            throw new InvalidOperationException("Classify should not be reached — cropper already threw.");

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingClassifier : INsfwClassifier
    {
        public TensorLayout InputLayout => TensorLayout.Nhwc;

        public NsfwClassProbabilities Classify(DenseTensor<float> input) =>
            throw new InvalidOperationException("Simulated inference session failure.");

        public void Dispose()
        {
        }
    }

    private sealed class SucceedingClassifier : INsfwClassifier
    {
        public TensorLayout InputLayout => TensorLayout.Nhwc;

        public NsfwClassProbabilities Classify(DenseTensor<float> input) => new(0.2f, 0.2f, 0.2f, 0.2f, 0.2f);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingFrameBufferAuditor : IFrameBufferAuditor
    {
        private readonly Dictionary<string, int> _allocated = [];
        private readonly Dictionary<string, int> _zeroed = [];

        public void OnBufferAllocated(string bufferId, int sizeBytes) =>
            _allocated[bufferId] = _allocated.GetValueOrDefault(bufferId) + 1;

        public void OnZeroed(string bufferId, int sizeBytes) =>
            _zeroed[bufferId] = _zeroed.GetValueOrDefault(bufferId) + 1;

        public int AllocatedCount(string bufferId) => _allocated.GetValueOrDefault(bufferId);

        public int ZeroedCount(string bufferId) => _zeroed.GetValueOrDefault(bufferId);
    }
}
