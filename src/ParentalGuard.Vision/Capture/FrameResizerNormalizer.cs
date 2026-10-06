using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Vision.Inference;

namespace ParentalGuard.Vision.Capture;

/// <summary>
/// Bước 2+3 phần CPU thuần (Architecture/05 mục 4.2, ADR-51): bilinear resize + normalize đọc
/// trực tiếp từ <c>byte[]</c> BGRA8 nguồn, ghi trực tiếp vào <see cref="DenseTensor{T}"/> đích —
/// không qua object ảnh trung gian, giảm 1 buffer cần zero-out.
/// </summary>
public static class FrameResizerNormalizer
{
    /// <summary>
    /// Normalize về [0, 1] qua <c>pixel / 255</c> (ADR-51, đã xác nhận 2026-09-30 trên chính file
    /// <c>models/nsfw_model.onnx</c>): graph ONNX (TF Hub MobileNetV2, tf2onnx) TỰ chứa bước
    /// <c>hub_input/Mul(2.0)</c> → <c>Sub(1.0)</c> ngay sau input, tức model mong đợi [0, 1] và tự đổi
    /// sang [-1, 1] bên trong. Bug đã sửa: bản cũ tự đưa về [-1, 1] trước → model thấy [-3, 1]
    /// (chuẩn hoá 2 lần), phân phối đầu vào lệch hẳn, không phát hiện được nội dung vi phạm.
    /// </summary>
    public static void Resize(ReadOnlySpan<byte> sourceBgra8, int sourceWidth, int sourceHeight, DenseTensor<float> destination, TensorLayout layout) =>
        Resize(sourceBgra8, sourceWidth, sourceHeight, new WindowRect(0, 0, sourceWidth, sourceHeight), destination, layout);

    /// <summary>
    /// `IMG-016` (2026-10-06): như bản trên nhưng chỉ lấy <paramref name="region"/> (toạ độ trong ảnh nguồn) — dùng để chấm
    /// vùng con của cửa sổ mà không cần cấp phát buffer ảnh trung gian.
    /// </summary>
    public static void Resize(ReadOnlySpan<byte> sourceBgra8, int sourceWidth, int sourceHeight, WindowRect region, DenseTensor<float> destination, TensorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentException("Source dimensions must be positive.");
        }

        if (sourceBgra8.Length < sourceWidth * sourceHeight * 4)
        {
            throw new ArgumentException("Source buffer smaller than sourceWidth*sourceHeight*4 (BGRA8).", nameof(sourceBgra8));
        }

        int regionX = Math.Clamp(region.X, 0, sourceWidth - 1);
        int regionY = Math.Clamp(region.Y, 0, sourceHeight - 1);
        int regionWidth = Math.Clamp(region.Width, 1, sourceWidth - regionX);
        int regionHeight = Math.Clamp(region.Height, 1, sourceHeight - regionY);

        (int destHeight, int destWidth) = GetSpatialDimensions(destination.Dimensions, layout);
        Span<float> destSpan = destination.Buffer.Span;

        float xRatio = regionWidth / (float)destWidth;
        float yRatio = regionHeight / (float)destHeight;

        for (int y = 0; y < destHeight; y++)
        {
            float srcYf = ((y + 0.5f) * yRatio) - 0.5f;
            int y0 = regionY + Math.Clamp((int)MathF.Floor(srcYf), 0, regionHeight - 1);
            int y1 = Math.Min(y0 + 1, regionY + regionHeight - 1);
            float fy = Math.Clamp(srcYf - (y0 - regionY), 0f, 1f);

            for (int x = 0; x < destWidth; x++)
            {
                float srcXf = ((x + 0.5f) * xRatio) - 0.5f;
                int x0 = regionX + Math.Clamp((int)MathF.Floor(srcXf), 0, regionWidth - 1);
                int x1 = Math.Min(x0 + 1, regionX + regionWidth - 1);
                float fx = Math.Clamp(srcXf - (x0 - regionX), 0f, 1f);

                for (int channel = 0; channel < 3; channel++)
                {
                    // BGRA8 -> RGB: channel 0(R) đọc byte offset 2, 1(G) offset 1, 2(B) offset 0.
                    int srcByteOffset = 2 - channel;
                    float p00 = sourceBgra8[((y0 * sourceWidth) + x0) * 4 + srcByteOffset];
                    float p10 = sourceBgra8[((y0 * sourceWidth) + x1) * 4 + srcByteOffset];
                    float p01 = sourceBgra8[((y1 * sourceWidth) + x0) * 4 + srcByteOffset];
                    float p11 = sourceBgra8[((y1 * sourceWidth) + x1) * 4 + srcByteOffset];

                    float top = p00 + ((p10 - p00) * fx);
                    float bottom = p01 + ((p11 - p01) * fx);
                    float pixel = top + ((bottom - top) * fy);
                    float normalized = pixel / 255f;

                    destSpan[GetDestinationIndex(layout, destWidth, destHeight, x, y, channel)] = normalized;
                }
            }
        }
    }

    private static (int Height, int Width) GetSpatialDimensions(ReadOnlySpan<int> dimensions, TensorLayout layout)
    {
        if (dimensions.Length != 4)
        {
            throw new ArgumentException("Expected a rank-4 tensor [batch, ...].", nameof(dimensions));
        }

        return layout == TensorLayout.Nhwc ? (dimensions[1], dimensions[2]) : (dimensions[2], dimensions[3]);
    }

    private static int GetDestinationIndex(TensorLayout layout, int width, int height, int x, int y, int channel) => layout == TensorLayout.Nhwc
        ? (((y * width) + x) * 3) + channel
        : (((channel * height) + y) * width) + x;
}
