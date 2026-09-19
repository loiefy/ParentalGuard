using System.Drawing;

namespace ParentalGuard.Overlay.Windows;

/// <summary>1 button ứng viên đọc được từ <c>IUIAutomation</c> (mục 2.2) — tách khỏi COM để matching logic test được độc lập.</summary>
public readonly record struct CloseButtonCandidate(string AutomationId, string Name, string LocalizedControlType, Rectangle BoundingRectangle);

/// <summary>
/// Heuristic matching lớp 1 (`FE-016c`, Architecture/07-overlay-architecture.md mục 2.2, ADR-55) —
/// hàm thuần, không phụ thuộc COM/UI Automation thật để test được với dữ liệu giả lập.
/// </summary>
public static class CloseButtonMatcher
{
    /// <summary>Tập <c>AutomationId</c> khớp chính xác (mục 2.2 bước 2) — mở rộng thêm khi test thực tế theo `BE-075`.</summary>
    private static readonly HashSet<string> _knownAutomationIds = new(StringComparer.Ordinal)
    {
        "Close", "CloseButton", "closeButton", "PART_CloseButton", "CaptionButtonClose", "Chrome_CloseButton", "Box_CloseButton",
    };

    /// <summary>Trả <c>null</c> nếu không có ứng viên nào khớp. Nếu nhiều khớp, chọn gần góc trên-phải cửa sổ nhất (mục 2.2 bước 3).</summary>
    public static CloseButtonCandidate? FindBestMatch(IReadOnlyList<CloseButtonCandidate> candidates, Rectangle windowRect)
    {
        List<CloseButtonCandidate> matches = candidates.Where(IsCloseButton).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        var anchor = new Point(windowRect.Right, windowRect.Top);
        return matches.OrderBy(c => DistanceSquared(RectCenter(c.BoundingRectangle), anchor)).First();
    }

    private static bool IsCloseButton(CloseButtonCandidate candidate) =>
        _knownAutomationIds.Contains(candidate.AutomationId)
        || ContainsCloseKeyword(candidate.Name)
        || ContainsCloseKeyword(candidate.LocalizedControlType);

    private static bool ContainsCloseKeyword(string text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains("close", StringComparison.OrdinalIgnoreCase) || text.Contains("đóng", StringComparison.OrdinalIgnoreCase));

    private static Point RectCenter(Rectangle rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    private static long DistanceSquared(Point a, Point b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}
