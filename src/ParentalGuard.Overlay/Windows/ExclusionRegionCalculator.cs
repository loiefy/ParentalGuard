using System.Drawing;

namespace ParentalGuard.Overlay.Windows;

/// <summary>
/// Vùng loại trừ nút đóng 3 lớp (`FE-016c`, Architecture/07-overlay-architecture.md mục 2.3/2.4) —
/// hàm thuần, tách khỏi Win32/COM để test độc lập.
/// </summary>
public static class ExclusionRegionCalculator
{
    /// <summary>Lớp 3: fallback cố định 160×50 ở 100% DPI, neo góc trên-phải cửa sổ.</summary>
    public static Rectangle FallbackRect(Rectangle windowRect, double dpiScale)
    {
        int width = ScaleDpi(160, dpiScale);
        int height = ScaleDpi(50, dpiScale);
        return new Rectangle(windowRect.Right - width, windowRect.Top, width, height);
    }

    /// <summary>Lớp 2: padding 16px mỗi cạnh quanh rect chính xác lớp 1, cắt về trong biên cửa sổ.</summary>
    public static Rectangle PaddedRect(Rectangle uiaButtonRect, Rectangle windowRect, double dpiScale)
    {
        int padding = ScaleDpi(16, dpiScale);
        Rectangle inflated = Rectangle.Inflate(uiaButtonRect, padding, padding);
        return Rectangle.Intersect(inflated, windowRect);
    }

    private static int ScaleDpi(int value, double dpiScale) => (int)Math.Round(value * dpiScale, MidpointRounding.AwayFromZero);
}
