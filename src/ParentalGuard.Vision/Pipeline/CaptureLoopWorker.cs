using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using Vortice.DXGI;

namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Vòng lặp Capture-Inference (Architecture/05 mục 3.3/3.5) — chạy trên 1 <see cref="Thread"/>
/// chuyên dụng, <c>IsBackground = true</c>, KHÔNG dùng <c>Task.Run</c>/ThreadPool (ADR-38: native
/// blocking call không được chiếm giữ ThreadPool worker; <c>ID3D11DeviceContext</c> không
/// thread-safe — 1 thread cố định loại bỏ nhu cầu lock).
/// </summary>
public sealed class CaptureLoopWorker
{
    // Ngưỡng lỗi liên tiếp trước khi tự thoát có kiểm soát thay vì lặp vô hạn với pipeline nghi
    // ngờ hỏng (Việc 2, security-privacy-auditor/test-runner 2026-09-18) — không có con số nào được
    // spec chốt sẵn, chọn đủ lớn để bỏ qua lỗi thoáng qua (vd 1 frame lỗi do cửa sổ đổi kích thước
    // giữa chừng) nhưng đủ nhỏ để không giám sát "chết lâm sàng" quá lâu mà Service không biết.
    private const int _maxConsecutiveFailures = 10;

    // ADR-65: dispose OutputCaptureContext sau đúng số này chu kỳ capture LIÊN TIẾP không có
    // candidate nào trên output đó (tránh dispose/recreate rung lắc khi cửa sổ dao động qua lại
    // biên 2 màn hình).
    private const int _idleDisposeThreshold = 5;

    private readonly VisionRuntimeConfigHolder _configHolder;
    private readonly FrameClassificationPipeline _pipeline;
    private readonly IpcChildClient _ipcClient;
    private readonly OutputCaptureContextPool _contextPool;
    private WindowMessagePump? _messagePump;
    private readonly ManualResetEventSlim _wakeEvent = new(initialState: false);
    private ulong _frameId;
    private Thread? _thread;
    private int _consecutiveFailures;

    /// <summary>
    /// <paramref name="initialOutputContext"/>: context đã tạo sẵn cho output (adapter 0, output 0)
    /// — chính là cặp capture/cropper đã dùng để probe Integrity Level trước khi vòng lặp bắt đầu
    /// (Architecture/05 mục 8.1) — tái dùng thay vì tạo trùng 1 <c>ID3D11Device</c> thứ 2 cho cùng
    /// output đó ngay chu kỳ đầu tiên.
    /// </summary>
    public CaptureLoopWorker(VisionRuntimeConfigHolder configHolder, FrameClassificationPipeline pipeline, IpcChildClient ipcClient, OutputCaptureContext initialOutputContext)
    {
        _configHolder = configHolder;
        _pipeline = pipeline;
        _ipcClient = ipcClient;
        _contextPool = new OutputCaptureContextPool(CreateOutputCaptureContext, _idleDisposeThreshold, seedOutputIndex: 0, initialOutputContext);
    }

    /// <summary>
    /// Architecture/05 mục 3.6 (ADR-133) — gán SAU khi tạo (vì <see cref="WindowMessagePump"/> cần
    /// <see cref="WakeUp"/> của chính instance này để khởi tạo, phá vòng phụ thuộc constructor). Gọi
    /// trước <see cref="Start"/>; bỏ qua (giữ <c>null</c>) hợp lệ cho test — chỉ mất tối ưu re-enumerate
    /// theo <c>WM_DISPLAYCHANGE</c>, không ảnh hưởng tính đúng đắn.
    /// </summary>
    public void AttachMessagePump(WindowMessagePump messagePump) => _messagePump = messagePump;

