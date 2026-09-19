namespace ParentalGuard.Vision.Configuration;

/// <summary>
/// Đường dẫn cấu hình được cho file <c>.onnx</c> đã convert từ <c>GantMan/nsfw_model</c> (`IMG-014`).
/// Vision bị chặn network tuyệt đối (`SEC-016`-`018`) nên KHÔNG có cơ chế tự tải model lúc chạy —
/// file phải được đóng gói sẵn cùng bản cài đặt, đường dẫn có thể override qua biến môi trường cho
/// mục đích dev/test cục bộ (không phải kênh network, không vi phạm ranh giới đó).
/// </summary>
public static class ModelPaths
{
    private const string _modelDirEnvVar = "PARENTALGUARD_MODEL_DIR";
    private const string _modelFileName = "nsfw_model.onnx";

    public static string ModelDirectory => Environment.GetEnvironmentVariable(_modelDirEnvVar)
        ?? Path.Combine(AppContext.BaseDirectory, "models");

    public static string OnnxModelPath => Path.Combine(ModelDirectory, _modelFileName);
}
