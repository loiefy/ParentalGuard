# 05 — Image Processing Pipeline Architecture

> Version: v0.3.0 | Trạng thái: Approved | Cập nhật: 2026-09-25

## 1. Mục đích và phạm vi

File này trả lời **HOW** cho toàn bộ pipeline xử lý ảnh mô tả ở `Specification/09-image-processing-spec.md` (capture → crop → resize → inference → decision), threading model của `ParentalGuard.Vision`, và 2 câu hỏi mở đang treo từ các file trước:

- `02-process-architecture.md` mục 8: cơ chế suspend `Vision` lúc Pause.
- `06-security-architecture.md` mục 7 (dòng 1): validate/thiết kế fallback Low Integrity Level ↔ Medium IL cho Desktop Duplication API.

Phạm vi ban đầu (v0.1.0) đúng khung Đợt 1 theo `ROADMAP.md` mục 4 — **1 cửa sổ, 1 màn hình, happy path đầy đủ end-to-end**. **Cập nhật v0.2.0 (Đợt 2)**: mục 3.5 bổ sung multi-monitor/đa cửa sổ phía `Vision` (`BE-080`–`082`, `IMG-020`) — phần Overlay/Service tiêu thụ kết quả này (`BE-083`–`089`, `FE-016`) thiết kế ở `07-overlay-architecture.md`, không lặp ở đây. **Cập nhật v0.3.0 (Đợt 7, `ROADMAP.md` mục 4 "Performance tuning chính thức")**: mục 3.6 (thread thứ 3 — Window Message Pump), mục 3.7 (Adaptive Frame Rate đầy đủ `PERF-010`/`PERF-020`/`021` + Perceptual Hashing `PERF-011`/`IMG-011`) — đóng câu hỏi mở cũ về `WM_DISPLAYCHANGE` (mục 11 trước đây), xác nhận `PERF-020`/`021` đã đủ ở mục 3.3/3.5 ngoại trừ 1 điểm sửa (polling → event-driven cho exclude-list), làm rõ quan hệ `performance_mode` (Đợt 6, `04` ADR-127) ↔ Adaptive Frame Rate (dynamic).

Không phát minh yêu cầu sản phẩm mới. Nơi phải tự quyết định 1 chi tiết kỹ thuật không có trong spec (ví dụ công thức tổng hợp risk score) — `IMG-014` đã tường minh uỷ quyền quyết định đó cho System Design ("chi tiết công thức để ở System Design"), không phải trường hợp thiếu spec cần quay lại `spec-maintainer`.

## 2. Tổng quan pipeline 7 bước (bám `09` mục 2)

```
[1] Capture (DXGI Desktop Duplication)          BE-071, PERF-020/021
       │ Texture2D toàn màn hình (sở hữu bởi OS/DWM, không phải Vision)
       ▼
[2] Crop GPU-side (CopySubresourceRegion         IMG-012
    thẳng vào staging texture CPU-accessible)     → gộp "crop" + điểm bắt đầu readback vào 1 buffer,
       │                                            xem ADR-41
       ▼
[3] Readback + [3a] Hash-gate + Resize/Normalize  IMG-010, PERF-032
       │ Map() staging texture → byte[] BGRA8
       │ [3a] MỚI v0.3.0: pHash 64-bit từ byte[] BGRA8 (mục 3.8, PERF-011/
       │      IMG-011) → so với hash frame trước CÙNG cửa sổ → nếu dưới
       │      ngưỡng khác biệt, SKIP thẳng xuống bước [6], tái dùng risk
       │      score đã cache (không resize/inference) → tiết kiệm đúng phần
       │      tốn CPU nhất. Nếu vượt ngưỡng: tiếp tục resize + normalize
       │      trực tiếp vào DenseTensor<float> [1,224,224,3] (hoặc NCHW —
       │      đọc từ session.InputMetadata, ADR-48)
       ▼
[4] Inference (ONNX Runtime, session tái dùng)   IMG-014, PERF-030/031/032
       │ risk score thô = 5 xác suất lớp (drawing/hentai/neutral/porn/sexy)
       ▼
[5] Tổng hợp risk score (aggregation)            IMG-013, ADR-47
       │ risk_score = P(hentai) + P(porn) + P(sexy), clamp [0,1]
       ▼
[6] Zero-out toàn bộ buffer đã dùng (bước 1-5)   IMG-003 — chi tiết bảng mục 6
       ▼
[7] Gửi VisionInferenceResult qua IPC             BE-021, 03-ipc-communication.md mục 3.3
       │ risk_score + bbox (rect cửa sổ, KHÔNG phải ảnh) + metadata
       ▼
   Service (so ngưỡng, quyết định overlay — KHÔNG do Vision quyết định)
```

Khác biệt nhỏ so với đánh số ở `09` mục 2 (7 bước: Capture/Crop/Resize/Normalize/Inference/Zero-out/Quyết định): file này **gộp Resize+Normalize thành 1 bước kỹ thuật** (ghi thẳng vào tensor, không qua object ảnh trung gian — ADR-51) và **gộp "quyết định theo ngưỡng" ở `09` thành 2 việc tách biệt**: Vision chỉ tổng hợp + gửi số (không tự quyết định ngưỡng — Service mới là nơi so `risk_threshold`, đúng ADR-12 ở `02-process-architecture.md`). Không đổi ý nghĩa 7 bước gốc, chỉ chi tiết hoá HOW.

**Bổ sung v0.3.0 (Đợt 7)**: bước `[3a]` (hash-gate, `IMG-011`/`PERF-011`) là 1 **điểm rẽ nhánh có điều kiện bên trong** bước Resize/Normalize hiện có, **không phải 1 bước thứ 8 mới** trong 7 bước gốc của `09` — đúng tinh thần "`IMG-011` là tối ưu hiệu năng cục bộ trong Vision, không phải nghiệp vụ mới" đã lường trước ở `02-process-architecture.md` mục 5 điểm 2. Chi tiết đầy đủ thuật toán ở mục 3.7/3.8.

## 3. Threading model

### 3.1 2 luồng độc lập, không luồng nào chặn luồng kia

| Luồng | Cơ chế | Việc gì | Không được làm |
|---|---|---|---|
| **Thread IPC** (đã có từ Đợt 0, `ParentalGuard.Ipc.Client.IpcChildClient`) | `async`/`Task` trên ThreadPool | Đọc/ghi Named Pipe: `Hello`/`HelloAck`, `HeartbeatPing`→`HeartbeatAck`, nhận `ControlVisionCommand`, gửi `VisionInferenceResult` | Không được gọi bất kỳ API DXGI/ONNX Runtime nào — mọi lệnh native block phải nằm ở Thread Capture-Inference |
| **Thread Capture-Inference** (mới, Đợt 1) | 1 `System.Threading.Thread` riêng, `IsBackground = true`, **không phải** `Task.Run`/ThreadPool | Toàn bộ pipeline mục 2 (bước 1-6) | Không tự đọc/ghi Named Pipe trực tiếp — chỉ đẩy kết quả vào 1 `Channel<IpcPayload>` dùng chung |

**Lý do bắt buộc dùng `Thread` riêng, không dùng `Task.Run`/ThreadPool (ADR-38)**: (1) `AcquireNextFrame`/`session.Run` là lệnh **block đồng bộ** (native, có thể mất hàng chục-hàng trăm ms) — nếu chạy trên ThreadPool sẽ chiếm giữ 1 worker thread trong lúc chờ, rủi ro làm cạn ThreadPool nếu bị lặp lại liên tục (đặc biệt nguy hiểm nếu Thread IPC vô tình dùng chung pool); (2) `ID3D11DeviceContext` (immediate context) **không thread-safe** khi gọi đồng thời từ nhiều thread — dùng đúng 1 thread cố định cho toàn bộ chuỗi lệnh D3D11 loại bỏ hoàn toàn nhu cầu đồng bộ hoá (lock) quanh device context, đơn giản và an toàn hơn dùng `ID3D11Multithread`. Đây chính là cách heartbeat **không bao giờ** bị chặn bởi 1 lần inference nặng — 2 luồng vật lý tách biệt hoàn toàn, không có `await`/blocking call nào của luồng này nằm trên đường thực thi của luồng kia.

### 3.2 Mở rộng bắt buộc cho `IpcChildClient` (Đợt 0 → Đợt 1)

`IpcChildClient.MessageLoopAsync` hiện tại (Đợt 0) ghi `HeartbeatAck` **trực tiếp inline** trong vòng lặp đọc (đọc → xử lý → ghi → đọc tiếp). Đợt 1 cần thêm 1 nguồn ghi thứ 2 (`VisionInferenceResult` từ Thread Capture-Inference) — ghi đồng thời từ 2 nơi lên cùng 1 `NamedPipeClientStream` mà không đồng bộ hoá sẽ làm interleave byte giữa 2 message, vỡ framing (`03-ipc-communication.md` mục 2.3).

**Quyết định (ADR-39)**: tách `IpcChildClient` thành 2 vòng lặp `async` chạy song song trong cùng 1 kết nối (`Task.WhenAll`), cả 2 đều nhẹ (I/O-bound, không có lệnh native block nào — không vi phạm ADR-38):

- **Reader loop**: y hệt logic đọc hiện có, nhưng khi gặp `HeartbeatPing` thì **không ghi trực tiếp** — thay vào đó enqueue 1 `HeartbeatAck` vào `Channel<IpcPayload>` outbound dùng chung.
- **Writer loop**: duy nhất 1 nơi được gọi `IpcFrameTransport.WriteFrameAsync` cho kết nối đó — `await foreach` đọc từ `Channel<IpcPayload>` outbound, ghi tuần tự (đảm bảo không interleave). `Vision` gọi `IpcChildClient.EnqueueOutbound(VisionInferenceResult)` (API mới, thread-safe, gọi được từ Thread Capture-Inference) để đẩy kết quả vào đúng channel này.
- Channel outbound: **unbounded** (khác channel mục 3.3) vì đây là dữ liệu *phải* gửi đủ (heartbeat ack không được rớt, nếu không Service coi là treo) — không áp dụng backpressure/drop ở tầng này.

Đây là thay đổi nội bộ của thư viện `ParentalGuard.Ipc` (Đợt 0), không đổi bất kỳ message/field nào đã chốt ở `03-ipc-communication.md` — thuần kỹ thuật, không cần quay lại sửa file đó.

### 3.3 Vòng lặp Capture-Inference (`CaptureLoopWorker`)

```
loop (chạy trên Thread Capture-Inference, tới khi cancellationToken huỷ):
    config = VisionRuntimeConfig.Current            // snapshot mới nhất, cập nhật bởi Reader loop
    if !config.MonitoringEnabled:
        wakeEvent.Wait()                            // park, ~0% CPU, KHÔNG busy-loop
        continue                                     // đánh thức ngay khi có ControlVisionCommand mới
    hwnd = GetForegroundWindow()
    processName = ResolveProcessName(hwnd)
    if processName in config.ExcludeProcessNames ∪ config.UserWhitelistedProcessNames: // BE-073/073a,
        wakeEvent.Wait()                              // MISC-030, PERF-020. Đợt 7 (v0.3.0): đổi từ
        continue                                       // Wait(interval) sang Wait() VÔ HẠN — đúng nghĩa đen
                                                        // PERF-010 "chỉ theo dõi sự kiện đổi cửa sổ qua
                                                        // WinEventHook, không tốn CPU capture". Đánh thức bởi
                                                        // (a) ControlVisionCommand mới (như trước), HOẶC
                                                        // (b) EVENT_SYSTEM_FOREGROUND từ Window Message Pump
                                                        // (mục 3.6, mới) — không còn polling theo interval khi
                                                        // đang ở app bị loại trừ. Sửa lỗi tham chiếu "xem mục
                                                        // 3.4" của v0.1.0 (trỏ nhầm sang ADR-40/Pause) — nay
                                                        // trỏ đúng mục 3.6.
    candidates = ResolveCandidateWindows(hwnd)          // [hwnd] nếu 1 màn hình, mục 3.5 nếu multi-monitor
    foreach w in candidates:                            // BE-086: tuần tự, không đổi
        result = FrameClassificationPipeline.Process(w, ResolveMonitorId(w))  // mục 2+3.7/3.8, có thể null
        if result is not null:                                                 // nếu AcquireNextFrame timeout
            ipcClient.EnqueueOutbound(result)
    wakeEvent.Wait(config.CaptureIntervalMs)
```