    private static OutputCaptureContext? CreateOutputCaptureContext()
    {
        try
        {
            var capture = new DesktopDuplicationCapture();
            return new OutputCaptureContext(capture, new GpuWindowCropper(capture.Device));
        }
        catch (CaptureInitializationException)
        {
            // Fail-secure (Architecture/01 mục 5, defense in depth): lỗi khởi tạo capture cho 1
            // output KHÔNG phải lần thử đầu tiên toàn tiến trình (probe IL chỉ áp dụng cho output
            // 0/0 trước khi CaptureLoopWorker chạy — mục 8.1) nên KHÔNG coi là tín hiệu Integrity
            // Level — trả về null để caller bỏ qua candidate này chu kỳ này, tiếp tục giám sát các
            // output/candidate khác thay vì thoát toàn bộ Vision vì 1 màn hình lỗi tạm thời/vừa rút.
            return null;
        }
    }

    /// <summary>Đánh thức ngay lập tức khi có <c>ControlVisionCommand</c> mới — đổi <c>capture_interval_ms</c>/<c>monitoring_enabled</c> có hiệu lực ngay, không chờ hết interval cũ.</summary>
    public void WakeUp() => _wakeEvent.Set();

    public void Start(CancellationToken cancellationToken)
    {
        _thread = new Thread(() => Run(cancellationToken))
        {
            IsBackground = true,
            Name = "ParentalGuard.Vision.CaptureInference",
        };
        _thread.Start();
    }

    private void Run(CancellationToken cancellationToken)
    {
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        while (!cancellationToken.IsCancellationRequested)
        {
            VisionRuntimeConfig config = _configHolder.Current;
            if (!config.MonitoringEnabled)
            {
                WaitOnEvent(Timeout.Infinite, cancellationToken);
                continue;
            }

            // Mục 3.6 (ADR-133): tiêu thụ cờ WM_DISPLAYCHANGE trước khi enumerate — hiện tại
            // MonitorSelector.EnumerateOutputs đã chạy lại MỖI chu kỳ (không cache), nên bản thân việc
            // tiêu thụ cờ ở đây không đổi hành vi enumerate hiện có, chỉ giữ đúng hợp đồng thiết kế
            // (không để cờ tồn đọng) cho lúc enumerate được cache trong tương lai.
            _messagePump?.ConsumeDisplayChanged();

            IReadOnlyList<MonitorSelector.OutputInfo> outputs = MonitorSelector.EnumerateOutputs(factory);
            bool foregroundExcludedNoCandidates = ProcessCycle(outputs, config.ExcludeProcessNames);
            if (foregroundExcludedNoCandidates)
            {
                // Đợt 7 (Architecture/05 mục 3.3 v0.3.0, PERF-010 dòng 1): foreground đang bị exclude-list
                // VÀ không còn candidate nào khác (kể cả trên các màn hình khác) để giám sát — park hẳn,
                // chỉ WindowMessagePump (EVENT_SYSTEM_FOREGROUND) hoặc ControlVisionCommand mới đánh thức.
                // KHÔNG áp dụng khi candidates vẫn còn (multi-monitor, BE-073a) hay khi lý do rỗng candidate
                // là tạm thời/khác (vd không có cửa sổ foreground) — các case đó tự phục hồi ở interval kế tiếp.
                WaitOnEvent(Timeout.Infinite, cancellationToken);
                continue;
            }

            WaitOnEvent(config.CaptureIntervalMs, cancellationToken);
        }

        _contextPool.DisposeAll();
    }

