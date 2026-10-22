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

    /// <summary>
    /// `IMG-016`: chỉ chấm thêm 5 vùng con khi điểm cả cửa sổ đạt mức sàn này (giới hạn CPU — `PERF-032a`). Đo trên máy chủ
    /// dự án 2026-10-06 (Marqo): cửa sổ phát video khiêu dâm thật có điểm cả cửa sổ thấp nhất 0.13, cửa sổ bình thường trung vị 0.07.
    /// </summary>
    public const float DefaultSubRegionGate = 0.10f;

    private readonly float _subRegionGate;

    // DEV-051: vị trí crop so với góc trên-trái cửa sổ (crop có thể bị cắt ở mép màn hình) — đổi vùng con sang toạ độ cửa sổ.
    private (int X, int Y) _lastCropOffsetInWindow;

    private readonly bool _detectContentRegion;

    // IMG-016c: null = không có model NudeNet (file thiếu) — giữ kết quả của Marqo, tức chặn nhiều hơn chứ không ít hơn.
    private readonly IBodyPartDetector? _detector;

    // DEV-051b: điểm Marqo cao nhất của khung đang chấm, trước lọc màu da và NudeNet — chỉ để hiển thị ở chế độ developer.
    private float _rawMarqoMax;
    private readonly DenseTensor<float>? _detectorTensor;

    /// <summary>`IMG-016d`: NudeNet tìm thấy bộ phận cơ thể (bất kỳ nhãn nào) từ độ tin cậy này — đúng mức thấp nhất NudeNet tự báo 1 phát hiện (NMS 0.25).</summary>
    public const float BodyPartConfidenceThreshold = NudeNetDetector.MinReportedConfidence;

    /// <summary>`IMG-016c`: số vùng vượt ngưỡng tối đa được xác minh mỗi khung (mỗi lần ~170 ms DirectML, ~0.9 s CPU).</summary>
    public const int MaxVerifiedRegions = 2;

    /// <summary>`IMG-016c`: không tìm thấy bộ phận nhạy cảm → điểm cửa sổ = ngưỡng chặn × hệ số này (0.49 với ngưỡng 0.7).</summary>
    public const float RejectedScoreFactor = 0.7f;

    public FrameClassificationPipeline(INsfwClassifier classifier, IFrameBufferAuditor? auditor = null, float subRegionGate = DefaultSubRegionGate, bool detectContentRegion = true, IBodyPartDetector? detector = null)
    {
        _detectContentRegion = detectContentRegion;
        _classifier = classifier;
        _detector = detector;
        _auditor = auditor ?? NullFrameBufferAuditor.Instance;
        _subRegionGate = subRegionGate;

        // ADR-43: cấp phát đúng 1 lần lúc khởi tạo, tái dùng suốt vòng đời process — kích thước
        // luôn cố định 224x224x3 (IMG-014), không phụ thuộc kích thước cửa sổ.
        int size = classifier.InputSize; // 224 (GantMan/Falconsai) hoặc 384 (Marqo) — cờ build ParentalGuardModel
        int[] shape = classifier.InputLayout == TensorLayout.Nhwc ? [1, size, size, 3] : [1, 3, size, size];
        _inputTensor = new DenseTensor<float>(shape);
        _auditor.OnBufferAllocated("input_tensor", _inputTensor.Buffer.Length * sizeof(float));

        if (detector is not null)
        {
            _detectorTensor = new DenseTensor<float>([1, 3, detector.InputSize, detector.InputSize]);
            _auditor.OnBufferAllocated("detector_input_tensor", _detectorTensor.Buffer.Length * sizeof(float));
        }
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
    /// <param name="riskThreshold">Ngưỡng chặn hiện hành (`BE-090`, từ <c>ControlVisionCommand</c>) — điểm từ đây trở lên mới cần NudeNet xác minh (`IMG-016c`).</param>
    public VisionInferenceResult? Process(IFrameCapture capture, IWindowCropper cropper, IntPtr hwnd, int adapterIndex, int outputIndex, WindowRect outputBounds, ulong frameId, IReadOnlyList<WindowRect>? occluders = null, float riskThreshold = 0.7f)
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

        _lastCropOffsetInWindow = (cropOnDesktop.X - rect.Value.X, cropOnDesktop.Y - rect.Value.Y);

#if PARENTALGUARD_FAST_DETECTION
        IDisposable? fullScreenFrame = capture.AcquireNextFrame(adapterIndex, outputIndex, timeoutMs: 50);
#else
        IDisposable? fullScreenFrame = capture.AcquireNextFrame(adapterIndex, outputIndex, timeoutMs: 500);
#endif
        if (fullScreenFrame is null)
        {
            return null;
        }

        return ProcessFrame(capture, cropper, rect.Value, fullScreenFrame, hwnd, outputIndex, frameId, capturedAtUnixMs, cropRect.Value, masks, riskThreshold);
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
    internal VisionInferenceResult? ProcessFrame(IFrameCapture capture, IWindowCropper cropper, WindowRect rect, IDisposable fullScreenFrame, IntPtr hwnd, int outputIndex, ulong frameId, long capturedAtUnixMs, WindowRect? cropRect = null, IReadOnlyList<WindowRect>? occlusionMasks = null, float riskThreshold = 0.7f)
    {
        WindowRect crop = cropRect ?? rect;
        bool contentChanged = false;
        var debugRegions = new List<DebugRegionScore>();
        var detections = new List<DebugDetection>();
        bool detectorRan = false;
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
            Span<ulong> previousHash = stackalloc ulong[PerceptualHash.BlockHashWords];
            bool hasPreviousHash = _hashCache.TryCopyPreviousHash(hwnd, previousHash);
            contentChanged = _hashCache.ResolveContentChanged(hwnd, newHash, out float cachedRiskScore);
            riskScore = cachedRiskScore;

            if (contentChanged)
            {
                // IMG-016: max(cả cửa sổ, 5 vùng con) — phải chạy TRƯỚC khi zero buffer pixel bên dưới.
                var whole = new WindowRect(0, 0, crop.Width, crop.Height);
                _rawMarqoMax = 0f;
                riskScore = ClassifyRegion(crop.Width, crop.Height, whole);
                var scoredRegions = new List<(WindowRect Region, float Score)>(7) { (whole, riskScore) };
                if (riskScore >= _subRegionGate)
                {
                    foreach (WindowRect region in SubRegions(crop.Width, crop.Height))
                    {
                        float regionScore = ClassifyRegion(crop.Width, crop.Height, region);
                        scoredRegions.Add((region, regionScore));
                        riskScore = Math.Max(riskScore, regionScore);
#if PARENTALGUARD_DEVELOPER_MODE
                        debugRegions.Add(new DebugRegionScore
                        {
                            Rect = new Rect { X = region.X + _lastCropOffsetInWindow.X, Y = region.Y + _lastCropOffsetInWindow.Y, Width = region.Width, Height = region.Height },
                            RiskScore = regionScore,
                        });
#endif
                    }
                }

                // IMG-016a: vùng con thứ 6 — vùng video/ảnh phát hiện động; LUÔN chấm khi tìm được (không qua mức sàn).
                if (_detectContentRegion && ContentRegionDetector.Detect(_pixelBuffer, crop.Width, crop.Height, hasPreviousHash ? previousHash : [], newHash) is { } content)
                {
                    float contentScore = ClassifyRegion(crop.Width, crop.Height, content);
                    scoredRegions.Add((content, contentScore));
                    riskScore = Math.Max(riskScore, contentScore);
#if PARENTALGUARD_DEVELOPER_MODE
                    debugRegions.Add(new DebugRegionScore
                    {
                        Rect = new Rect { X = content.X + _lastCropOffsetInWindow.X, Y = content.Y + _lastCropOffsetInWindow.Y, Width = content.Width, Height = content.Height },
                        RiskScore = contentScore,
                        IsContentRegion = true,
                    });
#endif
                }

                // IMG-016c: Marqo báo vi phạm → NudeNet xác minh trên chính vùng đó; buffer pixel vẫn còn nguyên ở đây.
                detectorRan = riskScore >= riskThreshold && _detector is not null;
                if (detectorRan && !ConfirmBodyParts(crop.Width, crop.Height, scoredRegions, riskThreshold, detections))
                {
                    riskScore = riskThreshold * RejectedScoreFactor;
                }

                _hashCache.UpdateRiskScore(hwnd, riskScore);
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

        var result = new VisionInferenceResult
        {
            FrameId = frameId,
            WindowHandle = unchecked((ulong)hwnd.ToInt64()),
            MonitorId = (uint)outputIndex,
            RiskScore = riskScore,
            Bbox = new Rect { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height },
            CapturedAtUnixMs = capturedAtUnixMs,
            ContentChanged = contentChanged,
        };
        result.DebugRegions.AddRange(debugRegions);
#if PARENTALGUARD_DEVELOPER_MODE
        result.DebugDetections.AddRange(detections);
        result.DebugDetectorRan = detectorRan;
        result.DebugMarqoScore = contentChanged ? _rawMarqoMax : 0f; // khung không đổi: Overlay giữ số cũ
#endif
        return result;
    }

    /// <summary>Resize 1 vùng của buffer pixel hiện hành vào tensor, phân loại, zero tensor ngay (IMG-003).</summary>
    private float ClassifyRegion(int width, int height, WindowRect region)
    {
        try
        {
            FrameResizerNormalizer.Resize(_pixelBuffer, width, height, region, _inputTensor, _classifier.InputLayout);
            float score = RiskScoreAggregator.Aggregate(_classifier.Classify(_inputTensor));
            _rawMarqoMax = Math.Max(_rawMarqoMax, score); // DEV-051b: điểm gốc, trước IMG-016b/016c

            // IMG-016b: chạy khi buffer pixel còn nguyên (trước khi zero ở finally của Process).
            return SkinToneGate.Apply(score, _pixelBuffer, width, region);
        }
        finally
        {
            // IMG-003 (bảng mục 6, dòng "DenseTensor<float> input"): zero sau MỖI lần phân loại.
            _inputTensor.Buffer.Span.Clear();
            _auditor.OnZeroed("input_tensor", _inputTensor.Buffer.Length * sizeof(float));
        }
    }

    /// <summary>
    /// `IMG-016c`/`IMG-016d`: <c>true</c> nếu NudeNet tìm thấy bộ phận cơ thể người (bất kỳ nhãn nào trong 18 nhãn, không cần nhãn
    /// "lộ rõ") trong 1 trong các vùng (điểm cao nhất trước) đã vượt ngưỡng chặn, hoặc không có detector. Kết quả lưu vào <see cref="WindowHashCache"/> cùng điểm — khung không đổi không chạy lại.
    /// </summary>
    /// <param name="detections">`DEV-051a`: vật thể nhận ra ở mọi vùng đã chạy, toạ độ tương đối góc trên-trái cửa sổ.</param>
    private bool ConfirmBodyParts(int width, int height, List<(WindowRect Region, float Score)> scoredRegions, float riskThreshold, List<DebugDetection> detections)
    {
        if (_detector is null || _detectorTensor is null)
        {
            return true;
        }

        foreach ((WindowRect region, _) in scoredRegions.Where(r => r.Score >= riskThreshold).OrderByDescending(r => r.Score).Take(MaxVerifiedRegions))
        {
            try
            {
                FrameResizerNormalizer.ResizeLetterbox(_pixelBuffer, width, height, region, _detectorTensor);
                BodyPartDetectionResult found = _detector.Detect(_detectorTensor);
                detections.AddRange(found.Detections.Select(d => ToWindowDetection(d, region, _detector.InputSize)));
                if (found.Detections.Any(d => d.Confidence >= BodyPartConfidenceThreshold))
                {
                    return true;
                }
            }
            finally
            {
                // IMG-003: zero tensor đầu vào detector sau MỖI lần chạy, như tensor của classifier.
                _detectorTensor.Buffer.Span.Clear();
                _auditor.OnZeroed("detector_input_tensor", _detectorTensor.Buffer.Length * sizeof(float));
            }
        }

        return false;
    }

    /// <summary>Khung trong ảnh letterbox của detector (cạnh <paramref name="inputSize"/>) → toạ độ cửa sổ, cắt về trong vùng.</summary>
    internal DebugDetection ToWindowDetection(BodyPartDetection detection, WindowRect region, int inputSize)
    {
        float ratio = Math.Max(region.Width, region.Height) / (float)inputSize;
        int left = Math.Clamp((int)MathF.Round(detection.X * ratio), 0, region.Width);
        int top = Math.Clamp((int)MathF.Round(detection.Y * ratio), 0, region.Height);
        int right = Math.Clamp((int)MathF.Round((detection.X + detection.Width) * ratio), left, region.Width);
        int bottom = Math.Clamp((int)MathF.Round((detection.Y + detection.Height) * ratio), top, region.Height);
        return new DebugDetection
        {
            Label = detection.Label,
            Confidence = detection.Confidence,
            IsExposed = detection.IsExposed,
            Rect = new Rect
            {
                X = region.X + left + _lastCropOffsetInWindow.X,
                Y = region.Y + top + _lastCropOffsetInWindow.Y,
                Width = right - left,
                Height = bottom - top,
            },
        };
    }

    /// <summary>`IMG-016`: 4 vùng góc chồng lấn (60%×60%) + 1 vùng giữa — đúng bộ vùng đã đánh giá trên máy chủ dự án 2026-10-06.</summary>
    internal static IReadOnlyList<WindowRect> SubRegions(int width, int height)
    {
        int w = Math.Max(1, (int)(width * 0.6));
        int h = Math.Max(1, (int)(height * 0.6));
        int right = width - w;
        int bottom = height - h;
        return
        [
            new WindowRect(0, 0, w, h),
            new WindowRect(right, 0, w, h),
            new WindowRect(0, bottom, w, h),
            new WindowRect(right, bottom, w, h),
            new WindowRect((int)(width * 0.2), (int)(height * 0.15), Math.Max(1, (int)(width * 0.6)), Math.Max(1, (int)(height * 0.8))),
        ];
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