- `wakeEvent` (`ManualResetEventSlim` hoặc `AutoResetEvent`) được `Set()` bởi Reader loop mỗi khi nhận `ControlVisionCommand` mới — đảm bảo thay đổi `capture_interval_ms`/`monitoring_enabled` có hiệu lực **ngay lập tức**, không phải chờ hết interval cũ mới áp dụng. Đây chính là điểm `Service` cắm Adaptive Frame Rate (`PERF-010`, mục 3.7) vào: `Service` chỉ cần gửi `ControlVisionCommand` mới thường xuyên hơn với `capture_interval_ms` khác nhau tuỳ ngữ cảnh — `CaptureLoopWorker` không cần sửa 1 dòng nào cho phần **nhận** interval mới (đúng thiết kế đã lường trước ở v0.1.0); phần **tạo ra** tín hiệu ngữ cảnh để `Service` quyết định interval nào là nội dung mới ở mục 3.7/3.8.
- **Đợt 6 (`PERF-050b`, v0.2.2)**: cùng đúng cơ chế trên là điểm `Service` cắm "Chế độ hiệu năng" (`S4`, `10-ui-architecture.md` mục 6.4) vào — khi phụ huynh đổi `performance_mode`, `Service` chỉ cần gửi 1 `ControlVisionCommand` mới với `capture_interval_ms` tương ứng. **Cập nhật v0.3.0**: từ Đợt 7, `performance_mode` không còn là 1 con số cố định duy nhất nữa (mapping `04` ADR-127 `"balanced"`=2000ms/`"maximum_protection"`=1000ms nay chỉ còn là giá trị SÀN/mặc định trước khi có tín hiệu adaptive đầu tiên) — quan hệ đầy đủ giữa `performance_mode` (Đợt 6, tĩnh theo lựa chọn phụ huynh) và Adaptive Frame Rate (Đợt 7, động theo nội dung) được làm rõ ở mục 3.7.4. Không cần sửa `CaptureLoopWorker`/`VisionRuntimeConfig` cho cả 2 cơ chế — đúng như thiết kế đã lường trước.
- **`VisionRuntimeConfig`**: 1 class immutable nhỏ (`MonitoringEnabled`, `CaptureIntervalMs`, `RiskThreshold`, `ExcludeProcessNames`), cập nhật bằng cách swap nguyên 1 tham chiếu mới (`Interlocked.Exchange` hoặc field `volatile`) mỗi khi Reader loop nhận `ControlVisionCommand` — Capture-Inference loop chỉ đọc snapshot hiện tại ở đầu mỗi vòng lặp, không cần lock (lock-free, tránh Thread Capture-Inference phải chờ Thread IPC).
- **`RiskThreshold` nhận nhưng chưa dùng chủ động ở Đợt 1**: `ControlVisionCommand.RiskThreshold` (`BE-090`) được lưu vào snapshot nhưng pipeline hiện tại không rẽ nhánh theo nó (Vision luôn tính đủ risk score + gửi đủ bbox mỗi frame, xem mục 4.4) — trường này giữ chỗ cho tối ưu cục bộ tương lai (ví dụ bỏ qua 1 số bước phụ khi score chắc chắn thấp), quyết định overlay thật sự **luôn** do `Service` làm lại độc lập khi nhận `VisionInferenceResult`, không có 2 nguồn sự thật (nhất quán ADR-12 ở `02-process-architecture.md`).

### 3.4 Trả lời câu hỏi mở: cơ chế Pause/suspend `Vision` (`02-process-architecture.md` mục 8, `PAUSE-031`)

**Quyết định (ADR-40)**: **không** dùng `SuspendThread` (nguy hiểm — có thể đình chỉ thread ngay giữa lúc đang giữ 1 lock nội bộ của CLR/driver GPU, rủi ro deadlock, Microsoft khuyến cáo không dùng ngoài mục đích debug), **không** dùng Job Object riêng, **không** thêm message IPC mới. Tái sử dụng đúng field **`ControlVisionCommand.MonitoringEnabled`** đã có sẵn trong schema (`03-ipc-communication.md` mục 3.2): khi `Service` chuyển sang `Running·Paused`, nó chỉ cần gửi `ControlVisionCommand { MonitoringEnabled = false, ... }` xuống `Vision` — `CaptureLoopWorker` (mục 3.3) đã tự park ở `wakeEvent.Wait()` khi gặp cờ này, không tốn CPU, không giữ tài nguyên GPU trong lúc pause. Khi `Service` resume, gửi lại `ControlVisionCommand { MonitoringEnabled = true, ... }`, `wakeEvent.Set()` đánh thức vòng lặp ngay.

`Vision` **không biết** lý do `MonitoringEnabled = false` là do Pause hay do 1 lý do khác trong tương lai — đúng nguyên tắc "Vision là hàm phân loại thuần, không giữ state nghiệp vụ" đã chốt ở `02-process-architecture.md` mục 5.2. Tần suất heartbeat giảm khi Pause (`PAUSE-031`, `02` mục 4) là quyết định hoàn toàn nằm ở Thread IPC (Service đổi chu kỳ gửi `HeartbeatPing`) — độc lập với Thread Capture-Inference, không cần phối hợp gì thêm.

### 3.5 Đợt 2 — Multi-monitor / đa cửa sổ (`BE-080`–`082`, `IMG-020`)

Phạm vi mở rộng đã đánh dấu tường minh ở mục 1 — v0.1.0 chỉ xử lý `hwnd = GetForegroundWindow()` (mục 3.3). Phần này chi tiết hoá HOW cho `ROADMAP.md` Đợt 2 ("Multi-monitor `BE-080`–`083`"), không đổi bước 1-7 ở mục 2, chỉ mở rộng **đầu vào** của `CaptureLoopWorker` từ 1 `hwnd` sang 1 danh sách `hwnd` có thứ tự ưu tiên, xử lý tuần tự đúng `BE-086`.

**Enumerate output (`BE-081`)**: `Vision` enumerate toàn bộ `IDXGIOutput` qua `IDXGIFactory1.EnumAdapters1()` → `IDXGIAdapter1.EnumOutputs()`, gán `monitor_id` = **chỉ số thứ tự trong danh sách enumerate được** (0, 1, 2...) — chỉ ổn định trong 1 phiên chạy của `Vision` (đúng bản chất "chỉ dùng để nhóm", không phải định danh vật lý bền vững, đã ghi rõ ở `03-ipc-communication.md` mục 3.3). Re-enumerate toàn bộ (gán lại từ đầu, chấp nhận `monitor_id` cũ có thể đổi ý nghĩa) khi nhận `WM_DISPLAYCHANGE` (`Vision` cần 1 message-only window hoặc tái dùng handle sẵn có để nhận message này — chi tiết implement, không phải quyết định kiến trúc) — hệ quả tạm thời (các overlay đang hiển thị theo `monitor_id` cũ có thể nhóm sai 1 chu kỳ ngắn ở chế độ gộp `BE-088`, tự sửa đúng ở chu kỳ capture kế tiếp, chấp nhận được vì đổi cấu hình màn hình vật lý vốn đã là sự kiện hiếm và gây gián đoạn ngắn tự nhiên).

**`ResolveMonitorId(hwnd)`** (cầu nối GDI ↔ DXGI, ADR-63): `monitor_id` của 1 cửa sổ = chỉ số trong danh sách enumerate ở trên mà `IDXGIOutput.GetDesc().Monitor` (chính là 1 `HMONITOR`) trùng với `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)`. Đây là kỹ thuật chuẩn để đối chiếu output DXGI với màn hình vật lý mà không cần tự parse toạ độ.

**Capture theo output có nhu cầu (`BE-082`, lazy)**: `Dictionary<int outputIndex, OutputCaptureContext>` (mỗi `OutputCaptureContext` sở hữu 1 `ID3D11Device`/`IDXGIOutputDuplication` riêng cho đúng output đó, tái dùng nguyên bước 1-2 ở mục 2/4.1). Tạo `OutputCaptureContext` mới (lazy) ngay khi 1 cửa sổ trong danh sách candidate (dưới đây) nằm trên output đó lần đầu; **dispose** context của 1 output nếu không có cửa sổ candidate nào trên đó trong ≥ 5 chu kỳ capture liên tiếp (tránh dispose/recreate liên tục khi cửa sổ dao động qua lại biên 2 màn hình) — giữ đúng tinh thần "chỉ capture màn hình có cửa sổ cần giám sát" thay vì capture toàn bộ N output liên tục.

**Chọn danh sách cửa sổ cần xử lý mỗi chu kỳ (`IMG-020`, thay dòng `hwnd = GetForegroundWindow()` ở mục 3.3, ADR-64)**:

```
fgHwnd = GetForegroundWindow()                        // luôn xử lý trước tiên — IMG-020: "cửa sổ đang có
candidates = [fgHwnd]                                  // focus thực sự" có ưu tiên cao nhất
coveredMonitors = { ResolveMonitorId(fgHwnd) }

if outputCount > 1:                                     // BE-080: chỉ chạy nhánh multi-monitor khi thật sự có > 1 màn hình
    foreach w in EnumerateTopLevelWindowsInZOrder():     // EnumWindows — thứ tự trả về đã là top-to-bottom Z-order
        if coveredMonitors.Count == outputCount: break   // đã có candidate cho mọi màn hình, dừng sớm
        m = ResolveMonitorId(w)
        if m in coveredMonitors: continue
        if !IsCandidateWindow(w): continue                // visible, không minimized, không thuộc ExcludeProcessNames (BE-073/073a)
        candidates.Add(w)
        coveredMonitors.Add(m)

foreach hwnd in candidates:                              // BE-086: TUẦN TỰ, không song song
    result = FrameClassificationPipeline.Process(hwnd, ResolveMonitorId(hwnd))
    if result is not null:
        ipcClient.EnqueueOutbound(result)                 // gửi NGAY từng kết quả, không gom đợi hết candidates (BE-086)
```

Mỗi cửa sổ trong `candidates` chạy đúng nguyên vẹn pipeline 7 bước ở mục 2 (dùng `OutputCaptureContext` của đúng output chứa nó) — không có bước xử lý "gộp nhiều màn hình thành 1 frame lớn" nào, mỗi cửa sổ vẫn là 1 lần chạy pipeline độc lập, đúng model đã có ở Đợt 1. Với đúng 1 màn hình (`outputCount == 1`, môi trường phổ biến nhất), `candidates` luôn chỉ có `[fgHwnd]` — hành vi giống hệt Đợt 1, không có chi phí phát sinh nào (nhánh multi-monitor bị bỏ qua hoàn toàn bởi điều kiện `outputCount > 1`).

`IsCandidateWindow(w)`: `IsWindowVisible(w) && !IsIconic(w) && GetWindowTextLength(w) > 0` (loại cửa sổ ẩn/minimized/không có tiêu đề — heuristic loại các message-only/helper window không phải cửa sổ ứng dụng thật) `&& ResolveProcessName(w) not in (config.ExcludeProcessNames ∪ config.UserWhitelistedProcessNames)` (tái dùng đúng danh sách `BE-073a` đã áp dụng cho `fgHwnd` ở Đợt 1; **sửa nhỏ v0.3.0** — bổ sung `UserWhitelistedProcessNames` vào phép hợp, đúng nguyên tắc "union" đã chốt ở `04-data-architecture.md` mục 3.3 khi `MISC-030` được thêm ở Đợt 6 nhưng chưa kịp cập nhật ở đây, thuần đồng bộ hoá, không đổi hành vi nào khác).

Phần Overlay/Service tiêu thụ `monitor_id` này (nhóm theo màn hình cho chế độ gộp `BE-088`/`BE-089`, icon trạng thái mỗi màn hình `BE-083`) — thiết kế đầy đủ ở `07-overlay-architecture.md` mục 3/4, không lặp lại ở đây.

### 3.6 Thread thứ 3 — Window Message Pump (mới, Đợt 7)

Đóng câu hỏi mở cũ ở mục 11 ("cơ chế cụ thể để `Vision` nhận `WM_DISPLAYCHANGE`") và sửa tham chiếu sai ở mục 3.3 v0.1.0 ("chưa dùng WinEventHook để tắt hẳn — xem mục 3.4" trỏ nhầm sang ADR-40/Pause). Cả 2 nhu cầu (nhận `WM_DISPLAYCHANGE` để re-enumerate output, và phản ứng tức thời khi đổi cửa sổ foreground cho `PERF-020`) đều cần 1 **message-only window** + vòng lặp `GetMessage`/`TranslateMessage`/`DispatchMessage` — Win32 không cho phép nhận các sự kiện này nếu không có thread nào bơm message.

**Quyết định (ADR-133)**: thêm **1 `Thread` thứ 3** cho `Vision`, gọi là **Window Message Pump**, tách biệt khỏi cả Thread IPC (mục 3.1, async/`Task`) lẫn Thread Capture-Inference (mục 3.1, `Thread` chuyên dụng chạy DXGI/ONNX):

| Việc gì | Không được làm |
|---|---|
| Tạo 1 message-only window (`CreateWindowEx(0, className, "", 0, 0,0,0,0, HWND_MESSAGE, ...)`), chạy `GetMessage` loop tới khi `Vision` thoát | Không gọi bất kỳ API DXGI/ONNX Runtime nào (vi phạm ADR-38 nếu block) |
| Đăng ký `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT)` — callback chạy ngay trên thread này (đúng ngữ nghĩa `WINEVENT_OUTOFCONTEXT` + có message loop) | Không giữ lock/chờ đồng bộ với Thread Capture-Inference — chỉ `Set()` 1 `ManualResetEventSlim`/ghi 1 `volatile bool`, tựa hệt cách Reader loop `Set()` `wakeEvent` (mục 3.3) |
| Xử lý `WM_DISPLAYCHANGE` trong window procedure của chính message-only window đó | — |

