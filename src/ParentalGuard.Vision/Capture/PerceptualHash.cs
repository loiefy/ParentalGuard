namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 3.8.1 (ADR-128) — difference hash (dHash)
/// 64-bit viết tay, KHÔNG dùng thư viện pHash ngoài (cùng tinh thần ADR-50/51: 2 frame so sánh luôn
/// cùng cửa sổ/độ phân giải, không cần full DCT-pHash chịu được xoay/nén).
/// </summary>
public static class PerceptualHash
{
    private const int SampleCols = 9;
    private const int SampleRows = 8;

    /// <summary>
    /// Lấy mẫu 9×8 điểm dàn đều trên <paramref name="bgra8"/> (O(72) bất kể kích thước cửa sổ), so độ
    /// sáng 2 cột liền kề cùng hàng → 64 bit. <paramref name="bgra8"/> phải có đúng
    /// <paramref name="width"/> × <paramref name="height"/> × 4 byte (BGRA8, hàng liên tục).
    /// </summary>
    public static ulong ComputeDHash64(ReadOnlySpan<byte> bgra8, int width, int height)
    {
        Span<byte> luma = stackalloc byte[SampleCols * SampleRows];
        try
        {
            for (int row = 0; row < SampleRows; row++)
            {
                int y = row * (height - 1) / (SampleRows - 1);
                for (int col = 0; col < SampleCols; col++)
                {
                    int x = col * (width - 1) / (SampleCols - 1);
                    int offset = ((y * width) + x) * 4;
                    luma[(row * SampleCols) + col] = (byte)((bgra8[offset] + bgra8[offset + 1] + bgra8[offset + 2]) / 3);
                }
            }

            ulong hash = 0;
            for (int row = 0; row < SampleRows; row++)
            {
                for (int col = 0; col < SampleCols - 1; col++)
                {
                    int bit = luma[(row * SampleCols) + col] < luma[(row * SampleCols) + col + 1] ? 1 : 0;
                    hash = (hash << 1) | (uint)bit;
                }
            }

            return hash;
        }
        finally
        {
            // IMG-003 (Architecture/05 mục 6/3.8.3): buffer dẫn xuất từ pixel, zero ngay khi ra khỏi
            // scope — KHÔNG áp dụng cho chính `hash` trả về (ngoại lệ tường minh IMG-011).
            luma.Clear();
        }
    }
}
