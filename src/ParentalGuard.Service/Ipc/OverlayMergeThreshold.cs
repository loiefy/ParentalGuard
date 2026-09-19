namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Ngưỡng hysteresis chế độ overlay gộp (`BE-088`/`089`, Architecture/07-overlay-architecture.md
/// mục 3.2, ADR-57) — tách thành hàm thuần để test độc lập với <see cref="OverlayDecisionCoordinator"/>.
/// </summary>
public static class OverlayMergeThreshold
{
    /// <summary>`BE-088`: kích hoạt khi vượt quá 10 (≥ 11).</summary>
    public const int EnterThreshold = 10;

    /// <summary>ADR-57: thấp hơn ngưỡng vào để tránh "flapping" khi dao động quanh biên 10-11.</summary>
    public const int ExitThreshold = 8;

    public static bool ShouldBeMerged(bool currentlyMerged, int activeCount) => currentlyMerged
        ? activeCount > ExitThreshold
        : activeCount > EnterThreshold;
}