- **Khi nhận `EVENT_SYSTEM_FOREGROUND`** (đổi cửa sổ foreground, ở bất kỳ đâu trên hệ thống — hook này không lọc theo hwnd vì bản thân mục đích là biết "vừa đổi cửa sổ", không cần biết đổi thành cửa sổ nào tại đây): gọi `wakeEvent.Set()` — **cùng 1 `wakeEvent`** mà Reader loop (mục 3.3) đã dùng để đánh thức Thread Capture-Inference khi có `ControlVisionCommand` mới. Thread Capture-Inference thức dậy, tự đọc lại `GetForegroundWindow()`/`ResolveProcessName` như bình thường (không cần Window Message Pump truyền hwnd qua) — đây chính là cơ chế khiến `wakeEvent.Wait()` **vô hạn** ở nhánh exclude-list (mục 3.3) vẫn phản ứng tức thời khi user chuyển sang app khác, đúng nghĩa đen `PERF-020`.
- **Khi nhận `WM_DISPLAYCHANGE`**: đặt `DisplayChangedFlag = true` (`volatile bool` hoặc `Interlocked`) — Thread Capture-Inference kiểm tra cờ này ở đầu mỗi vòng lặp (mục 3.3), nếu `true` thì re-enumerate `IDXGIOutput` (mục 3.5) trước khi tiếp tục, rồi tự đặt lại `false`. Không cần `wakeEvent.Set()` riêng cho trường hợp này (không cấp thiết bằng đổi foreground — đợi tới vòng lặp kế tiếp là đủ, tránh phức tạp hoá).
- **Không cần Dispose/Unhook thủ công lúc `Vision` thoát bất thường** — `SetWinEventHook`/message-only window tự giải phóng khi process kết thúc (đúng hành vi OS chuẩn cho mọi handle process-scoped), nhất quán với cách `Vision` không có logic dọn dẹp đặc biệt nào khác lúc `Environment.Exit` (mục 7/8).

### 3.7 Adaptive Frame Rate (`PERF-010`) — state machine 2 tầng phối hợp với `performance_mode`

Trả lời đầy đủ `PERF-010` (bảng 4 dòng ở `Specification/08-performance-cpu-spec.md` mục 2). Nguyên tắc bao trùm giữ nguyên `ROADMAP.md` mục 2: **`Service` là bên duy nhất quyết định đổi `capture_interval_ms`** (qua `ControlVisionCommand`, cơ chế đã có sẵn từ v0.1.0, mục 3.3) — `Vision` chỉ **sinh tín hiệu** (`content_changed`, mục 3.8) để `Service` quyết định, không tự đổi tần suất capture của chính nó. Đây là lý do bắt buộc phải có 1 field mới trong `VisionInferenceResult` (xem mục 4.4, amendment `03-ipc-communication.md` cùng lượt) — không có cách nào khác để tín hiệu "nội dung có đổi hay không" (chỉ `Vision` nhìn thấy frame) đi tới nơi ra quyết định (`Service`) mà không qua IPC.

#### 3.7.1 4 dòng bảng `PERF-010` ánh xạ vào đâu

| Dòng `PERF-010` | Nơi xử lý | Cơ chế |
|---|---|---|
| Foreground app trong exclude-list → tạm dừng capture hoàn toàn | `Vision`, cục bộ, không round-trip `Service` | `wakeEvent.Wait()` vô hạn + `EVENT_SYSTEM_FOREGROUND` (mục 3.3/3.6) — không sinh `VisionInferenceResult` nào trong lúc này |
| Nội dung tĩnh → 1 frame/5 giây | `Service`, dựa trên chuỗi `content_changed=false` liên tiếp từ `Vision` | Mục 3.7.2 (bucket `Relaxed`) |
| Nội dung thay đổi liên tục → 1 frame/giây | `Service`, dựa trên `content_changed=true` | Mục 3.7.2 (bucket `Vigilant`) |
| Risk score cận ngưỡng → tăng tần suất tạm thời | `Service`, so trực tiếp `risk_score` (đã có sẵn trong `VisionInferenceResult` từ Đợt 1) với `risk_threshold` (đã có sẵn trong config `Service`) | Mục 3.7.3 (cờ `Boost`) — **không cần tín hiệu mới từ `Vision`**, `Service` tự tính được từ dữ liệu đã có |

Dòng 1 và dòng 4 **không cần thiết kế thêm** — dòng 1 đã có từ v0.1.0 (chỉ sửa polling→event ở mục 3.6), dòng 4 tự suy ra được từ dữ liệu `Service` đã sẵn có. Phần thiết kế mới thực sự nằm ở dòng 2/3 (phân biệt tĩnh/thay đổi) — cần đúng 1 tín hiệu mới (`content_changed`).

#### 3.7.2 State machine phía `Service`: 2 bucket + hysteresis bất đối xứng (ADR-130)

`Service` giữ 1 domain-state RAM-only mới, **theo dõi riêng từng cửa sổ đang là candidate** (khoá theo `window_handle`, cùng khoá đã dùng cho `VisionInferenceResult` — nhất quán với cách Overlay/Service đã theo dõi đa cửa sổ từ Đợt 2):

```
class WindowFrameRateState {
    CaptureVigilance Vigilance;     // Vigilant (mặc định) | Relaxed
    int              UnchangedStreak;
    long             LastSeenAtTick;   // để evict — mục 3.7.5
}
enum CaptureVigilance { Vigilant, Relaxed }
```

Cập nhật mỗi khi nhận 1 `VisionInferenceResult` mới cho `window_handle` đó:

```
state = states.GetOrAdd(r.window_handle, () => new WindowFrameRateState { Vigilance = Vigilant })
if r.content_changed:
    state.UnchangedStreak = 0
    state.Vigilance = Vigilant                      // NGAY LẬP TỨC — không cần streak (xem lý do dưới)
else:
    state.UnchangedStreak++
    if state.Vigilance == Vigilant
       and state.UnchangedStreak >= N_RELAX          // PROPOSED, chờ benchmark — mục 3.7.6
       and performance_mode == "balanced":           // mục 3.7.4 — KHÔNG relax nếu maximum_protection
        state.Vigilance = Relaxed
state.LastSeenAtTick = now
```

**Quyết định hysteresis bất đối xứng (ADR-130)**: chuyển **lên** `Vigilant` (nội dung vừa đổi, hoặc cửa sổ mới xuất hiện lần đầu — `content_changed` mặc định `true` khi chưa có hash trước để so, mục 3.8) áp dụng **ngay từ tín hiệu đầu tiên**, không cần streak/debounce. Chuyển **xuống** `Relaxed` (nội dung tĩnh, an toàn để giảm tần suất) yêu cầu **`N_RELAX` frame liên tiếp** `content_changed=false`. Đây là lựa chọn có chủ đích theo đúng chỉ đạo "ưu tiên an toàn hơn tiết kiệm CPU khi không chắc": phản ứng tức thời khi có dấu hiệu cần cảnh giác hơn, nhưng thận trọng/chờ xác nhận nhiều lần trước khi nới lỏng — sai lệch 1 hướng (chậm nới lỏng vài giây) chỉ tốn thêm chút CPU, sai lệch hướng kia (nới lỏng nhầm khi nội dung thực ra đang đổi) trực tiếp làm chậm phát hiện.

#### 3.7.3 Cờ `Boost` (risk score cận ngưỡng, dòng 4 bảng `PERF-010`)

Tính độc lập với `Vigilance`, áp dụng **đè lên** kết quả mục 3.7.2 khi đang bật:

```
state.Boost = (r.risk_score < config.RiskThreshold) and
              (config.RiskThreshold - r.risk_score <= NEAR_THRESHOLD_MARGIN)   // PROPOSED — mục 3.7.6
```

Chỉ boost khi **dưới** ngưỡng và **gần** ngưỡng (chưa đủ để tự kích hoạt overlay, nhưng đáng nghi) — nếu `risk_score >= risk_threshold`, overlay đã kích hoạt qua đúng luồng hiện có (`ADR-12`), không cần "tăng tần suất" nữa vì đã đang chặn. `Boost` không có hysteresis (bật/tắt ngay theo từng frame) vì đây thuần là phản ứng an toàn tăng tốc, không có rủi ro "dao động" đáng lo (khác chiều nới lỏng ở mục 3.7.2).

#### 3.7.4 Quy đổi sang `capture_interval_ms` gửi xuống — quan hệ với `performance_mode` (Đợt 6)

| Trạng thái | `capture_interval_ms` |
|---|---|
| `Boost = true` (bất kỳ cửa sổ nào đang candidate) | `BOOST_INTERVAL_MS` — PROPOSED, mục 3.7.6 |
| Không `Boost`, có ≥ 1 cửa sổ `Vigilant` | `1000` ms (dòng 3 `PERF-010`, ĐÃ CHỐT `08` v0.4.0 — cố định, **không phụ thuộc `performance_mode`**) |
| Không `Boost`, **toàn bộ** cửa sổ candidate đều `Relaxed` | `StaticIntervalForMode(performance_mode)` — mục dưới |

```
StaticIntervalForMode(mode):
    "balanced"           → 5000   // dòng 2 PERF-010, ĐÃ CHỐT 08 v0.4.0
    "maximum_protection" → 1000   // = giống Vigilant — xem giải thích dưới
```

`Service` tính `desiredIntervalMs` = giá trị nhỏ nhất (nhanh nhất) áp dụng cho trạng thái tổng hợp ở trên (an toàn nhất trong số các cửa sổ đang giám sát thắng); chỉ gửi `ControlVisionCommand` mới khi `desiredIntervalMs != currentAppliedIntervalMs` (tránh spam IPC mỗi frame khi không đổi gì).

**Quan hệ `performance_mode` ↔ Adaptive Frame Rate (làm rõ theo yêu cầu, ADR-131)**: `performance_mode` không còn là "1 con số tần suất cố định duy nhất áp dụng mọi lúc" như thiết kế tạm ở Đợt 6 (`04` ADR-127) nữa — nó trở thành **giới hạn nới lỏng tối đa cho phép** (chỉ tác động tới bucket `Relaxed`):

- **`"balanced"`** (mặc định): đúng nghĩa đen `PERF-050b` "theo chiến lược adaptive ở mục 2" — cho phép nới lỏng xuống tới 5000ms khi nội dung thực sự tĩnh đủ lâu (`N_RELAX` frame). Đây chính là hành vi mô tả nguyên vẹn ở `PERF-010`.
- **`"maximum_protection"`**: **không bao giờ nới lỏng** — dù `Vigilance` có chuyển thành `Relaxed` về mặt trạng thái nội bộ (đơn giản hoá code, không cần nhánh riêng), `StaticIntervalForMode` vẫn trả về `1000`ms, **giống hệt** giá trị `Vigilant`. Kết quả thực tế: máy chạy `"maximum_protection"` **luôn** capture ở 1000ms (trừ khi `Boost` đẩy còn nhanh hơn), đúng nghĩa đen `PERF-050b` "tăng tần suất capture/inference, chấp nhận tiêu tốn CPU cao hơn" — không có "điểm nghỉ" nào cho CPU dù nội dung đứng yên bao lâu.
- Cờ `Boost` (mục 3.7.3) áp dụng **như nhau ở cả 2 mode** — tăng tốc vì lý do an toàn không bao giờ nên bị `performance_mode` chặn lại.

Nói ngắn gọn: **`performance_mode` là cái trần (ceiling) cho việc nới lỏng, Adaptive Frame Rate là thuật toán quyết định có nới lỏng tới mức trần đó hay không dựa trên nội dung thực tế — không phải 2 cơ chế độc lập ghi đè lẫn nhau, mà lồng vào nhau theo đúng 1 chiều: adaptive không bao giờ vượt quá trần mà mode cho phép, nhưng mode không tự động áp dụng trần đó nếu nội dung chưa được xác nhận là tĩnh.**

**Ghi chú cần đồng bộ ở amendment `04-data-architecture.md` kế tiếp (không tự sửa ở đây, đúng nguyên tắc mỗi lượt)**: `capture_interval_baseline_ms`/ADR-127 hiện ghi `"balanced"` → 2000ms — con số này là giá trị tạm đặt ở Đợt 6 **trước khi** Adaptive Frame Rate được thiết kế đầy đủ (file này). Theo thiết kế mục 3.7.4, giá trị áp dụng thực tế cho bucket `Relaxed` dưới `"balanced"` phải là **5000ms** (đúng `PERF-010` v0.4.0 ĐÃ CHỐT, không phải 2000ms) — `"maximum_protection"` giữ nguyên 1000ms (khớp sẵn). `feature-dev` implement theo con số **5000ms** ở file này (nguồn chính thức, mới hơn), không theo 2000ms còn ghi ở `04` cho tới khi file đó được amend đồng bộ.

#### 3.7.5 Vòng đời state — evict cửa sổ không còn candidate

`Service` xoá `WindowFrameRateState` của 1 `window_handle` nếu không nhận `VisionInferenceResult` nào cho nó trong **5 chu kỳ liên tiếp** (tái dùng đúng ngưỡng đã chọn ở `ADR-65` cho `OutputCaptureContext`, giữ nhất quán 1 con số "thời gian chờ trước khi coi là không còn liên quan" xuyên suốt dự án thay vì phát minh thêm 1 hằng số khác cho cùng ý nghĩa). Không cần logic đặc biệt khi 1 cửa sổ đóng hẳn (đóng thì đơn giản không còn xuất hiện trong `VisionInferenceResult` nào nữa, tự rơi vào nhánh evict trên).

Khi `Service` khởi động lại (restart bình thường hoặc sau `Degraded·FailSecure`): toàn bộ `states` **reset rỗng**, cửa sổ đầu tiên gặp lại luôn bắt đầu ở `Vigilant` (mặc định an toàn, đúng constructor `GetOrAdd` ở mục 3.7.2) — không có gì để khôi phục từ `config.db` (state này hoàn toàn RAM-only, không persist, cùng tinh thần `PauseState.LastBannerShownAtUnixMs` ở `02-process-architecture.md` mục 3a.3: không phải cơ chế bảo mật, không cần sống sót qua restart, khởi động lại ở trạng thái an toàn nhất là lựa chọn đúng).

