using Microsoft.ML.OnnxRuntime.Tensors;

namespace ParentalGuard.Vision.Inference;

/// <summary>
/// Bước 4 (Architecture/05 mục 4.3): che giấu <c>InferenceSession</c> ONNX Runtime thật sau 1
/// interface — cho phép unit test <see cref="RiskScoreAggregator"/>/pipeline bằng fake classifier,
/// không phụ thuộc file <c>.onnx</c> thật (không có sẵn trong repo/CI, Vision bị chặn network nên
/// không thể tự tải lúc test).
/// </summary>
public interface INsfwClassifier : IDisposable
{
    /// <summary>Layout tensor input mà classifier này kỳ vọng — pipeline dựng <see cref="DenseTensor{T}"/> đúng theo đây (ADR-48).</summary>
    TensorLayout InputLayout { get; }

    NsfwClassProbabilities Classify(DenseTensor<float> input);
}
