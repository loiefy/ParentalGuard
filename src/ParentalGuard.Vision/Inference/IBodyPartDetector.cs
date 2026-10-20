using Microsoft.ML.OnnxRuntime.Tensors;

namespace ParentalGuard.Vision.Inference;

/// <summary>
/// `IMG-016c` (Specification/09 v0.11.0): mô hình phát hiện vật thể dùng để XÁC MINH lại vùng mà <see cref="INsfwClassifier"/>
/// đã chấm vượt ngưỡng chặn — chỉ giữ kết quả vi phạm khi thật sự tìm thấy bộ phận cơ thể nhạy cảm lộ rõ trong vùng đó.
/// Tách interface như <see cref="INsfwClassifier"/> để test pipeline không cần file <c>.onnx</c> thật.
/// </summary>
public interface IBodyPartDetector : IDisposable
{
    /// <summary>Cạnh ảnh vuông đầu vào (640 với NudeNet 640m) — input luôn NCHW, RGB [0, 1], letterbox góc trên-trái.</summary>
    int InputSize { get; }

    BodyPartDetectionResult Detect(DenseTensor<float> input);
}

/// <summary>1 vật thể NudeNet nhận ra — khung tính theo pixel của ảnh đầu vào detector (letterbox, gốc trên-trái).</summary>
public readonly record struct BodyPartDetection(string Label, bool IsExposed, float Confidence, float X, float Y, float Width, float Height);

/// <param name="MaxExposedConfidence">Độ tin cậy cao nhất (0–1) của các nhãn bộ phận nhạy cảm lộ rõ; 0 nếu không có.</param>
/// <param name="Detections">Vật thể từ mức <see cref="NudeNetDetector.MinReportedConfidence"/> sau NMS — chỉ để hiển thị ở chế độ developer (`DEV-051a`).</param>
public readonly record struct BodyPartDetectionResult(float MaxExposedConfidence, IReadOnlyList<BodyPartDetection> Detections);
