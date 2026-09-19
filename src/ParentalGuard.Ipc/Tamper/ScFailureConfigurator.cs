namespace ParentalGuard.Ipc.Tamper;

/// <summary>
/// SCM Recovery Options (`ANTI-011`, Architecture/09-anti-tamper-architecture.md mục 3.5, ADR-89) —
/// idempotent, gọi lại mỗi lần Starting. Không phải ngưỡng bảo mật cốt lõi (chỉ lớp bổ sung miễn phí
/// của Windows) — số liệu quyết định trực tiếp ở architecture, không cần chủ dự án xác nhận thêm.
/// </summary>
public static class ScFailureConfigurator
{
    private const int _resetPeriodSeconds = 86_400;

    public static Task<ScResult> ConfigureAsync(string serviceName, CancellationToken cancellationToken)
    {
        string arguments = $"failure {serviceName} reset= {_resetPeriodSeconds} actions= restart/60000/restart/120000/restart/300000";
        return ScProcessRunner.RunAsync(arguments, cancellationToken);
    }
}
