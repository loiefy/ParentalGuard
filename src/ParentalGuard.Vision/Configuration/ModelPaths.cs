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
#if PARENTALGUARD_MODEL_MARQO
    private const string _modelFileName = "nsfw_marqo_384.onnx";
#elif PARENTALGUARD_MODEL_FALCONSAI
    private const string _modelFileName = "nsfw_falconsai_224.onnx";
#else
    private const string _modelFileName = "nsfw_model.onnx";
#endif

    public static string ModelDirectory => Environment.GetEnvironmentVariable(_modelDirEnvVar)
        ?? Path.Combine(AppContext.BaseDirectory, "models");

    public static string OnnxModelPath => Path.Combine(ModelDirectory, _modelFileName);
}
