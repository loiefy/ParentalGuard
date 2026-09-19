namespace ParentalGuard.Vision.Pipeline;

/// <summary>Architecture/05-image-pipeline-architecture.md mục 8.2 — bảng mã thoát Service dùng để phân biệt loại thất bại.</summary>
public static class VisionExitCodes
{
    /// <summary>Khởi tạo Desktop Duplication API thất bại ở lần thử đầu (nghi ngờ do Integrity Level) — Service respawn ở Medium IL.</summary>
    public const int CaptureInitAccessDenied = 17;

    /// <summary>
    /// Checksum <c>.onnx</c> không khớp (<c>MISC-090</c>) — dành sẵn theo Architecture/05 mục 7/8.2.
    /// CHƯA được enforce ở Đợt 1 (MISC-090 thuộc phạm vi Đợt 8 theo ROADMAP.md mục 3) — giữ hằng số
    /// để tránh xung đột số hiệu khi bật gate này sau, không dùng ở code Đợt 1.
    /// </summary>
    public const int ModelIntegrityCheckFailed = 18;

    /// <summary>
    /// <c>CaptureLoopWorker</c> gặp <see cref="Exception"/> liên tiếp vượt ngưỡng
    /// (<c>_maxConsecutiveFailures</c>) khi gọi <c>FrameClassificationPipeline.Process</c> — tự
    /// thoát có kiểm soát (fail-secure: nghiêng về phía để Service respawn tiến trình sạch) thay vì
    /// tiếp tục vòng lặp với pipeline nghi ngờ hỏng liên tục, hoặc để exception bay lên crash silent.
    /// </summary>
    public const int PipelineRepeatedFailure = 19;
}
