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
    private readonly WindowHashCache _hashCache = new();

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
    /// <param name="occluders">
    /// Bounds (toạ độ virtual desktop) của các cửa sổ đang hiển thị nằm TRÊN <paramref name="hwnd"/> theo Z-order. Bug
    /// real-hardware 2026-10-06: Desktop Duplication là ảnh màn hình ĐÃ GHÉP — crop theo khung cửa sổ lấy luôn nội dung
    /// cửa sổ khác đè lên trên, rồi gán điểm (và overlay) nhầm cho cửa sổ bên dưới. Phần bị che được tô đen trước khi
    /// phân loại; còn lộ ra dưới <see cref="MinVisibleFraction"/> thì bỏ qua chu kỳ này.
    /// </param>
    public VisionInferenceResult? Process(IFrameCapture capture, IWindowCropper cropper, IntPtr hwnd, int adapterIndex, int outputIndex, WindowRect outputBounds, ulong frameId, IReadOnlyList<WindowRect>? occluders = null)
    {
        long capturedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        WindowRect? rect = WindowRectResolver.Resolve(hwnd);
        if (rect is null)
        {
            return null;
        }

        WindowRect? cropRect = WindowRectResolver.ToOutputLocalCrop(rect.Value, outputBounds);
        if (cropRect is null)
        {
            return null;
        }

        var cropOnDesktop = new WindowRect(outputBounds.X + cropRect.Value.X, outputBounds.Y + cropRect.Value.Y, cropRect.Value.Width, cropRect.Value.Height);
        IReadOnlyList<WindowRect> masks = OcclusionMask.ToCropLocal(cropOnDesktop, occluders ?? []);
        if (OcclusionMask.VisibleFraction(cropRect.Value.Width, cropRect.Value.Height, masks) < MinVisibleFraction)
        {
            return null;
        }

#if PARENTALGUARD_FAST_DETECTION
        IDisposable? fullScreenFrame = capture.AcquireNextFrame(adapterIndex, outputIndex, timeoutMs: 50);
#else
        IDisposable? fullScreenFrame = capture.AcquireNextFrame(adapterIndex, outputIndex, timeoutMs: 500);
#endif
        if (fullScreenFrame is null)
        {
            return null;
        }

        return ProcessFrame(capture, cropper, rect.Value, fullScreenFrame, hwnd, outputIndex, frameId, capturedAtUnixMs, cropRect.Value, masks);
    }

    /// <summary>Cửa sổ chỉ còn lộ ra ít hơn tỉ lệ này (phần còn lại bị cửa sổ khác che) → không đủ nội dung của chính nó để phân loại.</summary>
    public const double MinVisibleFraction = 0.10;

    /// <summary>
    /// Thân xử lý thật (crop → resize/normalize → classify), tách khỏi <see cref="Process"/> để
    /// test được bất biến zero-out mà không cần DWM/DXGI thật — chỉ cần fake <see cref="IWindowCropper"/>/
    /// <see cref="INsfwClassifier"/> (regression guard cho bug 2026-09-18, TEST-001).
    /// </summary>
    /// <param name="rect">Toạ độ virtual desktop của cửa sổ — trả nguyên về <c>Bbox</c> cho Overlay.</param>
    /// <param name="cropRect">Vùng crop trong texture của output (đã trừ offset + cắt biên); mặc định = <paramref name="rect"/> (test 1 màn hình tại gốc).</param>
    /// <param name="occlusionMasks">Vùng (toạ độ trong crop) bị cửa sổ khác che — tô đen trước khi hash/phân loại.</param>
    /// <returns><c>null</c> nếu ảnh crop đen hoàn toàn (khung Desktop Duplication chưa có nội dung — gặp ở khung đầu tiên sau khi tạo duplication): không có dữ liệu thật để phân loại.</returns>
    internal VisionInferenceResult? ProcessFrame(IFrameCapture capture, IWindowCropper cropper, WindowRect rect, IDisposable fullScreenFrame, IntPtr hwnd, int outputIndex, ulong frameId, long capturedAtUnixMs, WindowRect? cropRect = null, IReadOnlyList<WindowRect>? occlusionMasks = null)
    {
        WindowRect crop = cropRect ?? rect;
        bool contentChanged = false;
        float riskScore;
        try
        {
            try
            {
                EnsurePixelBuffer(crop.Width, crop.Height);
                try
                {
                    cropper.CropAndReadBack(fullScreenFrame, crop, _pixelBuffer);
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

            // Bug 2026-10-06: khung chưa có nội dung (toàn 0) — bỏ qua, không cập nhật hash/điểm của cửa sổ.
            if (_pixelBuffer.AsSpan(0, crop.Width * crop.Height * 4).IndexOfAnyExcept((byte)0) < 0)
            {
                return null;
            }

            OcclusionMask.Apply(_pixelBuffer, crop.Width, crop.Height, occlusionMasks ?? []);

            // Mục 3.8.1/ADR-129 (v0.3.0): hash-gate NGAY SAU readback, TRƯỚC resize — dùng chung 1 tín
            // hiệu cho cả PERF-010 (Service, qua ContentChanged) và PERF-011 (skip cục bộ dưới đây).
            // Bug 2026-10-06: hash khối 1024 bit thay dHash 72 điểm — xem PerceptualHash.ComputeBlockDHash.
            Span<ulong> newHash = stackalloc ulong[PerceptualHash.BlockHashWords];
            PerceptualHash.ComputeBlockDHash(_pixelBuffer, crop.Width, crop.Height, newHash);
            contentChanged = _hashCache.ResolveContentChanged(hwnd, newHash, out float cachedRiskScore);
            riskScore = cachedRiskScore;

            if (contentChanged)
            {
                FrameResizerNormalizer.Resize(_pixelBuffer, crop.Width, crop.Height, _inputTensor, _classifier.InputLayout);
            }
        }
        finally
        {
            // IMG-003 (Architecture/05 mục 6, dòng "byte[] pixel BGRA8", v0.3.0): zero NGAY SAU CẢ 2
            // bên tiêu thụ đã đọc xong — ComputeDHash64 (luôn chạy) và FrameResizerNormalizer (chỉ
            // chạy nếu content_changed=true) — vô điều kiện dù bước nào ở trên throw (regression guard
            // bug 2026-09-18, TEST-001: cropper throw giữa chừng vẫn phải zero phần đã ghi).
            Array.Clear(_pixelBuffer);
            _auditor.OnZeroed("pixel_buffer_bgra8", _pixelBuffer.Length);
        }

        if (contentChanged)
        {
            NsfwClassProbabilities probabilities;
            try
            {
                probabilities = _classifier.Classify(_inputTensor);
            }
            finally
            {
                // IMG-003 (bảng mục 6, dòng "DenseTensor<float> input", v0.3.0): chỉ áp dụng khi
                // content_changed=true — khi skip, Resize/Classify chưa từng chạm tensor nên không có
                // gì để zero (tensor vẫn nguyên trạng zero từ lần ghi+zero trước đó).
                _inputTensor.Buffer.Span.Clear();
                _auditor.OnZeroed("input_tensor", _inputTensor.Buffer.Length * sizeof(float));
            }

            riskScore = RiskScoreAggregator.Aggregate(probabilities);
            _hashCache.UpdateRiskScore(hwnd, riskScore);
        }

        return new VisionInferenceResult
        {
            FrameId = frameId,
            WindowHandle = unchecked((ulong)hwnd.ToInt64()),
            MonitorId = (uint)outputIndex,
            RiskScore = riskScore,
            Bbox = new Rect { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height },
            CapturedAtUnixMs = capturedAtUnixMs,
            ContentChanged = contentChanged,
        };
    }

    /// <summary>Gọi đúng 1 lần cuối mỗi chu kỳ capture (Architecture/05 mục 3.8.2) — evict hash của cửa sổ không còn candidate.</summary>
    public void EndCaptureCycle(IReadOnlySet<IntPtr> candidateWindowHandles) => _hashCache.EndCycle(candidateWindowHandles);

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
