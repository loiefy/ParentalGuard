using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// `IMG-016c` (Specification/09 v0.11.0): Marqo báo vi phạm → NudeNet xác minh; không tìm thấy bộ phận nhạy cảm lộ rõ thì hạ điểm
/// dưới ngưỡng chặn. Bối cảnh: trang đăng nhập Teams 2026-10-20 — Marqo chấm tới 0.94 trên hình 3D, NudeNet cao nhất 0.004.
/// </summary>
public class BodyPartVerificationTests
{
    private const int W = 64;
    private const int H = 48;

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

    /// <summary>Ảnh xám (không có màu → `IMG-016b` không can thiệp), nội dung đổi theo <see cref="Seed"/>.</summary>
    private sealed class GrayCropper : IWindowCropper
    {
        public byte Seed { get; set; } = 40;

        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
        {
            for (int i = 0; i < W * H; i++)
            {
                byte v = (byte)(Seed + ((i * 7) % 50));
                destinationBgra8[i * 4] = destinationBgra8[(i * 4) + 1] = destinationBgra8[(i * 4) + 2] = v;
                destinationBgra8[(i * 4) + 3] = 255;
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedClassifier(float score) : INsfwClassifier
    {
        public TensorLayout InputLayout => TensorLayout.Nchw;

        public NsfwClassProbabilities Classify(DenseTensor<float> input) => new(0f, 0f, 1f - score, score, 0f);

        public void Dispose()
        {
        }
    }

    private sealed class FakeDetector(float confidence) : IBodyPartDetector
    {
        public int Calls { get; private set; }

        public bool SawNonZeroInput { get; private set; }

        public int InputSize => 32;

        public float MaxExposedConfidence(DenseTensor<float> input)
        {
            Calls++;
            SawNonZeroInput |= input.Buffer.Span.IndexOfAnyExcept(0f) >= 0;
            return confidence;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingAuditor : IFrameBufferAuditor
    {
        public List<string> Zeroed { get; } = [];

        public void OnBufferAllocated(string bufferId, int sizeBytes)
        {
        }

        public void OnZeroed(string bufferId, int sizeBytes) => Zeroed.Add(bufferId);
    }

    private static float Run(FrameClassificationPipeline pipeline, GrayCropper? cropper = null) =>
        pipeline.ProcessFrame(new NoOpFrameCapture(), cropper ?? new GrayCropper(), new WindowRect(0, 0, W, H), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 1, riskThreshold: 0.7f)!.RiskScore;

    [Fact]
    public void MarqoFlags_DetectorFindsNothing_ScoreDroppedBelowThreshold()
    {
        var detector = new FakeDetector(0.004f);

        float score = Run(new FrameClassificationPipeline(new FixedClassifier(0.94f), detectContentRegion: false, detector: detector));

        Assert.Equal(0.7f * FrameClassificationPipeline.RejectedScoreFactor, score, 5);
        Assert.True(score < 0.7f);
        Assert.Equal(FrameClassificationPipeline.MaxVerifiedRegions, detector.Calls);
        Assert.True(detector.SawNonZeroInput);
    }

    [Fact]
    public void MarqoFlags_DetectorConfirms_ScoreKept()
    {
        var detector = new FakeDetector(0.6f);

        float score = Run(new FrameClassificationPipeline(new FixedClassifier(0.94f), detectContentRegion: false, detector: detector));

        Assert.Equal(0.94f, score, 5);
        Assert.Equal(1, detector.Calls); // dừng ngay ở vùng đầu tiên xác nhận được
    }

    [Fact]
    public void ConfidenceJustBelowThreshold_IsNotAConfirmation()
    {
        var detector = new FakeDetector(FrameClassificationPipeline.ExposedConfidenceThreshold - 0.01f);

        float score = Run(new FrameClassificationPipeline(new FixedClassifier(0.94f), detectContentRegion: false, detector: detector));

        Assert.True(score < 0.7f);
    }

    [Fact]
    public void ScoreBelowThreshold_DetectorNotRun()
    {
        var detector = new FakeDetector(1f);

        float score = Run(new FrameClassificationPipeline(new FixedClassifier(0.3f), detectContentRegion: false, detector: detector));

        Assert.Equal(0.3f, score, 5);
        Assert.Equal(0, detector.Calls);
    }

    /// <summary>File NudeNet thiếu → giữ nguyên kết quả Marqo (chặn nhiều hơn, không ít hơn).</summary>
    [Fact]
    public void NoDetector_MarqoScoreKept()
    {
        float score = Run(new FrameClassificationPipeline(new FixedClassifier(0.94f), detectContentRegion: false));

        Assert.Equal(0.94f, score, 5);
    }

    [Fact]
    public void UnchangedFrame_ReusesCachedVerdict_WithoutRunningDetectorAgain()
    {
        var detector = new FakeDetector(0f);
        var pipeline = new FrameClassificationPipeline(new FixedClassifier(0.94f), detectContentRegion: false, detector: detector);
        var cropper = new GrayCropper();

        float first = Run(pipeline, cropper);
        int callsAfterFirst = detector.Calls;
        float second = Run(pipeline, cropper);

        Assert.Equal(first, second);
        Assert.Equal(callsAfterFirst, detector.Calls);
    }

    /// <summary>IMG-003: tensor đầu vào detector được zero sau MỖI lần chạy.</summary>
    [Fact]
    public void DetectorInputTensor_ZeroedAfterEveryRun()
    {
        var auditor = new RecordingAuditor();
        var detector = new FakeDetector(0f);

        Run(new FrameClassificationPipeline(new FixedClassifier(0.94f), auditor, detectContentRegion: false, detector: detector));

        Assert.Equal(detector.Calls, auditor.Zeroed.Count(id => id == "detector_input_tensor"));
    }
}
