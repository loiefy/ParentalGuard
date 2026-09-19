namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Architecture/05 mục 3.5 (`IMG-020`, `BE-082`, ADR-64): danh sách cửa sổ cần xử lý mỗi chu kỳ =
/// foreground trước (ưu tiên cao nhất) + tối đa 1 cửa sổ/màn hình còn lại theo Z-order. Hàm thuần
/// (nhận delegate thay vì gọi P/Invoke trực tiếp) để test được không cần Win32/DXGI thật.
/// </summary>
public static class CandidateWindowSelector
{
    public static IReadOnlyList<IntPtr> SelectCandidates(
        IntPtr foregroundHwnd,
        bool foregroundExcluded,
        int outputCount,
        IReadOnlyList<IntPtr> windowsInZOrder,
        Func<IntPtr, IntPtr> monitorOf,
        Func<IntPtr, bool> isCandidateWindow)
    {
        var candidates = new List<IntPtr>();
        var coveredMonitors = new HashSet<IntPtr>();

        // BE-073a: cửa sổ foreground bị loại trừ không được thêm vào candidates VÀ không chiếm chỗ
        // "đã phủ" màn hình của nó — để nhánh Z-order bên dưới vẫn có thể chọn 1 cửa sổ khác không bị
        // loại trừ trên cùng màn hình đó (fail-secure: ưu tiên giám sát nhiều hơn, không bỏ sót cả màn
        // hình chỉ vì đúng cửa sổ đang focus thuộc danh sách loại trừ).
        if (foregroundHwnd != IntPtr.Zero && !foregroundExcluded)
        {
            candidates.Add(foregroundHwnd);
            coveredMonitors.Add(monitorOf(foregroundHwnd));
        }

        if (outputCount > 1)
        {
            foreach (IntPtr w in windowsInZOrder)
            {
                if (coveredMonitors.Count == outputCount)
                {
                    break;
                }

                IntPtr m = monitorOf(w);
                if (m == IntPtr.Zero || coveredMonitors.Contains(m))
                {
                    continue;
                }

                if (!isCandidateWindow(w))
                {
                    continue;
                }

                candidates.Add(w);
                coveredMonitors.Add(m);
            }
        }

        return candidates;
    }
}
