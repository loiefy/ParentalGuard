using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>1 cửa sổ top-level tại thời điểm chụp danh sách Z-order — dữ liệu thuần để test không cần Win32.</summary>
/// <param name="Handle">HWND.</param>
/// <param name="Rect">Bounds màn hình (DWM extended frame) — null nếu không resolve được.</param>
/// <param name="IsShown">Visible, không minimized, không bị DWM cloak, có tiêu đề — cửa sổ người dùng thực sự nhìn thấy (đồng thời là "vật che" cho cửa sổ phía dưới).</param>
/// <param name="IsMonitorable">Không thuộc exclude-list, không phải cửa sổ của chính ParentalGuard, nằm trên 1 màn hình có thật.</param>
public readonly record struct WindowSnapshot(IntPtr Handle, WindowRect? Rect, bool IsShown, bool IsMonitorable);

/// <summary>
/// `BE-071a`/`PERF-020a`/`IMG-020a` (Specification v0.7.8, ĐÃ CHỐT 2026-10-01, supersedes chiến lược
/// "chỉ foreground + 1 cửa sổ/màn hình phụ" của `BE-071`/`IMG-020`): giám sát MỌI cửa sổ đang hiển thị —
/// foreground trước, rồi Z-order trên xuống, tối đa <see cref="MaxCandidatesPerCycle"/> cửa sổ/chu kỳ.
/// Bỏ qua cửa sổ đang bị overlay che (`BE-034b`) và cửa sổ bị các cửa sổ phía trên che khuất hoàn toàn.
/// Hàm thuần (nhận snapshot thay vì gọi P/Invoke) để test được không cần Win32/DXGI thật.
/// </summary>
public static class CandidateWindowSelector
{
    /// <summary>`BE-071a`: trần CPU — tối đa 4 cửa sổ được capture/phân loại mỗi chu kỳ.</summary>
    public const int MaxCandidatesPerCycle = 4;

    /// <summary>Cửa sổ nhỏ hơn mức này (px) không đủ để hiển thị nội dung có ý nghĩa — bỏ qua (thanh công cụ nổi, popup tí hon).</summary>
    internal const int MinWindowSidePx = 64;

    private const int _occlusionSampleGrid = 8;

    public static IReadOnlyList<IntPtr> SelectVisibleCandidates(
        IntPtr foregroundHwnd,
        IReadOnlyList<WindowSnapshot> windowsInZOrder,
        IReadOnlySet<ulong> coveredWindowHandles)
    {
        var occluders = new List<WindowRect>();
        var visible = new List<IntPtr>();

        foreach (WindowSnapshot window in windowsInZOrder)
        {
            if (!window.IsShown || window.Rect is not { } rect || rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            bool covered = coveredWindowHandles.Contains(unchecked((ulong)window.Handle.ToInt64()));
            if (window.IsMonitorable
                && !covered
                && rect.Width >= MinWindowSidePx
                && rect.Height >= MinWindowSidePx
                && HasUncoveredArea(rect, occluders))
            {
                visible.Add(window.Handle);
            }

            // Mọi cửa sổ đang hiển thị (kể cả bị exclude/bị overlay che) đều che cửa sổ nằm dưới nó.
            occluders.Add(rect);
        }

        var ordered = new List<IntPtr>(Math.Min(visible.Count, MaxCandidatesPerCycle));
        if (foregroundHwnd != IntPtr.Zero && visible.Contains(foregroundHwnd))
        {
            ordered.Add(foregroundHwnd);
        }

        foreach (IntPtr hwnd in visible)
        {
            if (ordered.Count >= MaxCandidatesPerCycle)
            {
                break;
            }

            if (hwnd != foregroundHwnd)
            {
                ordered.Add(hwnd);
            }
        }

        return ordered;
    }

    /// <summary>Lấy mẫu lưới 8×8 tâm ô — còn ít nhất 1 điểm không nằm trong cửa sổ nào phía trên thì coi là còn nhìn thấy.</summary>
    internal static bool HasUncoveredArea(WindowRect rect, IReadOnlyList<WindowRect> occluders)
    {
        if (occluders.Count == 0)
        {
            return true;
        }

        for (int gy = 0; gy < _occlusionSampleGrid; gy++)
        {
            int y = rect.Y + (int)((gy + 0.5) * rect.Height / _occlusionSampleGrid);
            for (int gx = 0; gx < _occlusionSampleGrid; gx++)
            {
                int x = rect.X + (int)((gx + 0.5) * rect.Width / _occlusionSampleGrid);
                if (!occluders.Any(o => x >= o.X && x < o.X + o.Width && y >= o.Y && y < o.Y + o.Height))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>`BE-034b`: loại cửa sổ đang bị overlay che (Service gửi qua <c>ControlVisionCommand.covered_window_handles</c>).</summary>
    public static IReadOnlyList<IntPtr> ExcludeCovered(IReadOnlyList<IntPtr> candidates, IReadOnlySet<ulong> coveredWindowHandles)
    {
        if (coveredWindowHandles.Count == 0)
        {
            return candidates;
        }

        return [.. candidates.Where(hwnd => !coveredWindowHandles.Contains(unchecked((ulong)hwnd.ToInt64())))];
    }
}