    /// <returns><c>true</c> nếu foreground bị exclude-list và chu kỳ này không có candidate nào — caller nên park vô hạn (mục 3.3/3.6).</returns>
    private bool ProcessCycle(IReadOnlyList<MonitorSelector.OutputInfo> outputs, IReadOnlyList<string> excludeProcessNames)
    {
        IntPtr fgHwnd = ForegroundWindowTracker.GetForegroundWindowHandle();
        string? fgProcessName = ForegroundWindowTracker.ResolveProcessName(fgHwnd);
        bool fgInExcludeList = fgHwnd != IntPtr.Zero && ExcludeProcessMatcher.IsExcluded(fgProcessName, excludeProcessNames);
        bool fgExcluded = fgHwnd == IntPtr.Zero || fgInExcludeList;

        // BE-082/PERF-020: nhánh EnumWindows chỉ chạy khi thật sự có > 1 màn hình — outputs.Count == 1
        // (phổ biến nhất) giữ nguyên chi phí y hệt Đợt 1 (Architecture/05 mục 3.5).
        IReadOnlyList<IntPtr> windowsInZOrder = outputs.Count > 1
            ? WindowZOrderEnumerator.EnumerateTopLevelWindowsInZOrder()
            : [];

        IReadOnlyList<IntPtr> candidates = CandidateWindowSelector.SelectCandidates(
            fgHwnd,
            fgExcluded,
            outputs.Count,
            windowsInZOrder,
            MonitorSelector.GetMonitorForWindow,
            w => CandidateWindowChecks.IsVisibleTopLevelWindow(w) && !ExcludeProcessMatcher.IsExcluded(ForegroundWindowTracker.ResolveProcessName(w), excludeProcessNames));

        var usedOutputIndexes = new HashSet<int>();
        var usedWindowHandles = new HashSet<IntPtr>();
        foreach (IntPtr hwnd in candidates)
        {
            MonitorSelector.OutputInfo? output = MonitorSelector.ResolveOutputForWindow(outputs, hwnd);
            if (output is null)
            {
                continue;
            }

            OutputCaptureContext? context = _contextPool.GetOrCreate(output.Value.OutputIndex);
            if (context is null)
            {
                continue;
            }

            usedOutputIndexes.Add(output.Value.OutputIndex);
            usedWindowHandles.Add(hwnd);
            ProcessOneFrame(context, hwnd, output.Value.AdapterIndex, output.Value.OutputIndex);
        }

        // Mục 3.8.2 (cùng ngưỡng ADR-65): evict hash của cửa sổ không còn candidate sau 5 chu kỳ liên tiếp.
        _pipeline.EndCaptureCycle(usedWindowHandles);

        _contextPool.EndCycle(usedOutputIndexes);

        return ShouldParkInfinitely(fgInExcludeList, candidates.Count);
    }

    /// <summary>
    /// Hàm thuần (Architecture/05 mục 3.3 v0.3.0): chỉ park vô hạn khi foreground THẬT SỰ nằm trong
    /// exclude-list VÀ chu kỳ này không còn candidate nào khác (kể cả trên màn hình khác) — phân biệt
    /// với case rỗng candidate vì lý do khác (vd tạm thời không có cửa sổ foreground) vốn có thể tự
    /// phục hồi ở interval kế tiếp mà không cần <c>EVENT_SYSTEM_FOREGROUND</c>.
    /// </summary>
    internal static bool ShouldParkInfinitely(bool foregroundInExcludeList, int candidateCount) =>
        foregroundInExcludeList && candidateCount == 0;

    private void ProcessOneFrame(OutputCaptureContext context, IntPtr hwnd, int adapterIndex, int outputIndex)
    {
        ulong frameId = ++_frameId;
        VisionInferenceResult? result;
        try
        {
            result = _pipeline.Process(context.Capture, context.Cropper, hwnd, adapterIndex, outputIndex, frameId);
        }
        catch (Exception)
        {
            // Crash-guard: pipeline.Process không có try/catch trước đây — exception bay lên Run()
            // (Thread riêng, ADR-38) làm crash toàn bộ Vision.exe. Không log dữ liệu ảnh/exception
            // detail (Vision không ghi file, BE-022/SEC-017) — chỉ đếm liên tiếp + báo qua
            // HeartbeatAck.DiagnosticState giống PERF-030/031.
            _consecutiveFailures++;
            _ipcClient.DiagnosticState = $"pipeline-error(count={_consecutiveFailures})";
            if (_consecutiveFailures >= _maxConsecutiveFailures)
            {
                // Fail-secure: thoát có kiểm soát để Service respawn tiến trình sạch, thay vì tiếp
                // tục vòng lặp với pipeline nghi ngờ hỏng liên tục.
                Environment.Exit(VisionExitCodes.PipelineRepeatedFailure);
            }

            return;
        }

        _consecutiveFailures = 0;
        if (result is null)
        {
            return;
        }

        IpcPayload payload = _ipcClient.NewEnvelope();
        payload.VisionResult = result;
        _ipcClient.EnqueueOutbound(payload);
    }

    private void WaitOnEvent(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        try
        {
            _wakeEvent.Wait(millisecondsTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _wakeEvent.Reset();
    }
}
