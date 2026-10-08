using ParentalGuard.Ipc.Protocol;

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
    bool UsingFallbackConfig,
    PerformanceMode PerformanceMode,
    string OverlayMessage,
    IReadOnlyList<string> UserWhitelistedProcessNames)
{
    /// <summary>`PAUSE-040` (2026-10-07): chế độ "Bảo vệ cả phụ huynh" — mặc định tắt.</summary>
    public bool ParentProtectionEnabled { get; init; }

    /// <summary>`FE-063a` (2026-10-07): ngôn ngữ hiển thị cho cả máy — mặc định tiếng Việt.</summary>
    public string Language { get; init; } = DefaultLanguage;

    public const string DefaultLanguage = "vi";

    /// <summary>`PAUSE-045c` (2026-10-08): quãng đường trò chơi nhảy rào (độ khó) — 800 (dễ) / 1600 (vừa) / 2000 m (khó).</summary>
    public uint ParentGameMeters { get; init; } = DefaultParentGameMeters;

    public const uint DefaultParentGameMeters = 800;

    /// <summary>`FE-063a`: đúng 6 ngôn ngữ hỗ trợ.</summary>
    public static readonly IReadOnlySet<string> SupportedLanguages = new HashSet<string>(StringComparer.Ordinal) { "vi", "en", "fr", "es", "pt", "zh-Hans" };

    /// <summary>Placeholder — con số thật do benchmark quyết định ở BE-090/Đợt 1 (mục 3.3).</summary>
    public const float DefaultRiskThreshold = 0.7f;

    /// <summary>
    /// Không còn dùng để gửi thẳng <c>capture_interval_ms</c> (Đợt 7 — xem
    /// <c>ParentalGuard.Service.Performance.AdaptiveFrameRateCoordinator</c>) — giữ lại làm giá trị
    /// mặc định của field <see cref="CaptureIntervalBaselineMs"/> đã có trong schema <c>config.db</c>
    /// (Architecture/04 mục 3.3), chưa có đường ghi/đọc nào khác dùng tới.
    /// </summary>
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
        UsingFallbackConfig: false,
        PerformanceMode: PerformanceMode.Balanced,
        OverlayMessage: "",
        UserWhitelistedProcessNames: []);

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
        UsingFallbackConfig: false,
        PerformanceMode: PerformanceMode.Balanced,
        OverlayMessage: "",
        UserWhitelistedProcessNames: []);
}