#### 3.7.6 Hằng số PROPOSED — cần benchmark (`PERF-061`)

Đúng tinh thần `PERF-061` (mọi ngưỡng hiệu năng là đề xuất ban đầu, bắt buộc benchmark lại đa cấu hình máy trước khi khoá cứng) — **không chốt số tuyệt đối ở đây**, chỉ đề xuất điểm khởi đầu hợp lý để `feature-dev` có số chạy được ngay, `test-runner`/benchmark Đợt 9 tinh chỉnh lại:

| Hằng số | Đề xuất khởi điểm | Ghi chú |
|---|---|---|
| `T_UNCHANGED` (ngưỡng Hamming distance dHash coi là "không đổi", mục 3.8.1) | 3 (trên tổng 64 bit, ~4.7%) | Đủ dung sai cho nhiễu nén màn hình/anti-aliasing nhỏ giữa 2 lần chụp liên tiếp, nhưng vẫn nhạy với thay đổi nội dung thật |
| `N_RELAX` (số frame `content_changed=false` liên tiếp trước khi nới lỏng) | 3 | Đủ để loại nhiễu 1 frame tình cờ giống nhau, không quá lâu làm chậm tiết kiệm CPU thực sự |
| `BOOST_INTERVAL_MS` | 500 | Nhanh hơn `Vigilant` (1000ms) 2 lần — đủ ý nghĩa "tăng tốc xác nhận" mà không đối xứng quá gần `Vigilant` |
| `NEAR_THRESHOLD_MARGIN` | 0.15 (đơn vị `risk_score`, cùng thang `[0,1]`) | Cùng phạm vi câu hỏi mở benchmark chọn `risk_threshold` mặc định đã có ở `09-image-processing-spec.md` mục 8 (`BE-090`) — 2 con số nên benchmark cùng lúc vì phụ thuộc lẫn nhau |

### 3.8 Perceptual Hashing (`PERF-011`/`IMG-011`) — tín hiệu dùng chung cho cả PERF-010 và PERF-011 (ADR-129)

**Quyết định trung tâm (ADR-129)**: `PERF-010` (đổi tần suất — mục 3.7) và `PERF-011` (bỏ qua inference khi nội dung không đổi) dùng **CHUNG đúng 1 phép so sánh pHash mỗi chu kỳ** — không tính 2 lần cho 2 mục đích khác nhau. Giá trị boolean `content_changed` tính ra ở đây vừa (a) được gửi lên `Service` để quyết định bucket (mục 3.7), vừa (b) dùng cục bộ ngay trong `Vision` để quyết định có chạy bước Inference (bước 4/5, mục 2) hay tái dùng kết quả cũ.

#### 3.8.1 Thuật toán: difference hash (dHash) 64-bit viết tay (ADR-128)

**Không dùng thư viện pHash ngoài** (ví dụ các gói NuGet pHash/`CoenM.ImageHash`) — viết tay 1 hàm dHash đơn giản, cùng tinh thần thận trọng dependency đã áp dụng cho `ADR-51` (bilinear resize viết tay, không `SixLabors.ImageSharp`) và `ADR-50` (chỉ chấp nhận thư viện tối thiểu cần thiết cho binding DXGI). Lý do: (1) full pHash (DCT-based) giải quyết bài toán "ảnh giống nhau dù bị xoay/nén/đổi gamma" — không cần thiết ở đây vì 2 frame so sánh luôn **cùng 1 cửa sổ, cùng độ phân giải, chụp cách nhau đúng 1 `capture_interval_ms`**, không có biến dạng hình học; (2) dHash rẻ hơn nhiều (so sánh độ sáng tương đối giữa các điểm liền kề, không cần DCT) — quan trọng vì bản thân phép đo tiết kiệm CPU **phải rẻ hơn hẳn** thứ nó đang cố tránh (Inference), nếu không sẽ phản tác dụng.

```
ComputeDHash64(byte[] bgra8, int width, int height):
    // Lấy mẫu 9×8 = 72 điểm, toạ độ dàn đều (KHÔNG quét toàn bộ buffer — O(72) bất kể kích thước cửa sổ)
    luma[9,8] = for each (col in 0..8, row in 0..7):
        (x, y) = (col * (width-1) / 8, row * (height-1) / 7)   // strided point-sample, nearest-neighbor
        pixel = bgra8[offset(x,y)]
        luma[col,row] = (pixel.B + pixel.G + pixel.R) / 3       // xấp xỉ thô, đủ dùng cho so sánh tương đối

    hash = 0UL
    for row in 0..7:
        for col in 0..7:
            bit = luma[col,row] < luma[col+1,row] ? 1 : 0        // so 2 cột liền kề cùng hàng
            hash = (hash << 1) | bit
    return hash   // 64 bit
```

- **Vị trí chèn (mục 2, bước `[3a]`)**: ngay sau `Map()` staging texture ra `byte[]` BGRA8 (readback, đầu bước `[3]` hiện có), **trước** bilinear resize + normalize — để có thể skip cả resize lẫn inference nếu nội dung không đổi (resize tuy rẻ hơn inference nhưng vẫn đáng tiết kiệm nếu miễn phí được).
- **So sánh**: `HammingDistance(newHash, previousHash) = BitOperations.PopCount(newHash ^ previousHash)` (0-64) — nếu `<= T_UNCHANGED` (PROPOSED, mục 3.7.6 gộp chung bảng) → `content_changed = false`; ngược lại `true`. Cửa sổ **lần đầu gặp** (chưa có `previousHash` lưu) → luôn `content_changed = true` (không có gì để so, mặc định an toàn — chạy inference đầy đủ).
- **Luôn cập nhật `previousHash = newHash`** sau mỗi chu kỳ, **bất kể** kết quả so sánh — so với frame *ngay trước đó*, không phải so với 1 frame mốc cố định (tránh "trôi" — nội dung đổi từ từ qua nhiều chu kỳ nhỏ dưới ngưỡng mỗi lần nhưng cộng dồn thành khác biệt lớn sẽ không bao giờ bị phát hiện nếu luôn so với frame gốc ban đầu).

#### 3.8.2 State lưu ở đâu — xác nhận đúng ranh giới "state kỹ thuật" (không phải "state nghiệp vụ")

`Dictionary<IntPtr windowHandle, WindowHashEntry>` sống **trong `Vision`** (không phải `Service`), RAM-only:

```
class WindowHashEntry {
    ulong  PreviousHash;
    float  CachedRiskScore;    // tái dùng khi content_changed = false (PERF-011)
    long   LastSeenAtTick;     // evict — cùng ngưỡng 5 chu kỳ, ADR-65
}
```

Đây **chính xác là** trường hợp đã được lường trước ở `02-process-architecture.md` mục 5 điểm 2: *"Vision là hàm phân loại thuần theo từng frame, không giữ state nghiệp vụ giữa các lần gọi (**ngoại trừ** state kỹ thuật thuần tuý như perceptual-hash frame trước để so sánh, `IMG-011` — đây là tối ưu hiệu năng, không phải nghiệp vụ)"*. Ranh giới cụ thể (làm rõ theo yêu cầu): "state nghiệp vụ" (bị cấm ở `Vision`) là bất kỳ dữ liệu nào ảnh hưởng **quyết định** (ngưỡng bao nhiêu, app nào bị loại trừ, đang pause hay không — mọi thứ `Service` đẩy xuống qua `ControlVisionCommand`); `WindowHashEntry` không quyết định gì cả — nó chỉ là **input** cho 1 phép tính thuần (`content_changed`) mà kết quả cuối cùng vẫn do `Service` (bucket switching, mục 3.7) hoặc chính `Vision` (skip inference, thuần kỹ thuật không đổi ý nghĩa risk score) sử dụng. Không có rủi ro "2 nguồn sự thật" vì `WindowHashEntry` không lưu bất kỳ điều gì `Service` cũng cần biết để hoạt động đúng — nếu `Vision` restart, hash reset về "chưa có gì để so" và tự phục hồi đúng ở chu kỳ kế tiếp (coi frame đầu tiên sau restart là `content_changed = true`, an toàn).

`CachedRiskScore` cùng `WindowHashEntry` — khi `content_changed = false`, `FrameClassificationPipeline` trả về `VisionInferenceResult` với `risk_score = CachedRiskScore` (không chạy lại inference) nhưng **vẫn tính lại `bbox`** mỗi chu kỳ (rẻ — chỉ 1 lệnh `DwmGetWindowAttribute`, mục 4.1 — không có lý do tái dùng rect cũ nếu cửa sổ đã di chuyển/đổi kích thước dù nội dung bên trong không đổi) và **vẫn cập nhật `captured_at_unix_ms`** mới (đúng nghĩa "đây là kết quả của chu kỳ vừa capture", dù risk score tái dùng).

#### 3.8.3 Zero-out buffer mới (`IMG-003`) — mở rộng bảng mục 6

Mảng `luma[9,8]` (72 byte, dữ liệu dẫn xuất từ pixel nhưng không thể tái tạo lại ảnh gốc) được coi **thận trọng** như 1 buffer dẫn xuất từ ảnh — zero ngay sau khi tính xong `hash`, cùng nguyên tắc `try/finally` cục bộ đã áp dụng cho mọi buffer khác (bảng đầy đủ ở mục 6, dòng mới thêm). **`PreviousHash` (8 byte, `ulong`) và `CachedRiskScore` (4 byte, `float`) KHÔNG thuộc phạm vi zero-out** — đây chính xác là ngoại lệ đã được `IMG-011` cho phép tường minh ("hash chỉ là 1 chuỗi số ngắn... an toàn để giữ trong RAM lâu hơn 1 chút để so sánh giữa các frame"), giá trị cũ tự nhiên bị ghi đè bởi giá trị mới mỗi chu kỳ (không tồn tại "dữ liệu ảnh cũ" theo nghĩa `IMG-003` vì bản thân hash/score không phải và không thể suy ngược ra ảnh).

#### 3.8.4 Đa cửa sổ / đa màn hình (Đợt 2, mục 3.5)

Không cần thiết kế thêm — `Dictionary<IntPtr, WindowHashEntry>` đã khoá theo `window_handle`, tự nhiên tách biệt hash của từng cửa sổ candidate độc lập, kể cả khi chúng nằm trên nhiều màn hình khác nhau (mục 3.5). Với đúng 1 màn hình (trường hợp phổ biến nhất), `candidates` luôn có đúng 1 phần tử — hành vi giống hệt như mô tả ở trên, không có chi phí/độ phức tạp phát sinh.

## 4. Class/component design chi tiết theo từng bước

Namespace đề xuất: `ParentalGuard.Vision.Capture` (bước 1-3), `ParentalGuard.Vision.Inference` (bước 4-5), `ParentalGuard.Vision.Pipeline` (điều phối + threading mục 3), `ParentalGuard.Vision.ModelIntegrity` (mục 7).

### 4.1 Bước 1 — Capture

- **`ForegroundWindowTracker`**: `GetForegroundWindow()` → HWND; `GetWindowThreadProcessId` + `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName` → tên file process, dùng cho exclude-list (`BE-073a`). Đợt 1 chỉ xử lý **đúng 1** cửa sổ foreground tại 1 thời điểm (`GetForegroundWindow` chỉ trả về 1 HWND theo đúng ngữ nghĩa OS) — kịch bản "nhiều cửa sổ cùng foreground khả dĩ" ở `IMG-020` chỉ phát sinh khi có đa màn hình (mỗi màn hình có 1 cửa sổ "active" riêng theo `BE-082`), thuộc phạm vi Đợt 2.
- **`WindowRectResolver`**: `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, ...)` → rect màn hình thật của cửa sổ (không dùng `GetWindowRect` thô, đúng `IMG-012`).
- **`MonitorSelector`**: `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)` → HMONITOR, ánh xạ sang chỉ số output DXGI (duyệt `IDXGIAdapter.EnumOutputs` tìm `DXGI_OUTPUT_DESC.Monitor` khớp HMONITOR) — chọn đúng 1 màn hình chứa cửa sổ (`PERF-021`). Đợt 1 giả định cửa sổ nằm gọn trong 1 màn hình (đúng scope "1 cửa sổ, 1 màn hình" của `ROADMAP.md`); trường hợp cửa sổ vắt qua nhiều output (đã ghi nhận là "để System Design quyết định" ở `IMG-012`) — quyết định Đợt 1: **clip crop rect về đúng bounds của màn hình đã chọn**, phần cửa sổ tràn sang màn hình khác bị cắt bỏ khỏi khung phân tích (chấp nhận được cho happy-path 1 màn hình); việc ghép nhiều output texture cho đúng 1 cửa sổ liên màn hình để ở Đợt 2 (`BE-080`–`083`).
- **`DesktopDuplicationCapture`**: sở hữu 1 `ID3D11Device` (tạo 1 lần lúc Vision khởi động, `D3D_DRIVER_TYPE_HARDWARE`, `BindFlags` hỗ trợ `D3D11_CREATE_DEVICE_BGRA_SUPPORT` cho tương thích DXGI) + 1 `IDXGIOutputDuplication` theo output hiện tại (tạo lại khi đổi output hoặc khi `AcquireNextFrame` trả `DXGI_ERROR_ACCESS_LOST` — tình huống bình thường khi đổi độ phân giải/khoá màn hình/secure desktop, **không** phải lỗi quyền, xử lý bằng cách Dispose + recreate duplication object, không phải fallback IL ở mục 8). Trả về `ID3D11Texture2D` (sở hữu bởi OS/DWM, xem mục 6) hoặc `null` nếu timeout (không có thay đổi màn hình trong khoảng chờ — `AcquireNextFrame` timeout là hành vi bình thường, không phải lỗi).
- **Ghi chú hành vi trên secure desktop** (UAC prompt, màn hình khoá): Desktop Duplication API **không** capture được nội dung secure desktop theo thiết kế bảo mật của Windows (giới hạn OS, không phải giới hạn của app) — `AcquireNextFrame` sẽ timeout hoặc trả khung trống trong lúc đó. Đây là hành vi chấp nhận được (không có nội dung nào hiển thị cho trẻ trong lúc màn hình khoá/UAC), không cần xử lý đặc biệt.

