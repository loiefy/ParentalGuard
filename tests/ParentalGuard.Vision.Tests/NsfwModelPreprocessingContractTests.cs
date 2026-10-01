using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Regression bug 2026-09-30 (không phát hiện được nội dung vi phạm trên máy thật): tiền xử lý C#
/// phải khớp đúng hợp đồng đầu vào của CHÍNH file <c>models/nsfw_model.onnx</c> đóng gói — graph tự
/// chứa <c>hub_input/Mul(2.0)→Sub(1.0)</c>, tức mong đợi [0,1]. Giá trị kỳ vọng tính độc lập bằng
/// Python onnxruntime với input <c>pixel/255</c>; bản lỗi <c>(pixel/127.5)-1</c> cho kết quả khác hẳn
/// (drawing ≈ 0.20 thay vì ≈ 0.51 với ảnh xám 128). Dùng file model thật trong repo (đã track git)
/// — cố ý, vì đây là guard cho khớp nối giữa code và model, không test được bằng fake.
/// </summary>
public class NsfwModelPreprocessingContractTests
{
    [Fact]
    public void UniformGray128_ThroughProductionPreprocessing_MatchesReferenceModelOutput()
    {
        byte[] modelBytes = File.ReadAllBytes(FindModelPath());
        using var sessionOptions = new SessionOptions();
        using var classifier = new NsfwClassifier(modelBytes, sessionOptions);

        const int size = 64;
        byte[] grayBgra8 = new byte[size * size * 4];
        Array.Fill(grayBgra8, (byte)128);
        int[] shape = classifier.InputLayout == TensorLayout.Nhwc ? [1, 224, 224, 3] : [1, 3, 224, 224];
        var tensor = new DenseTensor<float>(shape);

        FrameResizerNormalizer.Resize(grayBgra8, size, size, tensor, classifier.InputLayout);
        NsfwClassProbabilities p = classifier.Classify(tensor);

        // Tham chiếu Python (pixel/255): [drawing 0.51, hentai 0.14, neutral 0.20, porn 0.08, sexy 0.07].
        Assert.Equal(0.51f, p.Drawing, 0.02f);
        Assert.Equal(0.20f, p.Neutral, 0.02f);
    }

    private static string FindModelPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "models", "nsfw_model.onnx");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("models/nsfw_model.onnx not found above test output directory.");
    }
}
