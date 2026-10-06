using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 3.7/3.8 (Đợt 7, PERF-010/011) — hash-gate
/// (dHash) quyết định skip resize+inference khi nội dung không đổi, tái dùng risk score cache,
/// luôn điền <c>ContentChanged</c>/<c>Bbox</c>/<c>CapturedAtUnixMs</c> bất kể skip hay không.
/// </summary>
public class FrameClassificationPipelineHashGateTests
{
    private static WindowRect SmallRect() => new(X: 10, Y: 20, Width: 2, Height: 2);

    private sealed class FakeFrame : IDisposable
    {
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

    private sealed class WritesPatternCropper(byte[] pattern) : IWindowCropper
    {
        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8) =>
            pattern.CopyTo(destinationBgra8, 0);

        public void Dispose()
        {
        }
    }

    private sealed class CountingClassifier : INsfwClassifier
    {
        public int CallCount { get; private set; }

        public TensorLayout InputLayout => TensorLayout.Nhwc;

        public NsfwClassProbabilities Classify(DenseTensor<float> input)
        {
            CallCount++;
            // Score đổi theo mỗi lần gọi — giúp test phát hiện rõ pipeline có tái dùng giá trị cũ hay không.
            float porn = 0.1f * CallCount;
            return new NsfwClassProbabilities(Drawing: 0f, Hentai: 0f, Neutral: 0f, Porn: porn, Sexy: 0f);
        }

        public void Dispose()
        {
        }
    }

    // 2x2 BGRA8 caro (không phải khối đồng nhất — ảnh đồng nhất luôn cho dHash=0 bất kể sáng/tối,
    // xem PerceptualHashTests.ComputeDHash64_BlackVersusWhiteImage...): (0,0)+(0,1) tối, (1,0)+(1,1)
    // sáng — đủ tương phản trái/phải để dHash phân biệt được 2 pattern đảo ngược nhau.
    private static byte[] DarkPattern() => [0, 0, 0, 255, /**/ 255, 255, 255, 255, /**/ 0, 0, 0, 255, /**/ 255, 255, 255, 255];

    private static byte[] LightPattern() => [255, 255, 255, 255, /**/ 0, 0, 0, 255, /**/ 255, 255, 255, 255, /**/ 0, 0, 0, 255];

    [Fact]
    public void ProcessFrame_SecondCallIdenticalPixels_SkipsClassifyAndReusesCachedRiskScore()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);
        byte[] pattern = DarkPattern();

        VisionInferenceResult first = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(pattern), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100)!;
        VisionInferenceResult second = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper((byte[])pattern.Clone()), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 2, capturedAtUnixMs: 200)!;

        Assert.True(first.ContentChanged);
        Assert.False(second.ContentChanged);
        Assert.Equal(1, classifier.CallCount);
        Assert.Equal(first.RiskScore, second.RiskScore);
    }

    [Fact]
    public void ProcessFrame_SkippedFrame_StillFillsBboxAndCapturedAtUnixMs()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);
        byte[] pattern = DarkPattern();
        pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(pattern), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100);

        VisionInferenceResult second = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper((byte[])pattern.Clone()), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 2, capturedAtUnixMs: 200)!;

        Assert.False(second.ContentChanged);
        Assert.Equal(200, second.CapturedAtUnixMs);
        Assert.Equal(10, second.Bbox.X);
        Assert.Equal(20, second.Bbox.Y);
    }

    [Fact]
    public void ProcessFrame_DrasticallyDifferentPixels_RunsClassifyAgain()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);

        VisionInferenceResult first = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(DarkPattern()), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100)!;
        VisionInferenceResult second = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(LightPattern()), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 2, capturedAtUnixMs: 200)!;

        Assert.True(first.ContentChanged);
        Assert.True(second.ContentChanged);
        Assert.Equal(2, classifier.CallCount);
        Assert.NotEqual(first.RiskScore, second.RiskScore);
    }

    [Fact]
    public void ProcessFrame_DifferentWindowHandle_IsIndependentFirstTime()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);
        byte[] pattern = DarkPattern();
        pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(pattern), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100);

        VisionInferenceResult otherWindow = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper((byte[])pattern.Clone()), SmallRect(), new FakeFrame(), hwnd: 2, outputIndex: 0, frameId: 2, capturedAtUnixMs: 200)!;

        Assert.True(otherWindow.ContentChanged);
        Assert.Equal(2, classifier.CallCount);
    }

    [Fact]
    public void EndCaptureCycle_FiveIdleCyclesForWindow_EvictsHash_NextCallIsTreatedAsFirstTime()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);
        byte[] pattern = DarkPattern();
        pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(pattern), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100);

        for (int i = 0; i < 5; i++)
        {
            pipeline.EndCaptureCycle(new HashSet<IntPtr>());
        }

        VisionInferenceResult afterEvict = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper((byte[])pattern.Clone()), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 2, capturedAtUnixMs: 300)!;

        Assert.True(afterEvict.ContentChanged);
        Assert.Equal(2, classifier.CallCount);
    }

    /// <summary>Bug 2026-10-06: khung Desktop Duplication chưa có nội dung (toàn 0) không được phân loại/gửi kết quả.</summary>
    [Fact]
    public void ProcessFrame_AllZeroCrop_ReturnsNull_DoesNotClassify()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);

        VisionInferenceResult? result = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(new byte[16]), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100);

        Assert.Null(result);
        Assert.Equal(0, classifier.CallCount);
    }

    /// <summary>Bug 2026-10-06: vùng bị cửa sổ khác che bị tô đen TRƯỚC hash — nội dung chỉ đổi ở vùng bị che không tính là "đổi".</summary>
    [Fact]
    public void ProcessFrame_ChangeOnlyInsideOccludedArea_IsTreatedAsUnchanged()
    {
        var classifier = new CountingClassifier();
        var pipeline = new FrameClassificationPipeline(classifier);
        WindowRect[] rightColumnCovered = [new WindowRect(1, 0, 1, 2)];
        byte[] first = DarkPattern();
        byte[] second = DarkPattern();
        second[4] = 10; // pixel (1,0) — nằm trong vùng bị che

        pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(first), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 100, occlusionMasks: rightColumnCovered);
        VisionInferenceResult again = pipeline.ProcessFrame(new NoOpFrameCapture(), new WritesPatternCropper(second), SmallRect(), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 2, capturedAtUnixMs: 200, occlusionMasks: rightColumnCovered)!;

        Assert.False(again.ContentChanged);
        Assert.Equal(1, classifier.CallCount);
    }
}
