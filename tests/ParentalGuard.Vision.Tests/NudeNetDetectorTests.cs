using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.ModelIntegrity;

namespace ParentalGuard.Vision.Tests;

/// <summary>`IMG-016c`: giải mã output NudeNet (<c>[4 + 18, anchors]</c>) và tiền xử lý letterbox — không cần file <c>.onnx</c> thật.</summary>
public class NudeNetDetectorTests
{
    private const int Anchors = 3;
    private static readonly int _classes = NudeNetDetector.Labels.Length;

    private static float[] Output(params (int Anchor, string Label, float Score)[] scores)
    {
        var output = new float[(4 + _classes) * Anchors];
        foreach ((int anchor, string label, float score) in scores)
        {
            output[((4 + Array.IndexOf(NudeNetDetector.Labels, label)) * Anchors) + anchor] = score;
        }

        return output;
    }

    [Fact]
    public void Labels_Has18Classes_AndEveryExposedLabelExists()
    {
        Assert.Equal(18, _classes);
        Assert.All(NudeNetDetector.ExposedLabels, label => Assert.Contains(label, NudeNetDetector.Labels));
    }

    [Fact]
    public void NoAnchors_ReturnsZero()
    {
        Assert.Equal(0f, NudeNetDetector.MaxExposedConfidence(Output(), _classes));
    }

    [Fact]
    public void ReturnsHighestExposedScore_AcrossAnchors()
    {
        float[] output = Output((0, "FEMALE_BREAST_EXPOSED", 0.4f), (2, "MALE_GENITALIA_EXPOSED", 0.7f), (1, "FACE_FEMALE", 0.95f));

        Assert.Equal(0.7f, NudeNetDetector.MaxExposedConfidence(output, _classes), 5);
    }

    [Fact]
    public void NonSensitiveLabels_AreIgnored()
    {
        float[] output = Output((0, "FACE_MALE", 0.9f), (1, "FEET_EXPOSED", 0.8f), (2, "BELLY_EXPOSED", 0.8f));

        Assert.Equal(0f, NudeNetDetector.MaxExposedConfidence(output, _classes));
    }

    /// <summary>Như NudeNet: anchor mang nhãn điểm cao nhất của nó — "ngực có áo" thắng thì không tính là lộ.</summary>
    [Fact]
    public void AnchorWhoseTopLabelIsCovered_IsNotExposed()
    {
        float[] output = Output((0, "FEMALE_BREAST_COVERED", 0.6f), (0, "FEMALE_BREAST_EXPOSED", 0.5f));

        Assert.Equal(0f, NudeNetDetector.MaxExposedConfidence(output, _classes));
    }

    [Fact]
    public void BoxChannels_AreNotReadAsScores()
    {
        float[] output = Output();
        Array.Fill(output, 1f, 0, 4 * Anchors);

        Assert.Equal(0f, NudeNetDetector.MaxExposedConfidence(output, _classes));
    }

    private static void SetBox(float[] output, int anchor, float cx, float cy, float w, float h)
    {
        output[anchor] = cx;
        output[Anchors + anchor] = cy;
        output[(2 * Anchors) + anchor] = w;
        output[(3 * Anchors) + anchor] = h;
    }

    [Fact]
    public void Decode_ConvertsCenterBoxToTopLeft_AndKeepsLabels()
    {
        float[] output = Output((0, "FACE_FEMALE", 0.83f), (1, "BUTTOCKS_EXPOSED", 0.6f));
        SetBox(output, 0, 100, 100, 40, 20);
        SetBox(output, 1, 300, 300, 50, 50);

        IReadOnlyList<BodyPartDetection> found = NudeNetDetector.Decode(output, _classes, 0.25f);

        Assert.Equal(2, found.Count);
        Assert.Equal(new BodyPartDetection("FACE_FEMALE", false, 0.83f, 80, 90, 40, 20), found[0]);
        Assert.True(found[1].IsExposed);
    }

