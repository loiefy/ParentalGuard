using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;

namespace ParentalGuard.Vision.Tests;

public class FrameResizerNormalizerTests
{
    // 2x2 BGRA8: (0,0)=đỏ thuần, (1,0)=xanh lá thuần, (0,1)=xanh dương thuần, (1,1)=trắng.
    private static byte[] BuildSourcePixels() =>
    [
        // row 0: (0,0) B G R A, (1,0) B G R A
        0, 0, 255, 255, /**/ 0, 255, 0, 255,
        // row 1: (0,1) B G R A, (1,1) B G R A
        255, 0, 0, 255, /**/ 255, 255, 255, 255,
    ];

    [Fact]
    public void Resize_SameSizeNhwc_IsExactPassthroughNormalized()
    {
        byte[] source = BuildSourcePixels();
        var destination = new DenseTensor<float>([1, 2, 2, 3]);

        FrameResizerNormalizer.Resize(source, sourceWidth: 2, sourceHeight: 2, destination, TensorLayout.Nhwc);

        // (0,0) đỏ: R=1, G=-1, B=-1 theo công thức (pixel/127.5)-1.
        Assert.Equal(1f, destination[0, 0, 0, 0], precision: 3);
        Assert.Equal(-1f, destination[0, 0, 0, 1], precision: 3);
        Assert.Equal(-1f, destination[0, 0, 0, 2], precision: 3);

        // (1,1) trắng: R=G=B=1.
        Assert.Equal(1f, destination[0, 1, 1, 0], precision: 3);
        Assert.Equal(1f, destination[0, 1, 1, 1], precision: 3);
        Assert.Equal(1f, destination[0, 1, 1, 2], precision: 3);
    }

    [Fact]
    public void Resize_SameSizeNchw_WritesCorrectPlanarLayout()
    {
        byte[] source = BuildSourcePixels();
        var destination = new DenseTensor<float>([1, 3, 2, 2]);

        FrameResizerNormalizer.Resize(source, sourceWidth: 2, sourceHeight: 2, destination, TensorLayout.Nchw);

        // (0,0) đỏ: channel 0 (R) = 1, channel 1 (G) = -1, channel 2 (B) = -1.
        Assert.Equal(1f, destination[0, 0, 0, 0], precision: 3);
        Assert.Equal(-1f, destination[0, 1, 0, 0], precision: 3);
        Assert.Equal(-1f, destination[0, 2, 0, 0], precision: 3);
    }

    [Fact]
    public void Resize_UpscaleDoesNotThrow_AndStaysWithinNormalizedRange()
    {
        byte[] source = BuildSourcePixels();
        var destination = new DenseTensor<float>([1, 4, 4, 3]);

        FrameResizerNormalizer.Resize(source, sourceWidth: 2, sourceHeight: 2, destination, TensorLayout.Nhwc);

        foreach (float value in destination.Buffer.Span)
        {
            Assert.InRange(value, -1f, 1f);
        }
    }

    [Fact]
    public void Resize_SourceBufferTooSmall_Throws()
    {
        byte[] tooSmall = new byte[4];
        var destination = new DenseTensor<float>([1, 2, 2, 3]);

        Assert.Throws<ArgumentException>(() => FrameResizerNormalizer.Resize(tooSmall, 2, 2, destination, TensorLayout.Nhwc));
    }
}
