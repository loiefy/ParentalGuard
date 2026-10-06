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

    /// <summary>Số <c>ulong</c> của <see cref="ComputeBlockDHash"/> (32 hàng × 32 bit = 1024 bit).</summary>
    public const int BlockHashWords = 16;

    private const int BlockCols = 33;
    private const int BlockRows = 32;
    private const int BlockSampleStep = 2;

    /// <summary>
    /// Bug real-hardware 2026-10-06 (video chủ dự án: ảnh băng chuyền trong Edge đổi nhưng điểm 91% của ảnh trước "kẹt"
    /// sang ảnh sau): <see cref="ComputeDHash64"/> chỉ lấy 72 ĐIỂM ảnh đơn lẻ trên cả cửa sổ — nội dung đổi trong 1 vùng
    /// con hầu như không chạm điểm mẫu nào nên bị coi là "không đổi". Bản này chia cửa sổ thành lưới 33×32 ô, lấy độ sáng
    /// TRUNG BÌNH mỗi ô (lấy mẫu cách 1 pixel) rồi so 2 ô liền kề → 1024 bit: thay đổi ở bất kỳ vùng nào cỡ vài % diện tích
    /// đều làm lệch nhiều bit. <paramref name="destination"/> phải có <see cref="BlockHashWords"/> phần tử.
    /// </summary>
    public static void ComputeBlockDHash(ReadOnlySpan<byte> bgra8, int width, int height, Span<ulong> destination)
    {
        Span<int> luma = stackalloc int[BlockCols * BlockRows];
        try
        {
            for (int row = 0; row < BlockRows; row++)
            {
                int y0 = row * height / BlockRows;
                int y1 = Math.Max(y0 + 1, (row + 1) * height / BlockRows);
                for (int col = 0; col < BlockCols; col++)
                {
                    int x0 = col * width / BlockCols;
                    int x1 = Math.Max(x0 + 1, (col + 1) * width / BlockCols);
                    long sum = 0;
                    int count = 0;
                    for (int y = y0; y < y1; y += BlockSampleStep)
                    {
                        int rowOffset = y * width * 4;
                        for (int x = x0; x < x1; x += BlockSampleStep)
                        {
                            int offset = rowOffset + (x * 4);
                            sum += bgra8[offset] + bgra8[offset + 1] + bgra8[offset + 2];
                            count++;
                        }
                    }

                    luma[(row * BlockCols) + col] = count == 0 ? 0 : (int)(sum / count);
                }
            }

            for (int row = 0; row < BlockRows; row++)
            {
                ulong bits = 0;
                for (int col = 0; col < BlockCols - 1; col++)
                {
                    bits = (bits << 1) | (luma[(row * BlockCols) + col] < luma[(row * BlockCols) + col + 1] ? 1UL : 0UL);
                }

                // 2 hàng 32 bit gộp vào 1 ulong.
                int word = row / 2;
                destination[word] = row % 2 == 0 ? bits << 32 : destination[word] | bits;
            }
        }
        finally
        {
            luma.Clear(); // IMG-003: buffer dẫn xuất từ pixel
        }
    }
}