### 4.2 Bước 2+3 — Crop GPU-side + Readback + Resize/Normalize

**Quyết định gộp buffer (ADR-41)**: thay vì tạo 1 texture `DEFAULT` trung gian cho bước crop rồi copy tiếp sang staging texture để đọc CPU, `GpuWindowCropper.CopySubresourceRegion(stagingTexture, ..., duplicationFrameTexture, ..., sourceBox: cropRectInMonitorSpace)` copy **thẳng** từ frame toàn màn hình vào **1 staging texture** (`D3D11_USAGE_STAGING`, `CPU_ACCESS_READ`) đã cấp phát sẵn đúng kích thước cửa sổ. `CopySubresourceRegion` vẫn là lệnh GPU thuần tuý (không đọc CPU) bất kể texture đích là loại gì — đúng nghĩa đen `IMG-012` ("vẫn trên GPU, chưa readback CPU"); "readback" thực sự chỉ xảy ra ở bước `Map()` ngay sau đó. Giảm 1 buffer cần quản lý/zero-out so với thiết kế 2-texture.

- Ngay sau `CopySubresourceRegion`, gọi `duplicationInterface.ReleaseFrame()` **càng sớm càng tốt** — trả quyền sở hữu frame toàn màn hình lại cho OS (xem mục 6, không phải buffer của Vision).
- `context.Map(stagingTexture, ..., D3D11_MAP_READ, ...)` → con trỏ tới vùng nhớ GPU-CPU shared; copy dữ liệu BGRA8 ra `byte[]` quản lý (tái dùng buffer nếu cùng kích thước — `PERF-040`); **ghi đè vùng con trỏ đã Map bằng 0** trước khi `Unmap()` (mục 6); `Unmap()`.
- **Bổ sung v0.3.0 (Đợt 7, `PERF-011`/`IMG-011`)**: ngay sau khi có `byte[]` BGRA8 ở bước trên, chèn **hash-gate** (`ComputeDHash64`, mục 3.8.1) **trước** khi gọi `FrameResizerNormalizer` — nếu `content_changed = false` (so với hash cửa sổ này ở chu kỳ trước), **bỏ qua hoàn toàn** `FrameResizerNormalizer` + bước 4/5 (mục 4.3), nhảy thẳng xuống zero-out (mục 6) + dùng `CachedRiskScore` (mục 3.8.2) khi build `VisionInferenceResult` (mục 4.4). Nếu `content_changed = true`, tiếp tục bình thường như dưới đây.
- **`FrameResizerNormalizer`**: bilinear resize thủ công (không qua `SixLabors.ImageSharp`/`System.Drawing` — ADR-51, giảm dependency và giảm 1 buffer object trung gian) đọc trực tiếp từ `byte[]` BGRA8 nguồn, ghi trực tiếp vào `DenseTensor<float>` đích kích thước cố định theo input model (`224×224×3` — `IMG-014`/`PERF-032`), kết hợp luôn normalize (rescale + trừ mean/chia std hoặc `[-1,1]` tuỳ đúng phép `preprocess_input` của model gốc — **xác nhận công thức chính xác khi implement, đối chiếu với bước tiền xử lý gốc của `GantMan/nsfw_model`**, vì đây là chi tiết ảnh hưởng độ chính xác, cần review cùng lúc benchmark ngưỡng risk score đã ghi ở câu hỏi mở `09-image-processing-spec.md` mục 8).
- `DenseTensor<float>` input **cấp phát đúng 1 lần lúc Vision khởi động, tái dùng suốt vòng đời process** (kích thước luôn cố định `224×224×3`, không phụ thuộc kích thước cửa sổ — khác `byte[]`/staging texture phải resize-recreate khi cửa sổ đổi kích thước).

### 4.3 Bước 4+5 — Inference + tổng hợp risk score

- **`NsfwClassifier`** (namespace `ParentalGuard.Vision.Inference`): sở hữu 1 `InferenceSession` **khởi tạo eager, đúng 1 lần** ngay sau khi Vision hoàn tất handshake IPC đầu tiên (không đợi `ControlVisionCommand` đầu tiên bật monitoring — model cần sẵn sàng trước để không phát sinh độ trễ ở lần capture đầu tiên khi resume/enable), **tái dùng cho mọi frame** tới khi process thoát (ADR-44). Tạo `InferenceSession` mới mỗi frame **không được chọn** — chi phí load model + compile execution provider mỗi lần là quá lớn so với ngân sách 1 frame.
- **Xác định tên input + layout tensor động (ADR-48)**: đọc `session.InputMetadata` lúc khởi tạo để lấy đúng tên input node và thứ tự trục (NHWC mặc định của Keras/TensorFlow hay NCHW nếu bước convert `tf2onnx` có transpose) — không hard-code 1 layout cụ thể, vì phụ thuộc vào cách file `.onnx` thực tế được convert (công đoạn chuẩn bị asset, ngoài phạm vi code Vision).
- `session.Run(...)` → output 5 xác suất lớp (`drawing`/`hentai`/`neutral`/`porn`/`sexy`, `IMG-014`).
- **`RiskScoreAggregator`** (ADR-47, **PROPOSED** — chờ xác nhận benchmark, liên kết câu hỏi mở đã có ở `09-image-processing-spec.md` mục 8): `risk_score = P(hentai) + P(porn) + P(sexy)` (bỏ `drawing` — tranh vẽ không nhạy cảm thực sự, `neutral` — nội dung an toàn), clamp `[0.0, 1.0]` (đề phòng sai số dấu phẩy động khi tổng 3 lớp vượt nhẹ 1.0). Đây là công thức phổ biến trong cộng đồng dùng `GantMan/nsfw_model`, được chọn vì đơn giản/minh bạch, **không** phải quyết định cuối cùng — sẽ được xác nhận/hiệu chỉnh cùng lúc benchmark chọn ngưỡng mặc định (`BE-090`), đúng đúng tinh thần "chi tiết công thức để ở System Design" mà `IMG-014` đã uỷ quyền.

### 4.4 Bước 7 — Gửi kết quả

- **`VisionInferenceResult`** (đã chốt schema ở `03-ipc-communication.md` mục 3.3, không định nghĩa lại): `FrameId` (bộ đếm `Interlocked.Increment`, reset mỗi lần Vision khởi động lại — chỉ phục vụ chẩn đoán, không có ý nghĩa bảo mật), `WindowHandle` (cast HWND → `fixed64`), `MonitorId` (chỉ số output DXGI theo thứ tự `EnumOutputs`, ổn định trong 1 phiên chạy — lược đồ định danh bền vững hơn để ở Đợt 2), `RiskScore` (kết quả mục 4.3, **hoặc `CachedRiskScore` mục 3.8.2 nếu bị skip**), `Bbox` (chính là `cropRectInMonitorSpace` đã dùng ở bước 2, quy đổi lại toạ độ màn hình tuyệt đối — **luôn điền**, không bỏ trống theo điều kiện ngưỡng, đơn giản hoá vì chi phí gần như 0 và vẫn hợp lệ với ngữ nghĩa "có thể rỗng" ở `03` là 1 khả năng được phép chứ không bắt buộc; **luôn tính lại mỗi chu kỳ kể cả khi `content_changed=false`**, mục 3.8.2), `CapturedAtUnixMs` (timestamp lấy ở đầu bước 1, **luôn cập nhật mỗi chu kỳ** dù risk score có tái dùng hay không), `ProcessName` (bổ sung v0.2.1, Đợt 6, `MISC-030`, `03` ADR-111) — **tái dùng nguyên giá trị** `ForegroundWindowTracker` đã resolve ở bước 1 (mục 4.1, dùng cho exclude-list `BE-073a`), gán thẳng vào field này khi build `VisionInferenceResult`, **không** resolve thêm lần thứ 2 nào (chi phí đã trả 1 lần), **`ContentChanged`** (field mới v0.3.0, `PERF-010`/`011`, `03` ADR-134 — amendment cùng lượt): kết quả hash-gate mục 3.8.1, **luôn điền** (không optional) — `Service` dùng field này làm tín hiệu duy nhất cho state machine mục 3.7, `Vision` không tự diễn giải ý nghĩa gì thêm ngoài đúng giá trị boolean đã tính.
- `CaptureLoopWorker` gọi `ipcClient.EnqueueOutbound(payload)` (mục 3.2) — không tự ghi pipe.

## 5. ONNX Runtime session — DirectML → CPU fallback (`PERF-030`/`031`)

- **Package**: `Microsoft.ML.OnnxRuntime.DirectML` (NuGet chính thức Microsoft) — ADR-50 mở rộng.
- **Khởi tạo (ADR-45)**: `SessionOptions` → gọi `DisableTelemetryEvents()` (hardening bắt buộc, `IMG-015`) → thử `sessionOptions.AppendExecutionProvider_DML(deviceId: 0)`. Nếu ném exception (driver/GPU không hỗ trợ DirectML) → `catch`, log **không được** (Vision không ghi file — `SEC-017`/`BE-022`), thay vào đó set `HeartbeatAck.DiagnosticState = "ep=cpu-fallback"` (field free-text đã có sẵn, chỉ phục vụ chẩn đoán, **không** dùng để kích hoạt logic nghiệp vụ ở phía Service — đúng đúng giới hạn đã ghi rõ ở `03-ipc-communication.md` mục 3.2) và tiếp tục tạo `InferenceSession` với CPU execution provider mặc định (`PERF-031`). Không có cơ chế retry lại DirectML giữa chừng — quyết định EP cố định cho suốt vòng đời 1 process `Vision` (đơn giản, tránh runtime-detection phức tạp không cần thiết).
- **`FE-041`** (cảnh báo hiệu năng CPU-fallback hiển thị trong Dashboard) là tính năng UI thuộc Đợt 6 — Đợt 1 chỉ đảm bảo tín hiệu chẩn đoán `ep=cpu-fallback` đã có sẵn trong heartbeat để Đợt 6 tận dụng sau, không cần thêm cơ chế mới lúc đó.

## 6. Zero-out buffer chi tiết (`IMG-003`)

Nguyên tắc bao trùm: **mỗi bước tự chịu trách nhiệm zero-out buffer nó vừa dùng xong, trong 1 khối `try/finally` cục bộ của chính bước đó** — không dồn zero-out vào 1 `finally` duy nhất ở cuối `FrameClassificationPipeline`, để đảm bảo nếu exception xảy ra ở bước N, buffer của các bước 1..N-1 vẫn được zero đúng lúc (mỗi `finally` cục bộ chạy trước khi exception tiếp tục lan lên bước trên).

