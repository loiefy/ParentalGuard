using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// `IMG-016b` (Specification/09 v0.10.0): lọc màu da cho điểm sát ngưỡng. Model phân loại không "tìm người" — nó học các
/// đặc điểm thống kê (đường cong bóng mượt, tông hồng/tím…), nên hình 3D trang trí có thể bị chấm cao dù không có ai trong
/// ảnh (trang đăng nhập Teams 2026-10-15: vùng con 0.737, chỉ 0.1% điểm ảnh màu da). Vùng có màu rõ nhưng gần như không có
/// màu da và điểm dưới <see cref="MaxGatedScore"/> → hạ điểm xuống tối đa <see cref="CappedScore"/>. Ảnh đen trắng/ít màu
/// (không phân biệt được màu da) và điểm rất cao giữ nguyên. Đọc thẳng buffer pixel đang xử lý — không cấp phát, không lưu gì.
/// </summary>
public static class SkinToneGate
{
    /// <summary>Điểm từ mức này trở lên giữ nguyên dù không có màu da.</summary>
    public const float MaxGatedScore = 0.9f;

    /// <summary>Điểm sau khi hạ — dưới ngưỡng chặn mặc định 0.7 (`BE-090`).</summary>
    public const float CappedScore = 0.5f;

    /// <summary>Dưới tỷ lệ điểm ảnh màu da này coi là "gần như không có màu da".</summary>
    public const double MaxSkinFraction = 0.005;

    /// <summary>Vùng cần ít nhất tỷ lệ điểm ảnh có màu này mới áp dụng (ảnh xám không có Cb/Cr để nhận màu da).</summary>
    public const double MinChromaticFraction = 0.20;

    /// <summary>Khoảng cách |Cb−128| + |Cr−128| tối thiểu để 1 điểm ảnh tính là "có màu".</summary>
    private const double _chromaticDistance = 12;

    // Lấy mẫu 1 điểm mỗi bước theo cả 2 chiều — đủ cho tỷ lệ, nhanh với vùng lớn.
    private const int _sampleStep = 2;

    /// <summary>Trả về điểm sau lọc cho 1 vùng của buffer BGRA8 (<paramref name="width"/> = chiều rộng cả buffer).</summary>
    public static float Apply(float score, ReadOnlySpan<byte> bgra8, int width, WindowRect region)
    {
        if (score <= CappedScore || score >= MaxGatedScore)
        {
            return score;
        }

        (double skinFraction, double chromaticFraction) = Measure(bgra8, width, region);
        return chromaticFraction >= MinChromaticFraction && skinFraction < MaxSkinFraction ? CappedScore : score;
    }

    /// <summary>Tỷ lệ điểm ảnh màu da và tỷ lệ điểm ảnh có màu trong vùng (lấy mẫu).</summary>
    internal static (double SkinFraction, double ChromaticFraction) Measure(ReadOnlySpan<byte> bgra8, int width, WindowRect region)
    {
        long sampled = 0;
        long skin = 0;
        long chromatic = 0;
        for (int y = region.Y; y < region.Y + region.Height; y += _sampleStep)
        {
            int rowStart = y * width;
            for (int x = region.X; x < region.X + region.Width; x += _sampleStep)
            {
                int i = (rowStart + x) * 4;
                double b = bgra8[i];
                double g = bgra8[i + 1];
                double r = bgra8[i + 2];

                // YCbCr (BT.601) — dải màu da Chai & Ngan: 77 ≤ Cb ≤ 127, 133 ≤ Cr ≤ 173 (không phụ thuộc độ sáng).
                double cb = 128 - (0.168736 * r) - (0.331264 * g) + (0.5 * b);
                double cr = 128 + (0.5 * r) - (0.418688 * g) - (0.081312 * b);
                sampled++;
                if (Math.Abs(cb - 128) + Math.Abs(cr - 128) > _chromaticDistance)
                {
                    chromatic++;
                }

                if (cb is >= 77 and <= 127 && cr is >= 133 and <= 173)
                {
                    skin++;
                }
            }
        }

        return sampled == 0 ? (1, 0) : ((double)skin / sampled, (double)chromatic / sampled);
    }
}
