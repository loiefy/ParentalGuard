using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// `IMG-016` (2026-10-06): risk score = max(cả cửa sổ, 5 vùng con) — nội dung vi phạm chỉ chiếm 1 phần cửa sổ (video dọc giữa
/// trang web) không bị "pha loãng". Đo trên máy chủ dự án: chấm cả cửa sổ chỉ bắt 0–28% khung video khiêu dâm ở ngưỡng 70%.
/// </summary>
public class SubRegionScoringTests
{
    private const int W = 100;
    private const int H = 100;

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

    /// <summary>Ảnh nền xám, 1 khối "nội dung" sáng ở góc dưới-phải (30×30).</summary>
    private sealed class BrightCornerCropper : IWindowCropper
    {
        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    byte v = (byte)(x >= 70 && y >= 70 ? 250 : 40 + ((x + y) % 7));
                    int o = ((y * W) + x) * 4;
                    destinationBgra8[o] = destinationBgra8[o + 1] = destinationBgra8[o + 2] = v;
                    destinationBgra8[o + 3] = 255;
                }
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Điểm = tỉ lệ pixel sáng trong vùng đưa vào (giả lập model: nội dung càng chiếm nhiều khung, điểm càng cao).</summary>
    private sealed class BrightFractionClassifier : INsfwClassifier
    {
        public int Calls { get; private set; }

        public TensorLayout InputLayout => TensorLayout.Nhwc;

        public NsfwClassProbabilities Classify(DenseTensor<float> input)
        {
            Calls++;
            Span<float> s = input.Buffer.Span;
            int bright = 0;
            for (int i = 0; i < s.Length; i += 3)
            {
                if (s[i] > 0.9f)
                {
                    bright++;
                }
            }

            float fraction = bright / (s.Length / 3f);
            return new NsfwClassProbabilities(0f, 0f, 1f - fraction, fraction, 0f);
        }

        public void Dispose()
        {
        }
    }

    private static VisionInferenceResult Run(BrightFractionClassifier classifier, float gate) =>
        new FrameClassificationPipeline(classifier, subRegionGate: gate)
            .ProcessFrame(new NoOpFrameCapture(), new BrightCornerCropper(), new WindowRect(0, 0, W, H), new FakeFrame(), hwnd: 1, outputIndex: 0, frameId: 1, capturedAtUnixMs: 1)!;

    [Fact]
    public void ContentInCorner_SubRegionScoreRaisesRisk_AboveWholeWindowScore()
    {
        var wholeOnly = new BrightFractionClassifier();
        float whole = Run(wholeOnly, float.PositiveInfinity).RiskScore;

        var withRegions = new BrightFractionClassifier();
        float best = Run(withRegions, gate: 0f).RiskScore;

        Assert.Equal(1, wholeOnly.Calls);
        Assert.Equal(6, withRegions.Calls); // cả cửa sổ + 5 vùng con
        Assert.True(best > whole * 2, $"whole={whole} best={best}");
    }

    [Fact]
    public void WholeScoreBelowGate_SkipsSubRegions()
    {
        var classifier = new BrightFractionClassifier();

        Run(classifier, gate: 0.99f);

        Assert.Equal(1, classifier.Calls);
    }

    [Fact]
    public void SubRegions_AreFiveRegionsInsideWindow_CoveringEveryCorner()
    {
        IReadOnlyList<WindowRect> regions = FrameClassificationPipeline.SubRegions(1000, 500);

        Assert.Equal(5, regions.Count);
        Assert.All(regions, r => Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.Width <= 1000 && r.Y + r.Height <= 500));
        Assert.Contains(regions, r => r.X == 0 && r.Y == 0);
        Assert.Contains(regions, r => r.X + r.Width == 1000 && r.Y + r.Height == 500);
    }
}
