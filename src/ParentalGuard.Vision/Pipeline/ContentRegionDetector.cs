using ParentalGuard.Vision.Capture;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// `IMG-016a` (2026-10-06): tìm vùng nội dung chính (video/ảnh) trong cửa sổ để chấm thêm 1 vùng con "động" — nội dung vi
/// phạm thường chỉ chiếm 1 phần cửa sổ trình duyệt, phần còn lại là giao diện web/nền.
/// <list type="number">
/// <item>Vùng CHUYỂN ĐỘNG: các ô của lưới hash khối (<see cref="PerceptualHash.ComputeBlockDHash"/>) có bit đổi so với khung
/// trước của cùng cửa sổ — tái dùng đúng hash đã lưu (state kỹ thuật `IMG-011`), không lưu thêm dữ liệu ảnh.</item>
/// <item>Không có chuyển động: vùng liền khối lớn nhất gồm các ô "giống ảnh chụp" (độ tương phản và độ đậm màu cao — giao diện web
/// phần lớn là nền phẳng và chữ xám), chỉ tính trên khung hiện tại.</item>
/// </list>
/// Không dùng thêm model AI nào. Hàm thuần, test không cần DXGI.
/// </summary>
public static class ContentRegionDetector
{
    public const int Cols = 33;
    public const int Rows = 32;

    /// <summary>Vùng tìm được phải chiếm ít nhất tỉ lệ này của cửa sổ (bỏ chuột nhấp nháy, icon động nhỏ).</summary>
    public const double MinAreaFraction = 0.03;

    /// <summary>Vùng phủ gần hết cửa sổ thì trùng với "cả cửa sổ" — không cần vùng thứ 6.</summary>
    public const double MaxAreaFraction = 0.90;

    /// <summary>Nới mỗi cạnh thêm tỉ lệ này của kích thước vùng, để không cắt mất mép video.</summary>
    public const double ExpandFraction = 0.05;

    private const int MinCells = 4;
    private const int PhotoMinStdDev = 10;
    private const int PhotoMinColorfulness = 14;
    private const int SampleStep = 2;

    /// <summary>Vùng chuyển động nếu có, ngược lại vùng giống ảnh chụp; đã nới biên. <c>null</c> nếu không tìm được vùng hợp lệ.</summary>
    public static WindowRect? Detect(ReadOnlySpan<byte> bgra8, int width, int height, ReadOnlySpan<ulong> previousHash, ReadOnlySpan<ulong> currentHash)
    {
        WindowRect? region = previousHash.Length == currentHash.Length && previousHash.Length > 0
            ? FromMotion(previousHash, currentHash, width, height)
            : null;
        region ??= FromPhotoCells(bgra8, width, height);
        return region is { } r ? Expand(r, width, height) : null;
    }

    /// <summary>Khung bao vùng liền khối lớn nhất các ô có bit hash đổi giữa 2 khung.</summary>
    public static WindowRect? FromMotion(ReadOnlySpan<ulong> previousHash, ReadOnlySpan<ulong> currentHash, int width, int height)
    {
        var changed = new bool[Rows, Cols];
        for (int row = 0; row < Rows; row++)
        {
            ulong prev = RowBits(previousHash, row);
            ulong cur = RowBits(currentHash, row);
            ulong diff = prev ^ cur;
            for (int col = 0; col < Cols - 1; col++)
            {
                if (((diff >> (31 - col)) & 1UL) != 0)
                {
                    changed[row, col] = true;
                    changed[row, col + 1] = true;
                }
            }
        }

        return LargestComponentBounds(changed, width, height, eightConnected: true);
    }

