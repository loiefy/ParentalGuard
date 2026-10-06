using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Bug real-hardware 2026-10-06 (Architecture/05 mục 4.2a): che phần ảnh crop thuộc về cửa sổ KHÁC đang nằm trên cửa
/// sổ được phân loại — Desktop Duplication là ảnh màn hình đã ghép, không phải nội dung riêng của từng cửa sổ.
/// Hàm thuần, test không cần DXGI.
/// </summary>
public static class OcclusionMask
{
    private const int _sampleGrid = 16;

    /// <summary>Giao từng vật che với vùng crop (toạ độ desktop) rồi đổi sang toạ độ trong crop; bỏ vật che không giao.</summary>
    public static IReadOnlyList<WindowRect> ToCropLocal(WindowRect cropOnDesktop, IReadOnlyList<WindowRect> occluders)
    {
        var local = new List<WindowRect>();
        foreach (WindowRect o in occluders)
        {
            int left = Math.Max(o.X, cropOnDesktop.X);
            int top = Math.Max(o.Y, cropOnDesktop.Y);
            int right = Math.Min(o.X + o.Width, cropOnDesktop.X + cropOnDesktop.Width);
            int bottom = Math.Min(o.Y + o.Height, cropOnDesktop.Y + cropOnDesktop.Height);
            if (right > left && bottom > top)
            {
                local.Add(new WindowRect(left - cropOnDesktop.X, top - cropOnDesktop.Y, right - left, bottom - top));
            }
        }

        return local;
    }

    /// <summary>Tỉ lệ diện tích crop KHÔNG bị che — lấy mẫu lưới 16×16 (đủ chính xác cho ngưỡng 10%).</summary>
    public static double VisibleFraction(int width, int height, IReadOnlyList<WindowRect> masks)
    {
        if (masks.Count == 0)
        {
            return 1.0;
        }

        int visible = 0;
        for (int gy = 0; gy < _sampleGrid; gy++)
        {
            int y = (int)((gy + 0.5) * height / _sampleGrid);
            for (int gx = 0; gx < _sampleGrid; gx++)
            {
                int x = (int)((gx + 0.5) * width / _sampleGrid);
                if (!masks.Any(m => x >= m.X && x < m.X + m.Width && y >= m.Y && y < m.Y + m.Height))
                {
                    visible++;
                }
            }
        }

        return visible / (double)(_sampleGrid * _sampleGrid);
    }

    /// <summary>Tô đen (BGRA = 0) các vùng bị che trong buffer BGRA8 chặt <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static void Apply(byte[] bgra, int width, int height, IReadOnlyList<WindowRect> masks)
    {
        foreach (WindowRect m in masks)
        {
            int left = Math.Clamp(m.X, 0, width);
            int right = Math.Clamp(m.X + m.Width, 0, width);
            int top = Math.Clamp(m.Y, 0, height);
            int bottom = Math.Clamp(m.Y + m.Height, 0, height);
            if (right <= left)
            {
                continue;
            }

            for (int y = top; y < bottom; y++)
            {
                bgra.AsSpan(((y * width) + left) * 4, (right - left) * 4).Clear();
            }
        }
    }
}