| Buffer | Sở hữu | Zero khi nào | Cơ chế cụ thể |
|---|---|---|---|
| Texture nội bộ `IDXGIOutputDuplication` (toàn màn hình, bước 1) | **OS/DWM**, không phải Vision | **Không zero trực tiếp** — không sở hữu, không có quyền/nghĩa vụ ghi đè bộ nhớ của interface hệ thống; thay vào đó **giảm thiểu thời gian giữ**: `ReleaseFrame()` ngay sau `CopySubresourceRegion` | `ReleaseFrame()` gọi càng sớm càng tốt (ADR-42) |
| Staging texture crop (bước 2, GPU-CPU shared) | Vision, **tái dùng** giữa các frame cùng kích thước (`PERF-040`) | Ngay sau `Map()` + copy dữ liệu ra `byte[]` quản lý, **trước** `Unmap()` | Ghi 0 vào vùng con trỏ trả về bởi `Map()` (`NativeMemory`/`Unsafe.InitBlockUnaligned` trên con trỏ đó) trước khi gọi `Unmap()`. Nếu cửa sổ đổi kích thước → texture cũ bị `UpdateSubresource` toàn 0 rồi `Dispose()` trước khi cấp phát texture mới đúng size |
| `byte[]` pixel BGRA8 thô (sau readback, trước resize) | Vision, tái dùng nếu cùng kích thước | **Cập nhật v0.3.0**: trong `finally` ngay sau **cả 2** bên tiêu thụ đã đọc xong — `ComputeDHash64` (mục 3.8.1, luôn chạy) **và** `FrameResizerNormalizer` (mục 4.2, chỉ chạy nếu `content_changed = true`); nếu hash-gate quyết định skip, zero ngay sau khi hash tính xong (không chờ 1 bước không bao giờ chạy) | `Array.Clear(buffer, 0, buffer.Length)` |
| `luma[9,8]` downsample cho pHash (72 byte, mới v0.3.0, mục 3.8.1) | Vision, cấp phát mới mỗi frame (72 byte — không đáng để pool) | Ngay sau khi `ComputeDHash64` tính xong `hash` từ mảng này | `Array.Clear`/`Span.Clear()` trước khi biến ra khỏi scope — **không** áp dụng cho chính `hash` (`ulong`, 8 byte) hay `CachedRiskScore` (`float`), 2 giá trị này được phép tồn tại lâu hơn 1 chu kỳ theo đúng ngoại lệ `IMG-011` (mục 3.8.3) |
| `DenseTensor<float>` input model (224×224×3, cố định) | Vision, tái dùng suốt vòng đời process | Trong `finally` ngay sau `session.Run(...)` trả về — **chỉ áp dụng khi `content_changed = true`** (khi skip, bước này không chạy nên không có gì để zero) | `tensor.Buffer.Span.Clear()` |
| Output tensor (5 xác suất lớp) | Vision, cấp phát mới mỗi frame (kích thước 5 float — không đáng để pool) | Ngay sau khi `RiskScoreAggregator` đọc xong — **chỉ áp dụng khi `content_changed = true`** | `Array.Clear`/`Span.Clear()` trước khi biến ra khỏi scope |
| `VisionInferenceResult` (Protobuf message đã gửi) | Vision, tạm thời | Không chứa dữ liệu pixel — chỉ 7 field số/toạ độ/boolean (`03` mục 3.3, +`ContentChanged` v0.3.0) — không thuộc phạm vi `IMG-003` (không phải "dữ liệu ảnh"), không cần zero riêng |

**Không phụ thuộc GC ở bất kỳ dòng nào trong bảng trên** — mọi zero-out là lệnh đồng bộ (`Array.Clear`/`Span.Clear`/ghi con trỏ native) thực thi ngay tại điểm code xác định, không chờ Garbage Collector đến lượt dọn object đó (đúng nghĩa đen `IMG-003`).

**Điểm giao thoa với `PERF-040`**: tái dùng allocation (staging texture, `byte[]`, `DenseTensor<float>`) qua nhiều frame không mâu thuẫn với "zero-out ngay sau khi dùng xong" — 2 yêu cầu tách biệt: `PERF-040` nói về **cấp phát** (đừng `new` lại mỗi frame), `IMG-003` nói về **nội dung** (đừng để dữ liệu cũ tồn tại lâu hơn cần thiết). Zero nội dung mỗi chu kỳ trong khi vẫn tái dùng object là cách thoả cả 2 cùng lúc (ADR-43) — đặc biệt quan trọng vì interval capture có thể lên tới 5 giây ở nội dung tĩnh (`PERF-010`), nếu không zero mỗi chu kỳ, dữ liệu pixel cũ sẽ tồn tại trong bộ nhớ GPU/RAM suốt khoảng thời gian đó.

## 7. Kiểm tra toàn vẹn model AI khi load (`MISC-090`)

- **`OnnxChecksumVerifier`**: đọc toàn bộ `<installdir>\models\*.onnx` thành `byte[]` **1 lần** → tính SHA-256 → so với 1 hằng số hash nhúng sẵn trong `ParentalGuard.Vision.exe` lúc build (checksum của đúng file `.onnx` được đóng gói cùng bản release đó — model không có cơ chế auto-update, `GEN-034`/`MISC-020` REJECTED, nên checksum chỉ đổi khi có bản release mới, đồng bộ với chính binary `Vision.exe`) — **không** cần Vision đọc `config.db` hay nhận checksum kỳ vọng qua IPC (giữ đúng nguyên tắc single-purpose, `SEC-017`), đây là lựa chọn tự-đủ (self-contained), ADR-46.
- Load `InferenceSession` **thẳng từ `byte[]` đã verify** (constructor `InferenceSession(byte[], SessionOptions)`), không đọc lại file theo đường dẫn lần thứ 2 — tránh TOCTOU (Time-Of-Check-Time-Of-Use: file có thể bị thay đổi giữa lúc verify và lúc load nếu đọc 2 lần).
- **Nếu checksum không khớp**: coi là tamper — Vision **không** load model, **không** cố gắng chạy pipeline ở trạng thái model không tin cậy. Gọi `Environment.Exit(18)` (mã thoát riêng, xem bảng mã thoát mục 8) ngay lập tức. `Service` phát hiện qua exit code + pipe vỡ, xử lý **như crash bình thường** (`BE-023`) — không có xử lý đặc biệt gì thêm ở Đợt 1 (khác biệt duy nhất là exit code `18` giúp `Service` ghi đúng `event_type` vào audit log nếu muốn phân biệt "crash thường" với "model integrity failure", chi tiết audit log cụ thể để ở lúc code, không bắt buộc thiết kế thêm cơ chế cảnh báo chủ động riêng — sự kiện lặp lại sẽ tự động rơi vào rate-limit `ANTI-060` sẵn có, đúng khuôn mẫu đã áp dụng cho `VisionNetworkBlocked` ở `06-security-architecture.md` mục 3.3/ADR-34).

## 8. Fallback Low Integrity Level ↔ Medium IL cho Desktop Duplication API

Trả lời câu hỏi mở `06-security-architecture.md` mục 7 (dòng 1): *"Validate thực nghiệm Low IL có tương thích DXGI hay không."*

### 8.1 Chiến lược đã chọn: thử Low IL trước, fallback Medium IL qua exit code riêng — không runtime-detection phức tạp (ADR-49)

