using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ParentalGuard.Tray;

/// <summary>
/// `FE-023a` (Specification/03 v0.24.0) — icon ParentalGuard trong khay hệ thống; click (trái hoặc phải) mở Dashboard.
/// Khởi chạy lúc đăng nhập qua khoá <c>HKLM\…\Run</c> do trình cài đặt ghi (Architecture/10 mục 9a). Explorer khởi động lại
/// (hoặc chưa sẵn sàng lúc đăng nhập) thì <see cref="NotifyIcon"/> tự thêm lại icon khi nhận <c>TaskbarCreated</c>.
/// </summary>
internal static class Program
{
    // Mỗi phiên đăng nhập 1 icon (Local\ — phiên khác có icon riêng).
    private const string _mutexName = @"Local\ParentalGuard.Tray.SingleInstance";
    private const string _dashboardExeName = "ParentalGuard.UI.exe";
    private const int _asfwAny = -1;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        if (!createdNew)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();

        using Icon icon = LoadIcon();
        var throttle = new LaunchThrottle();
        using var notifyIcon = new NotifyIcon { Icon = icon, Text = "ParentalGuard", Visible = true };
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right && throttle.TryAcquire())
            {
                OpenDashboard();
            }
        };

        Application.Run();
        notifyIcon.Visible = false;
        return 0;
    }

    private static Icon LoadIcon()
    {
        using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream("ParentalGuard.ico");
        return stream is null ? (Icon)SystemIcons.Shield.Clone() : new Icon(stream, SystemInformation.SmallIconSize);
    }

    /// <summary>
    /// Chạy đúng 1 đường dẫn cố định cạnh chính exe này, không tham số. Dashboard đang mở thì phiên mới tự đưa cửa sổ cũ lên
    /// trước rồi thoát (ADR-117a) — nên trao quyền đưa cửa sổ lên foreground (Explorer cấp cho icon khay khi được click).
    /// </summary>
    private static void OpenDashboard()
    {
        string exePath = Path.Combine(AppContext.BaseDirectory, _dashboardExeName);
        if (!File.Exists(exePath))
        {
            return;
        }

        AllowSetForegroundWindow(_asfwAny);
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Không mở được (vd đang gỡ cài đặt) — bỏ qua, lần click sau thử lại.
        }
    }
}
