using System.Runtime.InteropServices;
using Vortice.DXGI;

namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Bước 1 (Architecture/05 mục 4.1/3.5, `PERF-021`, `BE-080`-`082`): enumerate toàn bộ output DXGI
/// và khớp với HMONITOR chứa 1 cửa sổ (ADR-63) — cầu nối DXGI ↔ GDI/Win32 dùng cho cả single-monitor
/// lẫn multi-monitor (đa cửa sổ, `IMG-020`, ADR-64).
/// </summary>
public static class MonitorSelector
{
    private const uint _monitorDefaultToNearest = 2;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    public readonly record struct OutputInfo(int AdapterIndex, int OutputIndex, IntPtr Monitor);

    /// <summary>
    /// Enumerate toàn bộ <c>IDXGIOutput</c> qua mọi adapter (`BE-081`) — gọi lại **mỗi chu kỳ capture**,
    /// không cache giữa các chu kỳ: tự phản ánh thêm/rút màn hình mà không cần bắt riêng
    /// <c>WM_DISPLAYCHANGE</c> (Architecture/05 mục 3.5/11 — cơ chế nhận message đó để ngỏ cho
    /// feature-dev tự chọn, chọn phương án re-enumerate mỗi chu kỳ vì đơn giản hơn và chi phí
    /// enumerate (không tạo device) không đáng kể so với ngân sách 1 chu kỳ).
    /// </summary>
    public static IReadOnlyList<OutputInfo> EnumerateOutputs(IDXGIFactory1 factory)
    {
        var results = new List<OutputInfo>();
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success; adapterIndex++)
        {
            using (adapter)
            {
                for (uint outputIndex = 0; adapter!.EnumOutputs(outputIndex, out IDXGIOutput? output).Success; outputIndex++)
                {
                    using (output)
                    {
                        results.Add(new OutputInfo((int)adapterIndex, (int)outputIndex, output!.Description.Monitor));
                    }
                }
            }
        }

        return results;
    }

    /// <summary>HMONITOR chứa <paramref name="hwnd"/> — <see cref="IntPtr.Zero"/> nếu không xác định được (cửa sổ đã đóng).</summary>
    public static IntPtr GetMonitorForWindow(IntPtr hwnd) => MonitorFromWindow(hwnd, _monitorDefaultToNearest);

    /// <summary>Hàm thuần (ADR-63, test được không cần DXGI/Win32 thật): khớp <paramref name="monitor"/> với danh sách output đã enumerate.</summary>
    public static OutputInfo? MatchOutputByMonitor(IReadOnlyList<OutputInfo> outputs, IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        foreach (OutputInfo output in outputs)
        {
            if (output.Monitor == monitor)
            {
                return output;
            }
        }

        return null;
    }

    /// <summary>Tìm output chứa <paramref name="hwnd"/> trong danh sách đã enumerate — <c>null</c> nếu không tìm thấy (màn hình vừa bị rút).</summary>
    public static OutputInfo? ResolveOutputForWindow(IReadOnlyList<OutputInfo> outputs, IntPtr hwnd) =>
        MatchOutputByMonitor(outputs, GetMonitorForWindow(hwnd));
}
