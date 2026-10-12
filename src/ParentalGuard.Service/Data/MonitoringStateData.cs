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

    // PHẢI khai báo TRƯỚC InitialExcludeProcessNames (field static khởi tạo theo thứ tự trong file).
    /// <summary>`BE-073b` (2026-10-12): UniKey, EVKey, OpenKey (cả bản 32/64-bit).</summary>
    public static readonly IReadOnlyList<string> ExcludeListAdditionsRevision1 =
    [
        "UniKeyNT.exe",
        "EVKey64.exe",
        "EVKey32.exe",
        "OpenKey64.exe",
        "OpenKey32.exe",
        "OpenKey.exe",
    ];

    /// <summary>`BE-073c` (2026-10-12, chủ dự án yêu cầu): Notepad++ (chỉ chữ), menu Start (chỉ biểu tượng/tên ứng dụng và file gần đây).</summary>
    public static readonly IReadOnlyList<string> ExcludeListAdditionsRevision2 =
    [
        "notepad++.exe",
        "StartMenuExperienceHost.exe",
    ];

    /// <summary>Các lượt bổ sung theo thứ tự phiên bản — phần tử thứ i là lượt của phiên bản i+1.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> ExcludeListAdditions => [ExcludeListAdditionsRevision1, ExcludeListAdditionsRevision2];

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

        // BE-073b (2026-10-12): bộ gõ tiếng Việt — chỉ là thanh công cụ/hộp thoại chữ, không hiển thị ảnh/video. Bản cài thật bị che
        // nhầm cửa sổ UniKey ngay sau khi đăng nhập.
        .. ExcludeListAdditionsRevision1,
        .. ExcludeListAdditionsRevision2,
    ];

    /// <summary>Phiên bản danh sách khởi điểm hiện hành — tăng mỗi khi bổ sung mục mới cho máy đã cài (xem <see cref="MigrateExcludeList"/>).</summary>
    public const int CurrentExcludeListRevision = 2;

    /// <summary>
    /// Phiên bản danh sách đã áp dụng cho máy này. config.db cũ (thiếu trường) = 0. Tách khỏi nội dung danh sách để mục phụ huynh đã
    /// chủ động xoá (`MISC-030b`) KHÔNG bị thêm lại ở lần khởi động sau.
    /// </summary>
    public int ExcludeListRevision { get; init; }

    /// <summary>
    /// Bổ sung các mục khởi điểm mới (theo phiên bản) vào danh sách đã lưu của máy đã cài — đúng 1 lần cho mỗi phiên bản.
    /// Trả <c>null</c> nếu không có gì thay đổi.
    /// </summary>
    public MonitoringStateData? MigrateExcludeList()
    {
        if (ExcludeListRevision >= CurrentExcludeListRevision || UsingFallbackConfig)
        {
            return null;
        }

        var names = new List<string>(ExcludeProcessNames);
        for (int revision = ExcludeListRevision + 1; revision <= CurrentExcludeListRevision; revision++)
        {
            foreach (string name in ExcludeListAdditions[revision - 1])
            {
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }

        return this with { ExcludeProcessNames = names, ExcludeListRevision = CurrentExcludeListRevision };
    }

    /// <summary>Cài đặt lần đầu (mục 6.1 dòng 1) — không phải nhánh fail-secure.</summary>
    public static MonitoringStateData CreateFirstRunDefault() => new(
        MonitoringEnabled: true,
        RiskThreshold: DefaultRiskThreshold,
        CaptureIntervalBaselineMs: DefaultCaptureIntervalMs,
        ExcludeProcessNames: InitialExcludeProcessNames,
        UsingFallbackConfig: false,
        PerformanceMode: PerformanceMode.Balanced,
        OverlayMessage: "",
        UserWhitelistedProcessNames: [])
    {
        ExcludeListRevision = CurrentExcludeListRevision,
    };

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
        UserWhitelistedProcessNames: [])
    {
        // BE-073b: danh sách rỗng là CHỦ Ý của nhánh fail-secure — đánh dấu đã ở phiên bản hiện hành để không bao giờ bổ sung mục.
        ExcludeListRevision = CurrentExcludeListRevision,
    };
}