    [Fact]
    public void Decode_DropsBelowMinConfidence()
    {
        float[] output = Output((0, "FEMALE_BREAST_EXPOSED", 0.2f));
        SetBox(output, 0, 10, 10, 5, 5);

        Assert.Empty(NudeNetDetector.Decode(output, _classes, 0.25f));
    }

    /// <summary>NMS IoU 0.45 như nudenet.py: 2 khung gần trùng → giữ khung điểm cao hơn; khung tách rời giữ cả 2.</summary>
    [Fact]
    public void Decode_SuppressesOverlappingBoxes_KeepingHigherScore()
    {
        float[] output = Output((0, "FEMALE_BREAST_EXPOSED", 0.5f), (1, "FEMALE_BREAST_EXPOSED", 0.9f), (2, "FACE_FEMALE", 0.4f));
        SetBox(output, 0, 100, 100, 40, 40);
        SetBox(output, 1, 102, 101, 40, 40);
        SetBox(output, 2, 400, 400, 40, 40);

        IReadOnlyList<BodyPartDetection> found = NudeNetDetector.Decode(output, _classes, 0.25f);

        Assert.Equal([0.9f, 0.4f], found.Select(d => d.Confidence));
    }

    [Theory]
    [InlineData(100, 500, false)]
    [InlineData(500, 100, false)]
    [InlineData(101, 101, true)]
    [InlineData(1920, 1080, true)]
    public void BE071d_WindowsWithASideOf100PxOrLess_AreNotAnalyzed(int width, int height, bool analyzed)
    {
        Assert.Equal(analyzed, CandidateWindowChecks.IsLargeEnoughToAnalyze(new WindowRect(0, 0, width, height)));
    }

    [Fact]
    public void BE071d_UnknownRect_IsNotAnalyzed()
    {
        Assert.False(CandidateWindowChecks.IsLargeEnoughToAnalyze(null));
    }

    [Fact]
    public void MalformedOutputLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => NudeNetDetector.MaxExposedConfidence(new float[23], _classes));
    }

    [Fact]
    public void DetectorSha256Hex_Is64LowercaseHexCharacters()
    {
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), ExpectedModelChecksum.DetectorSha256Hex);
    }

    /// <summary>Vùng 4×2 vào tensor 8×8: giữ tỉ lệ (nửa trên là ảnh, nửa dưới đệm đen), RGB từ BGRA, chia 255.</summary>
    [Fact]
    public void ResizeLetterbox_KeepsAspectRatio_PadsBottomRightWithBlack()
    {
        const int sw = 4;
        const int sh = 2;
        var source = new byte[sw * sh * 4];
        for (int i = 0; i < sw * sh; i++)
        {
            source[i * 4] = 51;      // B
            source[(i * 4) + 1] = 102; // G
            source[(i * 4) + 2] = 255; // R
            source[(i * 4) + 3] = 255;
        }

        var tensor = new DenseTensor<float>([1, 3, 8, 8]);
        FrameResizerNormalizer.ResizeLetterbox(source, sw, sh, new WindowRect(0, 0, sw, sh), tensor);

        Assert.Equal(1f, tensor[0, 0, 0, 0], 4);       // R
        Assert.Equal(0.4f, tensor[0, 1, 3, 7], 4);     // G, hàng cuối của phần ảnh
        Assert.Equal(0.2f, tensor[0, 2, 0, 3], 4);     // B
        Assert.Equal(0f, tensor[0, 0, 4, 0]);          // phần đệm
        Assert.Equal(0f, tensor[0, 1, 7, 7]);
    }

    [Fact]
    public void ResizeLetterbox_TallRegion_PadsRight()
    {
        const int sw = 2;
        const int sh = 4;
        byte[] source = Enumerable.Repeat((byte)255, sw * sh * 4).ToArray();

        var tensor = new DenseTensor<float>([1, 3, 8, 8]);
        FrameResizerNormalizer.ResizeLetterbox(source, sw, sh, new WindowRect(0, 0, sw, sh), tensor);

        Assert.Equal(1f, tensor[0, 0, 7, 3], 4);
        Assert.Equal(0f, tensor[0, 0, 0, 4]);
    }
}