    /// <summary>Khung bao vùng liền khối lớn nhất các ô có độ tương phản và độ đậm màu cao (ảnh/video, không phải chữ/nền phẳng).</summary>
    public static WindowRect? FromPhotoCells(ReadOnlySpan<byte> bgra8, int width, int height)
    {
        var photo = new bool[Rows, Cols];
        for (int row = 0; row < Rows; row++)
        {
            int y0 = row * height / Rows;
            int y1 = Math.Max(y0 + 1, (row + 1) * height / Rows);
            for (int col = 0; col < Cols; col++)
            {
                int x0 = col * width / Cols;
                int x1 = Math.Max(x0 + 1, (col + 1) * width / Cols);
                long sum = 0;
                long sumSq = 0;
                long colorful = 0;
                int count = 0;
                for (int y = y0; y < y1; y += SampleStep)
                {
                    int rowOffset = y * width * 4;
                    for (int x = x0; x < x1; x += SampleStep)
                    {
                        int o = rowOffset + (x * 4);
                        int b = bgra8[o];
                        int g = bgra8[o + 1];
                        int r = bgra8[o + 2];
                        int luma = (r + g + b) / 3;
                        sum += luma;
                        sumSq += luma * luma;
                        colorful += Math.Abs(r - g) + Math.Abs(g - b) + Math.Abs(b - r);
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                double mean = sum / (double)count;
                double stdDev = Math.Sqrt(Math.Max(0, (sumSq / (double)count) - (mean * mean)));
                photo[row, col] = stdDev >= PhotoMinStdDev && colorful / (double)count >= PhotoMinColorfulness;
            }
        }

        return LargestComponentBounds(photo, width, height, eightConnected: false);
    }

    public static WindowRect Expand(WindowRect r, int width, int height)
    {
        int dx = (int)(r.Width * ExpandFraction);
        int dy = (int)(r.Height * ExpandFraction);
        int left = Math.Max(0, r.X - dx);
        int top = Math.Max(0, r.Y - dy);
        int right = Math.Min(width, r.X + r.Width + dx);
        int bottom = Math.Min(height, r.Y + r.Height + dy);
        return new WindowRect(left, top, right - left, bottom - top);
    }

    private static ulong RowBits(ReadOnlySpan<ulong> hash, int row)
    {
        ulong word = hash[row / 2];
        return row % 2 == 0 ? word >> 32 : word & 0xFFFFFFFFUL;
    }

    private static WindowRect? LargestComponentBounds(bool[,] cells, int width, int height, bool eightConnected)
    {
        var seen = new bool[Rows, Cols];
        var stack = new Stack<(int Row, int Col)>();
        int bestCount = 0;
        (int MinR, int MinC, int MaxR, int MaxC) best = default;
        for (int r0 = 0; r0 < Rows; r0++)
        {
            for (int c0 = 0; c0 < Cols; c0++)
            {
                if (!cells[r0, c0] || seen[r0, c0])
                {
                    continue;
                }

                int count = 0;
                (int MinR, int MinC, int MaxR, int MaxC) box = (r0, c0, r0, c0);
                seen[r0, c0] = true;
                stack.Push((r0, c0));
                while (stack.Count > 0)
                {
                    (int r, int c) = stack.Pop();
                    count++;
                    box = (Math.Min(box.MinR, r), Math.Min(box.MinC, c), Math.Max(box.MaxR, r), Math.Max(box.MaxC, c));
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if ((dr == 0 && dc == 0) || (!eightConnected && dr != 0 && dc != 0))
                            {
                                continue;
                            }

                            int nr = r + dr;
                            int nc = c + dc;
                            if (nr >= 0 && nr < Rows && nc >= 0 && nc < Cols && cells[nr, nc] && !seen[nr, nc])
                            {
                                seen[nr, nc] = true;
                                stack.Push((nr, nc));
                            }
                        }
                    }
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    best = box;
                }
            }
        }

        if (bestCount < MinCells)
        {
            return null;
        }

        int x = best.MinC * width / Cols;
        int y = best.MinR * height / Rows;
        int right = (best.MaxC + 1) * width / Cols;
        int bottom = (best.MaxR + 1) * height / Rows;
        double fraction = (right - x) * (double)(bottom - y) / ((double)width * height);
        return fraction < MinAreaFraction || fraction > MaxAreaFraction ? null : new WindowRect(x, y, right - x, bottom - y);
    }
}
