namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Snapshot immutable của <c>ControlVisionCommand</c> hiện hành (Architecture/05-image-pipeline-architecture.md
/// mục 3.3) — cập nhật bằng cách swap nguyên 1 tham chiếu mới (lock-free), <see cref="CaptureLoopWorker"/>
/// chỉ đọc <see cref="VisionRuntimeConfigHolder.Current"/> ở đầu mỗi vòng lặp.
/// </summary>
public sealed record VisionRuntimeConfig(
    bool MonitoringEnabled,
    int CaptureIntervalMs,
    float RiskThreshold,
    IReadOnlyList<string> ExcludeProcessNames)
{
    /// <summary>
    /// `BE-034b`: cửa sổ đang bị overlay che (<c>ControlVisionCommand.covered_window_handles</c>) —
    /// không capture/phân loại (chỉ còn thấy chính overlay; Service cũng bỏ qua kết quả của chúng).
    /// </summary>
    public IReadOnlySet<ulong> CoveredWindowHandles { get; init; } = new HashSet<ulong>();

    public static VisionRuntimeConfig CreateDefault() => new(
        MonitoringEnabled: false,
        CaptureIntervalMs: 1000,
        RiskThreshold: 0.7f,
        ExcludeProcessNames: []);
}

/// <summary>Giữ tham chiếu <see cref="VisionRuntimeConfig"/> hiện hành, thread-safe qua <c>volatile</c>.</summary>
public sealed class VisionRuntimeConfigHolder
{
    private volatile VisionRuntimeConfig _current = VisionRuntimeConfig.CreateDefault();

    public VisionRuntimeConfig Current => _current;

    public void Update(VisionRuntimeConfig config) => _current = config;
}
