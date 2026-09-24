using System.Runtime.InteropServices;

namespace ParentalGuard.UI.Services;

/// <summary>
/// ADR-117a (Architecture/10-ui-architecture.md mục 2.3) — named <see cref="Mutex"/> phát hiện đã có
/// 1 phiên <c>ParentalGuard.UI</c> đang chạy; nếu có, đưa cửa sổ đó lên foreground rồi tự thoát ngay
/// (không thử connect pipe — tránh lỗi kết nối khó hiểu "đang có phiên khác" từ giới hạn 1 kết nối
/// của pipe UI, Architecture/03 mục 6).
///
/// <para>
/// Tìm cửa sổ đang chạy theo TIÊU ĐỀ, không phải class name (khác câu chữ ADR-117a gốc): Windows App
/// SDK không expose 1 class name literal ổn định/tài liệu hoá qua các phiên bản để hard-code an toàn
/// cho <see cref="Microsoft.UI.Xaml.Window"/> unpackaged — tiêu đề <see cref="MainWindowTitle"/> đủ
/// định danh duy nhất cho mục đích UX thuần này (không phải ranh giới bảo mật, chỉ ảnh hưởng UX nếu
/// có cửa sổ khác trùng tiêu đề, rất khó xảy ra trên máy phụ huynh).
/// </para>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\ParentalGuard.UI.SingleInstance";
    private const string MainWindowTitle = "ParentalGuard";

    private readonly Mutex _mutex;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    /// <summary>Gọi khi <see cref="IsFirstInstance"/>=false — đưa cửa sổ đang chạy lên foreground.</summary>
    public static void ActivateExistingInstance()
    {
        IntPtr hWnd = FindWindow(null, MainWindowTitle);
        if (hWnd != IntPtr.Zero)
        {
            SetForegroundWindow(hWnd);
        }
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