1. `Service` spawn `Vision` **mặc định ở Low IL** (đúng `ADR-30` ở `06-security-architecture.md`, không đổi).
2. `Vision`, ngay sau khi handshake IPC thành công (Hello/HelloAck), thử khởi tạo `DesktopDuplicationCapture` (mục 4.1) — tạo `ID3D11Device` + `IDXGIOutputDuplication` cho output hiện tại **trước khi** bắt đầu `CaptureLoopWorker`.
3. Nếu bước khởi tạo thất bại với HRESULT thuộc nhóm "quyền" (`E_ACCESSDENIED`, hoặc `DXGI_ERROR_UNSUPPORTED` xảy ra ngay ở lần thử đầu tiên — phân biệt bảo thủ: **bất kỳ lỗi nào xảy ra ở lần khởi tạo đầu tiên** đều coi là thuộc nhóm "có thể do quyền", vì tại thời điểm này chưa có dữ liệu thực nghiệm để phân loại chính xác hơn — Vision **không tự đổi Integrity Level của chính nó** (không thể — IL được ấn định cố định lúc `CreateProcessAsUser`, 1 process không tự hạ/nâng IL của mình) → gọi `Environment.Exit(17)` ngay lập tức, đóng kết nối IPC.
4. `Service` phát hiện `Vision` thoát (pipe vỡ, đúng bảng xử lý lỗi `03-ipc-communication.md` mục 6) → đọc exit code qua `GetExitCodeProcess`. Nếu bằng **17**: `Service` respawn `Vision` lần này ở **Medium IL** thay vì Low IL — bỏ qua bước 5 (`SetTokenInformation(..., Low Mandatory Level)`) trong pipeline token ở `06-security-architecture.md` mục 2.1, giữ nguyên bước 4 (`CreateRestrictedToken` + `DISABLE_MAX_PRIVILEGE` — đúng phương án D đã ghi sẵn ở `06` mục 2.2 làm fallback).
5. `Service` **nhớ quyết định này chỉ trong bộ nhớ** (1 cờ runtime trên `MonitoringState`, ví dụ `VisionRequiresMediumIl: bool`) cho **hết phiên chạy hiện tại** (không ghi vào `config.db`) — mọi lần respawn tiếp theo trong cùng phiên (crash-restart, đổi session) dùng thẳng Medium IL, không thử lại Low IL mỗi lần (tránh vòng lặp thử-fail-fallback lặp lại tốn thời gian ở mỗi lần respawn). Khi `Service` khởi động lại từ đầu (reboot/SCM restart) → **thử lại Low IL từ đầu**, chấp nhận chi phí 1 lần thử-fail-fallback nhanh (nằm gọn trong ngân sách `≤ 3 giây` của `BE-023`, không đáng kể) thay vì thêm 1 field bền vững vào schema `config.db` (`04-data-architecture.md`) cho 1 sự thật phần cứng gần như tĩnh — đúng tinh thần "ưu tiên least-privilege (thử Low trước) nhưng không thiết kế runtime-detection quá phức tạp" đã yêu cầu.
6. `Service` ghi 1 record audit log `event_type = "VisionCaptureFallbackMediumIl"` khi việc fallback xảy ra lần đầu trong phiên (không lặp lại ghi log mỗi lần respawn tiếp theo trong cùng phiên — chỉ ghi tại đúng lúc cờ đổi từ chưa-biết sang Medium, để phụ huynh nhìn thấy qua Dashboard rằng máy này cần chạy Vision ở mức quyền cao hơn dự kiến).

### 8.2 Bảng mã thoát dùng để phân biệt loại thất bại

| Exit code | Ý nghĩa | Hành động của `Service` khi respawn |
|---|---|---|
| `17` | `CaptureInitAccessDenied` — khởi tạo DXGI Desktop Duplication thất bại ở lần thử đầu (nghi ngờ do Integrity Level) | Respawn ở Medium IL (mục 8.1), nhớ trong bộ nhớ cho hết phiên, ghi audit log 1 lần |
| `18` | `ModelIntegrityCheckFailed` — checksum `.onnx` không khớp (`MISC-090`, mục 7) | Respawn bình thường (không đổi IL) — xử lý như crash thường, rơi vào rate-limit `ANTI-060` nếu lặp lại |
| (khác — crash không kiểm soát, exception chưa bắt) | Crash thường (`BE-023`) | Respawn bình thường ở IL hiện hành (Low, hoặc Medium nếu đã fallback trong phiên này) |

**`DXGI_ERROR_ACCESS_LOST` không nằm trong bảng trên** — đây là lỗi xảy ra **trong lúc capture đang chạy bình thường** (đổi độ phân giải, khoá màn hình, secure desktop — mục 4.1), không phải lỗi khởi tạo, xử lý bằng cách `Dispose` + tạo lại `IDXGIOutputDuplication` ngay trong `CaptureLoopWorker` (không thoát process, không phải tín hiệu fallback IL).

**Bổ sung cần làm ở lượt sửa `04-data-architecture.md` kế tiếp** (không tự sửa ở đây, theo đúng nguyên tắc mỗi lượt chỉ viết 1 file — giống cách đã xử lý `VisionNetworkBlocked` ở `06` mục 7): thêm 2 dòng `event_type` (`VisionCaptureFallbackMediumIl`, và ghi chú `ModelIntegrityCheckFailed` nếu `Service` muốn phân biệt lý do restart trong log) vào bảng `event_type` ở `04-data-architecture.md` mục 5.1 — không chặn tiến độ Đợt 1 vì schema audit log chấp nhận `event_type` dạng chuỗi tự do (đã ghi nhận ở `06`).

## 9. Kiểm tra tuân thủ "không lưu trữ" tự động (`IMG-040`/`IMG-041`)

Thiết kế 3 lớp kiểm tra, đúng 3 gạch đầu dòng đã liệt kê ở `09-image-processing-spec.md` mục 7, đủ chi tiết để `security-privacy-auditor` biết kiểm tra gì:

1. **Quét thư mục temp + file log thực tế** (bắt buộc, tự động hoá được đầy đủ): 1 test tích hợp (`ParentalGuard.Vision.ComplianceTests`, chạy trong `11-testing-qa-process.md` Feature Gate) — chạy `Vision` thật với 1 bộ ảnh test mô phỏng nội dung nhạy cảm (dataset benchmark nội bộ, không public) hiển thị trong 1 cửa sổ giả lập trong X phút (X cấu hình được, đề xuất khởi điểm 5 phút) → sau đó quét:
   - Toàn bộ `%TEMP%` (user + system) tìm file có magic bytes của định dạng ảnh phổ biến (PNG `89 50 4E 47`, JPEG `FF D8 FF`, BMP `42 4D`) được tạo/sửa trong khoảng thời gian test chạy.
   - Nội dung thật của `%ProgramData%\ParentalGuard\audit.log` — parse từng dòng JSONL, assert không có field nào chứa chuỗi dài bất thường có thể là base64 của ảnh (heuristic: length > N ký tự liên tục không phải timestamp/UUID/hash cố định độ dài đã biết) và không có magic bytes ảnh dạng raw.
   - Test **fail cứng** (không phải warning) nếu tìm thấy bất kỳ match nào — đúng `IMG-041` ("bắt buộc pass").
2. **Heap dump (best-effort, không chặn CI)**: dùng `dotnet-gcdump` chụp snapshot heap managed của `Vision` process ngay giữa lúc đang chạy test kịch bản mục 1 (thời điểm ngẫu nhiên, không đồng bộ với chu kỳ zero-out để tăng khả năng bắt được nếu có buffer "quên" zero) → phân tích bằng script tìm object `byte[]`/`float[]` có kích thước khớp các buffer đã biết ở bảng mục 6 (ví dụ `byte[]` kích thước ≈ `width × height × 4` cho BGRA8) **và** nội dung khác toàn-0 tại thời điểm chụp. Đây chỉ bắt được **managed heap** — không bắt được GPU memory (staging texture) hay memory native ngoài CLR, ghi nhận rõ giới hạn này (đúng chữ "nếu công cụ cho phép" ở `IMG-040` — không phải yêu cầu tuyệt đối phải bao phủ 100% mọi loại bộ nhớ). Kết quả bất thường → cảnh báo cho reviewer, không tự động fail CI (phân biệt với mục 1, vốn fail cứng).
3. **Instrumentation hook nội bộ (chỉ build Test/Debug, không có trong Release)**: 1 interface `IFrameBufferAuditor` (no-op mặc định trong Release build qua conditional compilation, chỉ implement thật trong test build qua `InternalsVisibleTo`) — mỗi buffer ở bảng mục 6 gọi `auditor.OnZeroed(bufferId, sizeBytes)` ngay sau bước zero-out, test harness đếm số lần `OnZeroed` khớp đúng số lần buffer được cấp phát/dùng trong 1 phiên chạy (không buffer nào "dùng xong mà không zero"). Hook này **không** xuất hiện trong bất kỳ đường IPC/production nào — chỉ compiled vào assembly test, tránh tạo thêm bề mặt tấn công hay rò rỉ thông tin chẩn đoán ra production build.

Cả 3 lớp trên là tiêu chí **bắt buộc pass trước khi Đợt 1 được coi là "hoàn thành"** theo đúng `IMG-041` và `ROADMAP.md` mục 4 (Đợt 1: "Compliance check tự động `IMG-040`/`IMG-041`... chạy ngay từ Đợt này, không để dồn về sau").

## 9a. Hook cho benchmark đa cấu hình máy (`PERF-060`–`063`, mới Đợt 7)

Không thiết kế công cụ benchmark ở đây (thuộc Đợt 9/vận hành, đúng phạm vi đã giao) — chỉ xác nhận **dữ liệu cần thiết đã có sẵn đủ** để benchmark sau này thực hiện được, theo đúng bảng chỉ số ở `08-performance-cpu-spec.md` mục 7 (cột "Công cụ đo" của chính spec đã chỉ định công cụ **ngoài ứng dụng**, không phải cơ chế app tự ghi log riêng):

| Chỉ số (`08` mục 7) | Công cụ đo (đã ghi trong spec) | Dữ liệu ứng dụng cần có sẵn (xác nhận) |
|---|---|---|
| CPU trung bình lúc idle-content / CPU đỉnh lúc inference | Windows Performance Counter (ngoài app) | Không cần gì thêm từ `Vision`/`Service` — Performance Counter đo trực tiếp theo PID, không phụ thuộc app tự báo cáo. Để đối chiếu đúng thời điểm đo với bucket nào đang active (`Vigilant`/`Relaxed`/`Boost`, mục 3.7), benchmark harness có thể đọc `HeartbeatAck.diagnostic_state` (free-text đã có, mục 5/ADR-45) — **mở rộng quy ước** (không đổi schema): `Service` có thể nối thêm đoạn tuỳ chọn dạng `"...;interval_ms=1000;vigilance=Vigilant"` vào chuỗi đã có, thuần chẩn đoán, cùng nguyên tắc "không dùng để rẽ nhánh nghiệp vụ" đã áp dụng cho `ep=cpu-fallback` |
| Độ trễ nội dung xuất hiện → overlay hiện | Đo thủ công/script test tự động | `VisionInferenceResult.CapturedAtUnixMs` (đã có từ Đợt 1) + timestamp `ContentBlocked` trong `audit.log` (đã có, `04-data-architecture.md` mục 5.1) đủ để script test tính độ trễ end-to-end, không cần field mới |
| RAM ổn định sau 24h | Task Manager / .NET diagnostics (ngoài app) | Không cần gì thêm — đo trực tiếp theo PID |

**Kết luận**: không cần thêm bất kỳ field/schema/cơ chế ghi log mới nào cho riêng mục đích benchmark — toàn bộ dữ liệu cần thiết đã tồn tại sẵn từ thiết kế các Đợt trước, chỉ cần 1 mở rộng quy ước tuỳ chọn (không bắt buộc, không đổi hợp đồng IPC) ở `diagnostic_state` để benchmark harness dễ đối chiếu bucket đang active (ADR-135).

## 10. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-38 | Vòng lặp Capture-Inference chạy trên 1 `Thread` chuyên dụng (`IsBackground = true`), không dùng `Task.Run`/ThreadPool | Native blocking call (DXGI/ONNX) không được chiếm ThreadPool worker; `ID3D11DeviceContext` immediate context không thread-safe — 1 thread cố định loại bỏ nhu cầu lock |
| ADR-39 | Mở rộng `IpcChildClient` (Đợt 0) thành Reader loop + Writer loop song song, dùng chung 1 `Channel<IpcPayload>` outbound cho cả `HeartbeatAck` lẫn `VisionInferenceResult` | Tránh ghi đồng thời từ 2 nơi lên cùng 1 `NamedPipeClientStream` làm interleave byte, vỡ framing (`03` mục 2.3) |
| ADR-40 | Pause/suspend `Vision` tái dùng field `ControlVisionCommand.MonitoringEnabled` có sẵn, không `SuspendThread`/Job Object/message mới | Trả lời câu hỏi mở `02-process-architecture.md` mục 8; đơn giản, an toàn hơn `SuspendThread` (rủi ro deadlock), không cần đổi schema IPC |
| ADR-41 | `CopySubresourceRegion` crop thẳng từ frame toàn màn hình vào 1 staging texture CPU-accessible, bỏ texture `DEFAULT` trung gian | Giảm 1 buffer cần quản lý/zero-out; vẫn đúng nghĩa "GPU-side, chưa readback CPU" của `IMG-012` (readback là `Map()`, không phải `CopySubresourceRegion`) |
| ADR-42 | Không zero-out trực tiếp texture nội bộ của `IDXGIOutputDuplication` — chỉ `ReleaseFrame()` nhanh nhất có thể | Buffer đó thuộc sở hữu OS/DWM, không phải Vision — không có quyền/nghĩa vụ ghi đè; giảm thiểu thời gian giữ là biện pháp tương đương khả thi |
| ADR-43 | Tái dùng allocation (staging texture, `byte[]`, `DenseTensor<float>`) qua nhiều frame, nhưng vẫn zero **nội dung** mỗi chu kỳ | Thoả đồng thời `PERF-040` (object pooling, tránh cấp phát lại mỗi frame) và `IMG-003` (không để dữ liệu cũ tồn tại lâu hơn cần thiết, đặc biệt quan trọng khi interval lên tới 5 giây) |
| ADR-44 | `InferenceSession` khởi tạo 1 lần (eager, ngay sau handshake IPC đầu tiên), tái dùng suốt vòng đời process | Tránh chi phí load model + compile execution provider lặp lại mỗi frame — ảnh hưởng hiệu năng nghiêm trọng nếu tạo mới mỗi lần |
| ADR-45 | Thử `AppendExecutionProvider_DML` trước, fallback CPU EP nếu exception; báo trạng thái qua `HeartbeatAck.DiagnosticState` (free-text, chỉ chẩn đoán) | Đúng `PERF-030`/`031`; tái dùng field đã có sẵn, không đổi schema IPC, không biến chẩn đoán thành kênh điều khiển nghiệp vụ (giữ đúng giới hạn đã ghi ở `03` mục 3.2) |
| ADR-46 | `MISC-090` verify bằng SHA-256 nhúng hằng số trong `Vision.exe`, đọc file 1 lần rồi load thẳng `byte[]` đã verify vào `InferenceSession` | Tự-đủ (không cần Vision đọc `config.db`/nhận checksum qua IPC, giữ nguyên tắc single-purpose `SEC-017`); tránh TOCTOU giữa lúc verify và lúc load |
| ADR-47 | Công thức risk score = `P(hentai) + P(porn) + P(sexy)`, clamp `[0,1]` — **PROPOSED**, chờ benchmark | `IMG-014` uỷ quyền quyết định công thức cho System Design; công thức chọn theo quy ước phổ biến cộng đồng `GantMan/nsfw_model`, cần xác nhận cùng lúc chọn ngưỡng mặc định (câu hỏi mở đã có ở `09` mục 8) |
| ADR-48 | Đọc `session.InputMetadata` để xác định tên input + layout tensor (NHWC/NCHW) tại runtime, không hard-code | Phụ thuộc cách file `.onnx` thực tế được convert (`tf2onnx`), tránh sai lệch nếu layout khác giả định |
| ADR-49 | Fallback Low IL → Medium IL qua exit code riêng (`17`) do `Vision` tự chọn khi thoát, `Service` đọc exit code lúc respawn để quyết định IL — chỉ nhớ trong bộ nhớ `Service` cho hết phiên, không ghi `config.db` | 1 process không tự đổi IL của chính nó; đơn giản hơn cơ chế runtime-detection phức tạp; chấp nhận chi phí thử-lại-mỗi-lần-reboot thay vì thêm field bền vững cho 1 sự thật phần cứng gần như tĩnh |
| ADR-50 | Dùng thư viện binding `Vortice.Windows` (MIT, đang maintain) cho DXGI/Direct3D11, không dùng SharpDX (deprecated)/P-Invoke tay | Giảm rủi ro bảo trì dài hạn cho dự án cộng đồng 1 người, tránh viết lại toàn bộ interop COM tay |
| ADR-51 | Bilinear resize + normalize viết tay, ghi trực tiếp vào `DenseTensor<float>`, không qua `SixLabors.ImageSharp`/`System.Drawing` | Giảm 1 buffer object trung gian (ít điểm cần zero-out hơn — hỗ trợ trực tiếp `IMG-003`), giảm dependency ngoài |
| ADR-63 | `monitor_id` = chỉ số enumerate `IDXGIOutput`, đối chiếu với `HMONITOR` của cửa sổ qua `IDXGIOutput.GetDesc().Monitor == MonitorFromWindow(hwnd)` | Cầu nối chuẩn giữa DXGI (Vision cần để chọn đúng output capture) và GDI/Win32 (Overlay dùng để resolve bounds cục bộ, `07-overlay-architecture.md`) mà không cần 1 bên tính toán/truyền toạ độ màn hình cho bên kia |
| ADR-64 | Danh sách cửa sổ cần xử lý mỗi chu kỳ = `[GetForegroundWindow()]` + tối đa 1 cửa sổ topmost-theo-Z-order cho mỗi màn hình còn lại chưa có candidate (dừng sớm khi đã phủ hết số màn hình) | Hiện thực hoá `IMG-020` (ưu tiên cửa sổ có focus thực sự) + `BE-082` (chỉ capture màn hình có cửa sổ liên quan) bằng 1 thuật toán đơn, không cần khái niệm "focus theo từng màn hình" (Windows không có sẵn khái niệm này) |
| ADR-65 | `OutputCaptureContext` (mỗi output 1 `IDXGIOutputDuplication`) tạo lazy khi cần, dispose sau ≥ 5 chu kỳ liên tiếp không có cửa sổ candidate nào trên output đó | Tránh capture liên tục N màn hình khi chỉ 1-2 màn hình có cửa sổ cần giám sát (đúng tinh thần `PERF-020`/`BE-082`); ngưỡng 5 chu kỳ tránh dispose/recreate rung lắc khi cửa sổ dao động qua lại biên màn hình |
| ADR-128 (v0.3.0) | pHash tự viết tay dạng difference-hash (dHash) 64-bit từ 72 điểm lấy mẫu strided, không dùng thư viện pHash ngoài (NuGet) | 2 frame so sánh luôn cùng cửa sổ/độ phân giải/không biến dạng hình học — không cần full DCT-pHash; dHash rẻ hơn nhiều, bắt buộc rẻ hơn hẳn chi phí Inference mà nó thay thế; cùng tinh thần giảm dependency đã áp dụng ở ADR-50/51 |
| ADR-129 (v0.3.0) | `PERF-010` (đổi tần suất) và `PERF-011` (skip inference) dùng CHUNG đúng 1 phép so sánh pHash mỗi chu kỳ (`content_changed`), không tính 2 lần | Tránh lãng phí tính pHash 2 lần cho 2 mục đích trùng lặp bản chất; đơn giản hoá luồng dữ liệu — 1 tín hiệu, 2 nơi tiêu thụ (`Vision` cục bộ + `Service` qua IPC) |
| ADR-130 (v0.3.0) | Hysteresis bất đối xứng: chuyển bucket `Vigilant` (nhanh hơn) áp dụng ngay từ tín hiệu đầu tiên; chuyển `Relaxed` (chậm hơn) cần `N_RELAX` frame liên tiếp `content_changed=false` | Ưu tiên an toàn hơn tiết kiệm CPU khi không chắc (chỉ đạo rõ của chủ dự án) — phản ứng tức thời khi cần cảnh giác hơn, thận trọng khi nới lỏng |
| ADR-131 (v0.3.0) | `performance_mode` (Đợt 6) là **trần nới lỏng tối đa** cho bucket `Relaxed`, không phải 1 tần suất cố định độc lập với Adaptive Frame Rate (Đợt 7); `"maximum_protection"` khiến `Relaxed` trùng giá trị `Vigilant` (không bao giờ nới lỏng thực sự) | Giải quyết rõ quan hệ 2 tầng theo đúng yêu cầu; khớp nghĩa đen `PERF-050b` ("Cân bằng: theo chiến lược adaptive"; "Bảo vệ tối đa: tăng tần suất, chấp nhận CPU cao hơn") mà không cần sửa `Specification/` |
| ADR-132 (v0.3.0) | Trạng thái adaptive (`WindowFrameRateState`: `Vigilance`/`UnchangedStreak`/`Boost`) là domain-state RAM-only mới ở `Service`, theo dõi theo `window_handle`, tổng hợp bằng MIN(interval) toàn bộ cửa sổ candidate | Đúng nguyên tắc `ADR-12`/`ADR-13` (`02-process-architecture.md`) — domain-state tách riêng, không persist vì không phải dữ liệu bảo mật/cấu hình, an toàn nhất khi reset về `Vigilant` lúc restart |
| ADR-133 (v0.3.0) | Thêm Thread thứ 3 cho `Vision` — Window Message Pump (message-only window + `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + xử lý `WM_DISPLAYCHANGE`) | Win32 bắt buộc cần 1 thread bơm message để nhận các sự kiện này; đóng câu hỏi mở cũ (`WM_DISPLAYCHANGE`) + hiện thực hoá đúng nghĩa đen `PERF-020` "theo dõi qua WinEventHook, không polling"; không vi phạm ADR-38 (thread này không gọi DXGI/ONNX) |
| ADR-134 (v0.3.0) | Thêm field `content_changed` (field 8) vào `VisionInferenceResult`, amendment `03-ipc-communication.md` cùng lượt | Bắt buộc kỹ thuật: chỉ `Vision` thấy được frame để so sánh, nhưng theo `ROADMAP.md` mục 2 chỉ `Service` được quyết định đổi `capture_interval_ms` — tín hiệu phải qua IPC, không có cách nào khác |
| ADR-135 (v0.3.0) | Không thêm field/schema mới cho benchmark `PERF-060`–`063` — tái dùng `diagnostic_state` free-text (mở rộng quy ước không bắt buộc) + dữ liệu timestamp đã có sẵn (`VisionInferenceResult`/`audit.log`) + công cụ ngoài app (Performance Counter/Task Manager) đã được chính spec chỉ định | Toàn bộ dữ liệu cần thiết đã tồn tại từ thiết kế các Đợt trước; tránh thêm bề mặt/API chỉ phục vụ 1 nhu cầu vận hành thỉnh thoảng (Đợt 9) |

## 11. Câu hỏi mở

- [x] ~~(v0.2.0) Cơ chế cụ thể để `Vision` nhận `WM_DISPLAYCHANGE` (message-only window riêng, hay mượn 1 window handle helper đã có).~~ — **Đã xong v0.3.0** (mục 3.6: Window Message Pump, thread thứ 3, ADR-133 — cùng thread cũng xử lý `EVENT_SYSTEM_FOREGROUND` cho `PERF-020`).
- [ ] Công thức tổng hợp risk score (`ADR-47`) và phép normalize chính xác (mục 4.2) là **PROPOSED**, cần xác nhận bằng benchmark thực nghiệm trước khi khoá cứng — cùng phạm vi câu hỏi mở đã có sẵn ở `09-image-processing-spec.md` mục 8 (phương pháp/dataset benchmark chọn ngưỡng risk score mặc định `BE-090`), không phải câu hỏi mở mới.
- [ ] Cần bổ sung 2 dòng `event_type` (`VisionCaptureFallbackMediumIl`, và cân nhắc `ModelIntegrityCheckFailed`) vào bảng `event_type` ở `04-data-architecture.md` mục 5.1 ở lượt sửa file đó kế tiếp (mục 8.2 file này) — nối tiếp câu hỏi mở tương tự đã ghi ở `06-security-architecture.md` mục 7 cho `VisionNetworkBlocked`, không chặn tiến độ Đợt 1.
- [ ] Giá trị X phút cụ thể cho test compliance mục 9 (đề xuất khởi điểm 5 phút) và ngưỡng heuristic phát hiện chuỗi base64 khả nghi trong log — tinh chỉnh thực nghiệm lúc `test-runner`/`security-privacy-auditor` viết test thật, không chốt số tuyệt đối ở tài liệu kiến trúc (cùng cách xử lý số liệu benchmark khác đã áp dụng ở `08-performance-cpu-spec.md`).
- [ ] **(mới v0.3.0)** 4 hằng số `PERF-010`/`011` mới đều **PROPOSED**, cần benchmark đa cấu hình máy (`PERF-061`) trước khi khoá cứng — không chốt số tuyệt đối ở đây (mục 3.7.6): `T_UNCHANGED` (ngưỡng Hamming distance dHash coi là "không đổi"), `N_RELAX` (số frame liên tiếp trước khi nới lỏng), `BOOST_INTERVAL_MS`, `NEAR_THRESHOLD_MARGIN` (cùng phạm vi benchmark với `risk_threshold` mặc định `BE-090` đã có ở `09-image-processing-spec.md` mục 8 — 2 nhóm số nên đo cùng lúc vì phụ thuộc lẫn nhau).
- [ ] **(mới v0.3.0)** `04-data-architecture.md` mục 3.3/ADR-127 cần amendment kế tiếp để đồng bộ `capture_interval_baseline_ms` cho `"balanced"` từ 2000ms (giá trị tạm Đợt 6) sang **5000ms** (giá trị chính thức theo `PERF-010` v0.4.0 + thiết kế mục 3.7.4 file này) — không chặn tiến độ Đợt 7, `feature-dev` dùng 5000ms làm nguồn chính thức ngay từ bây giờ theo hướng dẫn tường minh ở mục 3.7.4 (không tự sửa `04` ở lượt này, theo đúng nguyên tắc mỗi lượt chỉ viết đúng file cần thiết — `03` được amend cùng lượt này vì là phụ thuộc kỹ thuật bắt buộc, `04` chỉ là 1 con số cần đồng bộ, không blocking).

## 12. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.3.0 | 2026-09-25 | **MINOR — Đợt 7 (`ROADMAP.md` mục 4, "Performance tuning chính thức")**: thiết kế đầy đủ Adaptive Frame Rate (`PERF-010`) + Perceptual Hashing (`PERF-011`/`IMG-011`), xác nhận `PERF-020`/`021` (window-aware capture). Thêm mục 3.6 (thread thứ 3 — Window Message Pump, `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + `WM_DISPLAYCHANGE`, ADR-133 — đóng câu hỏi mở cũ + sửa exclude-list từ polling sang `wakeEvent.Wait()` vô hạn đúng nghĩa đen `PERF-020`), mục 3.7 (state machine `Vigilant`/`Relaxed`/`Boost` phía `Service`, hysteresis bất đối xứng ADR-130, làm rõ quan hệ 2 tầng `performance_mode` (Đợt 6, trần nới lỏng tối đa) ↔ Adaptive Frame Rate (Đợt 7, động) ở ADR-131), mục 3.8 (dHash 64-bit viết tay ADR-128, dùng chung 1 tín hiệu `content_changed` cho cả `PERF-010`/`011` ADR-129, xác nhận ranh giới "state kỹ thuật" hợp lệ theo `IMG-011`/`02` mục 5 điểm 2, không phải "state nghiệp vụ" bị cấm). Mục 9a mới (benchmark hooks `PERF-060`–`063` — xác nhận không cần field/schema mới, tái dùng dữ liệu đã có + công cụ ngoài app). Cập nhật mục 2 (diagram bước `[3a]` hash-gate), mục 3.3 (pseudocode exclude-list + union `UserWhitelistedProcessNames`, sửa lỗi tham chiếu "mục 3.4" của v0.1.0), mục 3.5 (`IsCandidateWindow` bổ sung union), mục 4.2/4.4 (điểm chèn hash-gate, field `ContentChanged` mới), mục 6 (bảng zero-out mở rộng cho buffer `luma[9,8]`). 8 ADR mới (128-135). Amendment cùng lượt (phụ thuộc kỹ thuật bắt buộc, không phải mở rộng phạm vi): `03-ipc-communication.md` (PATCH, field `content_changed` = field 8 mới trong `VisionInferenceResult`, ADR-134), `02-process-architecture.md` (PATCH, đăng ký domain-state RAM-only mới `AdaptiveFrameRateState`/`WindowFrameRateState` ở mục 5, trỏ chi tiết sang file này). **2 câu hỏi mở không-blocking**: (1) 4 hằng số mới (`T_UNCHANGED`/`N_RELAX`/`BOOST_INTERVAL_MS`/`NEAR_THRESHOLD_MARGIN`) đều PROPOSED, cần benchmark `PERF-061` đa cấu hình máy trước khi khoá cứng — không tự chốt số tuyệt đối, đúng tinh thần đã áp dụng cho mọi ngưỡng hiệu năng khác trong dự án; (2) `04-data-architecture.md` mục 3.3/ADR-127 cần amendment kế tiếp đồng bộ `capture_interval_baseline_ms("balanced")` từ 2000ms (giá trị tạm Đợt 6) sang 5000ms (giá trị chính thức `PERF-010` v0.4.0 + mục 3.7.4 file này) — không chặn Đợt 7, hướng dẫn rõ dùng 5000ms ngay. Theo chỉ đạo — không dừng chờ review giữa chừng, viết xong đủ nội dung (05 + 2 amendment phụ thuộc) mới báo cáo lại |
| v0.2.2 | 2026-09-24 | PATCH — amendment cùng lượt cập nhật `10-ui-architecture.md` v0.2.0 sau khi `spec-maintainer` chốt `PERF-050b` (`Specification/08-performance-cpu-spec.md` v0.7.0). Thêm 1 đoạn liên kết ngắn ở mục 3.3: "Chế độ hiệu năng" (`S4`) cắm vào đúng cơ chế `wakeEvent`/`ControlVisionCommand` đã có sẵn từ v0.1.0 — không đổi code `CaptureLoopWorker`/`VisionRuntimeConfig`, con số `capture_interval_ms` cụ thể theo mode để ở `04-data-architecture.md` mục 3.3/ADR-127 (không lặp lại ở đây). Không đổi luồng/threading nào khác |
| v0.2.1 | 2026-09-20 | PATCH — Đợt 6 (`ROADMAP.md`, Dashboard UI), amendment cùng lượt viết `10-ui-architecture.md`. Mục 4.4: `VisionInferenceResult` thêm field `ProcessName`, gán trực tiếp từ giá trị `ForegroundWindowTracker` đã resolve sẵn ở bước 1 (`MISC-030`, `03-ipc-communication.md` ADR-111) — không resolve thêm lần nào khác, không đổi luồng xử lý nào khác của pipeline. Theo chỉ đạo — không dừng chờ review |
| v0.2.0 | 2026-09-19 | MINOR — Đợt 2 (`ROADMAP.md`): thêm mục 3.5 multi-monitor/đa cửa sổ phía `Vision` (`BE-080`–`082`, `IMG-020`) — enumerate `IDXGIOutput`, `monitor_id` cầu nối qua `HMONITOR` (ADR-63), thuật toán chọn danh sách cửa sổ candidate mỗi chu kỳ (foreground trước, tối đa 1 cửa sổ/màn hình còn lại theo Z-order, ADR-64), capture lazy theo output có nhu cầu + dispose sau 5 chu kỳ rảnh (ADR-65). Với đúng 1 màn hình, hành vi giữ nguyên y hệt v0.1.0 (nhánh multi-monitor bị bỏ qua). Phần Overlay/Service tiêu thụ `monitor_id`/danh sách nhiều cửa sổ để ở `07-overlay-architecture.md` (file mới), không lặp lại ở đây. 3 ADR mới (63-65), 1 câu hỏi mở nhỏ (cơ chế nhận `WM_DISPLAYCHANGE`, thuần implement). Viết cùng lượt với `07-overlay-architecture.md`/`03-ipc-communication.md` v0.3.0, theo chỉ đạo chủ dự án — không dừng chờ review từng file |
| v0.1.0 | 2026-09-17 | Khởi tạo — pipeline 7 bước chi tiết hoá (gộp buffer crop+readback vào 1 staging texture, ADR-41), threading model 2 luồng độc lập (IPC async vs Capture-Inference dedicated Thread, ADR-38/39) trả lời yêu cầu heartbeat không bị chặn, trả lời câu hỏi mở Pause/suspend `Vision` bằng cách tái dùng `MonitoringEnabled` (ADR-40, đóng câu hỏi mở `02-process-architecture.md` mục 8), ONNX Runtime session reuse + DirectML/CPU fallback qua `HeartbeatAck.DiagnosticState` (ADR-44/45), bảng zero-out buffer đầy đủ theo từng bước với `try/finally` cục bộ (đóng vai trò cross-cutting principle "không lưu trữ ảnh" ở `Architecture/01` mục 5), `MISC-090` checksum verify tự-đủ không qua IPC (ADR-46), công thức risk score PROPOSED (ADR-47, chờ benchmark), chiến lược fallback Low IL → Medium IL qua exit code riêng không cần runtime-detection phức tạp (ADR-49, đóng câu hỏi mở `06-security-architecture.md` mục 7 dòng 1), compliance check 3 lớp cho `IMG-040`/`041`, 14 ADR (38-51), 3 câu hỏi mở nhỏ (công thức risk score chờ benchmark — gộp vào câu hỏi mở sẵn có ở `09`, bổ sung `event_type` vào `04` ở lượt kế tiếp, số liệu compliance test tinh chỉnh thực nghiệm) |
