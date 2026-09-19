using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

public class VisionRuntimeConfigHolderTests
{
    [Fact]
    public void CreateDefault_MonitoringDisabled_FailSecureAtStartup()
    {
        // Trước khi nhận ControlVisionCommand đầu tiên từ Service, Vision không được tự bật giám
        // sát (không có nguồn cấu hình đáng tin cậy — SEC-017) — mặc định phải là false, KHÔNG
        // phải true, để CaptureLoopWorker tự park thay vì capture với cấu hình đoán mò.
        VisionRuntimeConfig config = VisionRuntimeConfig.CreateDefault();

        Assert.False(config.MonitoringEnabled);
        Assert.Empty(config.ExcludeProcessNames);
    }

    [Fact]
    public void Update_SwapsReferenceAtomically_ReadersSeeNewValueImmediately()
    {
        var holder = new VisionRuntimeConfigHolder();
        var updated = new VisionRuntimeConfig(MonitoringEnabled: true, CaptureIntervalMs: 500, RiskThreshold: 0.8f, ExcludeProcessNames: ["a.exe"]);

        holder.Update(updated);

        Assert.Same(updated, holder.Current);
    }
}
