namespace ParentalGuard.Service.Data;

/// <summary>
/// <c>MonitoringState</c> (Architecture/02-process-architecture.md mục 5.3,
/// Architecture/04-data-architecture.md mục 3.3).
/// </summary>
public sealed record MonitoringStateData(
    bool MonitoringEnabled,
    float RiskThreshold,
    uint CaptureIntervalBaselineMs,
    IReadOnlyList<string> ExcludeProcessNames,
    bool UsingFallbackConfig)
{
    /// <summary>Placeholder — con số thật do benchmark quyết định ở BE-090/Đợt 1 (mục 3.3).</summary>
    public const float DefaultRiskThreshold = 0.7f;

    /// <summary>Placeholder — tuning ở PERF-010/Đợt 7 (mục 3.3).</summary>
    public const uint DefaultCaptureIntervalMs = 1000;

    /// <summary>Danh sách khởi điểm BE-073a (Specification/02-backend-spec.md mục 6).</summary>
    public static readonly IReadOnlyList<string> InitialExcludeProcessNames =
    [
        "Taskmgr.exe",
        "regedit.exe",
        "cmd.exe",
        "conhost.exe",
        "powershell.exe",
        "pwsh.exe",
        "WindowsTerminal.exe",
        "mmc.exe",
        "eventvwr.exe",
        "perfmon.exe",
        "resmon.exe",
        "msconfig.exe",
        "msinfo32.exe",
        "charmap.exe",
        "CalculatorApp.exe",
        "SecHealthUI.exe",
        "notepad.exe",
        "LogonUI.exe",
    ];

    /// <summary>Cài đặt lần đầu (mục 6.1 dòng 1) — không phải nhánh fail-secure.</summary>
    public static MonitoringStateData CreateFirstRunDefault() => new(
        MonitoringEnabled: true,
        RiskThreshold: DefaultRiskThreshold,
        CaptureIntervalBaselineMs: DefaultCaptureIntervalMs,
        ExcludeProcessNames: InitialExcludeProcessNames,
        UsingFallbackConfig: false);

    /// <summary>
    /// Nhánh fail-secure (BE-061/ANTI-070, Architecture/04 mục 6.2 bước 2) — whitelist rỗng,
    /// KHÔNG dùng lại <see cref="InitialExcludeProcessNames"/> vì danh sách đó chỉ an toàn khi
    /// chính nó được xác thực toàn vẹn qua config.db hợp lệ.
    /// </summary>
    public static MonitoringStateData CreateFailSecureDefault() => new(
        MonitoringEnabled: true,
        RiskThreshold: DefaultRiskThreshold,
        CaptureIntervalBaselineMs: DefaultCaptureIntervalMs,
        ExcludeProcessNames: [],
        UsingFallbackConfig: false);
}
