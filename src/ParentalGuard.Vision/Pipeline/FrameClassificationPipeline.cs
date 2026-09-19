using Microsoft.ML.OnnxRuntime.Tensors;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Inference;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Điều phối bước 1-6 (Architecture/05 mục 2) cho đúng 1 cửa sổ/1 chu kỳ — gọi từ Thread
/// Capture-Inference chuyên dụng (ADR-38), không tự đọc/ghi IPC. <see cref="IFrameCapture"/>/
/// <see cref="IWindowCropper"/> nhận theo tham số mỗi lần gọi (không giữ ở constructor) vì mỗi
/// output DXGI có 1 cặp capture/cropper riêng (Architecture/05 mục 3.5, `OutputCaptureContext`) —
/// trong khi tensor/classifier vẫn dùng chung 1 instance suốt vòng đời process bất kể màn hình
/// nào đang xử lý (ADR-43/44), vì xử lý đa cửa sổ luôn TUẦN TỰ (`BE-086`), không song song.
/// </summary>
public sealed class FrameClassificationPipeline
{
    private readonly INsfwClassifier _classifier;
    private readonly IFrameBufferAuditor _auditor;
    private readonly DenseTensor<float> _inputTensor;

    private byte[] _pixelBuffer = [];

    public FrameClassificationPipeline(INsfwClassifier classifier, IFrameBufferAuditor? auditor = null)
    {
        _classifier = classifier;
        _auditor = auditor ?? NullFrameBufferAuditor.Instance;

        // ADR-43: cấp phát đúng 1 lần lúc khởi tạo, tái dùng suốt vòng đời process — kích thước
        // luôn cố định 224x224x3 (IMG-014), không phụ thuộc kích thước cửa sổ.
        int[] shape = classifier.InputLayout == TensorLayout.Nhwc ? [1, 224, 224, 3] : [1, 3, 224, 224];
        _inputTensor = new DenseTensor<float>(shape);
        _auditor.OnBufferAllocated("input_tensor", _inputTensor.Buffer.Length * sizeof(float));
    }

    /// <summary>
    /// <c>null</c> nếu không có gì để phân tích chu kỳ này (không có cửa sổ foreground hợp lệ,
    /// <c>AcquireNextFrame</c> timeout, hoặc cửa sổ đã đóng giữa chừng) — đây là hành vi bình
    /// thường (mục 4.1), không phải lỗi.
    /// </summary>
    public VisionInferenceResult? Process(IFrameCapture capture, IWindowCropper cropper, IntPtr hwnd, int adapterIndex, int outputIndex, ulong frameId)
    {
        long capturedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        WindowRect? rect = WindowRectResolver.Resolve(hwnd);
        if (rect is null)
        {
            return null;
        }

        IDisposable? fullScreenFrame = capture.AcquireNextFrame(adapterIndex, outputIndex, timeoutMs: 500);
        if (fullScreenFrame is null)
        {
            return null;
        }

        return ProcessFrame(capture, cropper, rect.Value, fullScreenFrame, hwnd, outputIndex, frameId, capturedAtUnixMs);
    }

    /// <summary>
    /// Thân xử lý thật (crop → resize/normalize → classify), tách khỏi <see cref="Process"/> để
    /// test được bất biến zero-out mà không cần DWM/DXGI thật — chỉ cần fake <see cref="IWindowCropper"/>/
    /// <see cref="INsfwClassifier"/> (regression guard cho bug 2026-09-18, TEST-001).
    /// </summary>
    internal VisionInferenceResult ProcessFrame(IFrameCapture capture, IWindowCropper cropper, WindowRect rect, IDisposable fullScreenFrame, IntPtr hwnd, int outputIndex, ulong frameId, long capturedAtUnixMs)
    {
        NsfwClassProbabilities probabilities;
        try
        {
            try
            {
                try
                {
                    EnsurePixelBuffer(rect.Width, rect.Height);
                    try
                    {
                        cropper.CropAndReadBack(fullScreenFrame, rect, _pixelBuffer);
                    }
                    finally
                    {
                        // ADR-42: trả quyền sở hữu frame toàn màn hình lại cho OS càng sớm càng tốt.
                        capture.ReleaseFrame();
                    }
                }
                finally
                {
                    fullScreenFrame.Dispose();
                }

                FrameResizerNormalizer.Resize(_pixelBuffer, rect.Width, rect.Height, _inputTensor, _classifier.InputLayout);
            }
            finally
            {
                // IMG-003 (Architecture/05 mục 6, bảng dòng "byte[] pixel BGRA8"): zero NGAY SAU khi
                // FrameResizerNormalizer đọc xong, KHÔNG chờ Classify (ONNX inference) chạy xong mới
                // zero — finally này bao trọn cả crop lẫn resize (không riêng resize) để vẫn giữ đúng
                // bất biến "zero vô điều kiện dù bước nào ở trên throw" (regression guard bug
                // 2026-09-18, TEST-001: cropper throw giữa chừng sau khi đã ghi 1 phần dữ liệu ảnh
                // thật vào _pixelBuffer vẫn phải được zero).
                Array.Clear(_pixelBuffer);
                _auditor.OnZeroed("pixel_buffer_bgra8", _pixelBuffer.Length);
            }

            probabilities = _classifier.Classify(_inputTensor);
        }
        finally
        {
            // IMG-003 (bảng mục 6, dòng "DenseTensor<float> input"): zero NGAY SAU session.Run trả
            // về, vô điều kiện dù bước nào ở trên (crop/resize/classify) throw giữa chừng — cùng bất
            // biến regression guard bug 2026-09-18 áp dụng riêng cho tensor này.
            _inputTensor.Buffer.Span.Clear();
            _auditor.OnZeroed("input_tensor", _inputTensor.Buffer.Length * sizeof(float));
        }

        float riskScore = RiskScoreAggregator.Aggregate(probabilities);

        return new VisionInferenceResult
        {
            FrameId = frameId,
            WindowHandle = unchecked((ulong)hwnd.ToInt64()),
            MonitorId = (uint)outputIndex,
            RiskScore = riskScore,
            Bbox = new Rect { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height },
            CapturedAtUnixMs = capturedAtUnixMs,
        };
    }

    private void EnsurePixelBuffer(int width, int height)
    {
        int required = width * height * 4;
        if (_pixelBuffer.Length == required)
        {
            return;
        }

        if (_pixelBuffer.Length > 0)
        {
            // Mảng cũ bỏ tham chiếu ngay sau đây — zero trước để không để lại object "mồ côi"
            // chứa dữ liệu ảnh thô chờ GC dọn tại thời điểm bất định (bảng mục 6, ADR-43).
            Array.Clear(_pixelBuffer);
        }

        _pixelBuffer = new byte[required];
        _auditor.OnBufferAllocated("pixel_buffer_bgra8", required);
    }

    /// <summary>Test hook (IMG-040/041 regression guard) — không dùng ở code Production.</summary>
    internal byte[] PixelBufferForTest => _pixelBuffer;

    /// <summary>Test hook (IMG-040/041 regression guard) — không dùng ở code Production.</summary>
    internal DenseTensor<float> InputTensorForTest => _inputTensor;
}
