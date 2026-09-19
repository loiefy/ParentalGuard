namespace ParentalGuard.Service.Ipc;

/// <summary>
/// `IMG-013`/`BE-090`: risk score ≥ ngưỡng cấu hình → coi là vi phạm, kích hoạt overlay. Tách
/// riêng khỏi <see cref="OverlayDecisionCoordinator"/> để unit test được độc lập, không cần dựng
/// toàn bộ hạ tầng IPC/process.
/// </summary>
public static class OverlayThresholdDecision
{
    public static bool Violates(float riskScore, float threshold) => riskScore >= threshold;
}
