# 05 — Image Processing Pipeline Architecture

> Version: v0.2.0 | Trạng thái: Approved | Cập nhật: 2026-09-19

## 1. Mục đích và phạm vi

File này trả lời **HOW** cho toàn bộ pipeline xử lý ảnh mô tả ở `Specification/09-image-processing-spec.md` (capture → crop → resize → inference → decision), threading model của `ParentalGuard.Vision`, và 2 câu hỏi mở đang treo từ các file trước:

- `02-process-architecture.md` mục 8: cơ chế suspend `Vision` lúc Pause.
- `06-security-architecture.md` mục 7 (dòng 1): validate/thiết kế fallback Low Integrity Level ↔ Medium IL cho Desktop Duplication API.

Phạm vi ban đầu (v0.1.0) đúng khung Đợt 1 theo `ROADMAP.md` mục 4 — **1 cửa sổ, 1 màn hình, happy path đầy đủ end-to-end**. **Cập nhật v0.2.0 (Đợt 2)**: mục 3.5 bổ sung multi-monitor/đa cửa sổ phía `Vision` (`BE-080`–`082`, `IMG-020`) — phần Overlay/Service tiêu thụ kết quả này (`BE-083`–`089`, `FE-016`) thiết kế ở `07-overlay-architecture.md`, không lặp ở đây. Adaptive Frame Rate đầy đủ (`PERF-010`+) vẫn là phạm vi Đợt 7, chưa đổi ở amendment này.

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
[3] Readback + Resize/Normalize (CPU)            IMG-010, PERF-032
       │ Map() staging texture → byte[] BGRA8 → bilinear resize + normalize
       │ trực tiếp vào DenseTensor<float> [1,224,224,3] (hoặc NCHW — đọc từ
       │ session.InputMetadata, ADR-48)
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
    if processName in config.ExcludeProcessNames:    // BE-073/073a, PERF-020
        wakeEvent.Wait(config.CaptureIntervalMs)     // vẫn chờ đúng interval hiện tại (Đợt 1: chưa dùng
        continue                                      // WinEventHook để tắt hẳn — xem mục 3.4)
    result = FrameClassificationPipeline.Process(hwnd, monitorSelector)  // mục 2, có thể null nếu
    if result is not null:                                                // AcquireNextFrame timeout/lỗi tạm thời
        ipcClient.EnqueueOutbound(result)
    wakeEvent.Wait(config.CaptureIntervalMs)
```

- `wakeEvent` (`ManualResetEventSlim` hoặc `AutoResetEvent`) được `Set()` bởi Reader loop mỗi khi nhận `ControlVisionCommand` mới — đảm bảo thay đổi `capture_interval_ms`/`monitoring_enabled` có hiệu lực **ngay lập tức**, không phải chờ hết interval cũ mới áp dụng. Đây chính là điểm mở để Đợt 7 (Adaptive Frame Rate) cắm vào: Service chỉ cần gửi `ControlVisionCommand` mới thường xuyên hơn với `capture_interval_ms` khác nhau tuỳ ngữ cảnh — `CaptureLoopWorker` không cần sửa 1 dòng nào.
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

`IsCandidateWindow(w)`: `IsWindowVisible(w) && !IsIconic(w) && GetWindowTextLength(w) > 0` (loại cửa sổ ẩn/minimized/không có tiêu đề — heuristic loại các message-only/helper window không phải cửa sổ ứng dụng thật) `&& ResolveProcessName(w) not in config.ExcludeProcessNames` (tái dùng đúng danh sách `BE-073a` đã áp dụng cho `fgHwnd` ở Đợt 1).

Phần Overlay/Service tiêu thụ `monitor_id` này (nhóm theo màn hình cho chế độ gộp `BE-088`/`BE-089`, icon trạng thái mỗi màn hình `BE-083`) — thiết kế đầy đủ ở `07-overlay-architecture.md` mục 3/4, không lặp lại ở đây.

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
- **`FrameResizerNormalizer`**: bilinear resize thủ công (không qua `SixLabors.ImageSharp`/`System.Drawing` — ADR-51, giảm dependency và giảm 1 buffer object trung gian) đọc trực tiếp từ `byte[]` BGRA8 nguồn, ghi trực tiếp vào `DenseTensor<float>` đích kích thước cố định theo input model (`224×224×3` — `IMG-014`/`PERF-032`), kết hợp luôn normalize (rescale + trừ mean/chia std hoặc `[-1,1]` tuỳ đúng phép `preprocess_input` của model gốc — **xác nhận công thức chính xác khi implement, đối chiếu với bước tiền xử lý gốc của `GantMan/nsfw_model`**, vì đây là chi tiết ảnh hưởng độ chính xác, cần review cùng lúc benchmark ngưỡng risk score đã ghi ở câu hỏi mở `09-image-processing-spec.md` mục 8).
- `DenseTensor<float>` input **cấp phát đúng 1 lần lúc Vision khởi động, tái dùng suốt vòng đời process** (kích thước luôn cố định `224×224×3`, không phụ thuộc kích thước cửa sổ — khác `byte[]`/staging texture phải resize-recreate khi cửa sổ đổi kích thước).

### 4.3 Bước 4+5 — Inference + tổng hợp risk score

- **`NsfwClassifier`** (namespace `ParentalGuard.Vision.Inference`): sở hữu 1 `InferenceSession` **khởi tạo eager, đúng 1 lần** ngay sau khi Vision hoàn tất handshake IPC đầu tiên (không đợi `ControlVisionCommand` đầu tiên bật monitoring — model cần sẵn sàng trước để không phát sinh độ trễ ở lần capture đầu tiên khi resume/enable), **tái dùng cho mọi frame** tới khi process thoát (ADR-44). Tạo `InferenceSession` mới mỗi frame **không được chọn** — chi phí load model + compile execution provider mỗi lần là quá lớn so với ngân sách 1 frame.
- **Xác định tên input + layout tensor động (ADR-48)**: đọc `session.InputMetadata` lúc khởi tạo để lấy đúng tên input node và thứ tự trục (NHWC mặc định của Keras/TensorFlow hay NCHW nếu bước convert `tf2onnx` có transpose) — không hard-code 1 layout cụ thể, vì phụ thuộc vào cách file `.onnx` thực tế được convert (công đoạn chuẩn bị asset, ngoài phạm vi code Vision).
- `session.Run(...)` → output 5 xác suất lớp (`drawing`/`hentai`/`neutral`/`porn`/`sexy`, `IMG-014`).
- **`RiskScoreAggregator`** (ADR-47, **PROPOSED** — chờ xác nhận benchmark, liên kết câu hỏi mở đã có ở `09-image-processing-spec.md` mục 8): `risk_score = P(hentai) + P(porn) + P(sexy)` (bỏ `drawing` — tranh vẽ không nhạy cảm thực sự, `neutral` — nội dung an toàn), clamp `[0.0, 1.0]` (đề phòng sai số dấu phẩy động khi tổng 3 lớp vượt nhẹ 1.0). Đây là công thức phổ biến trong cộng đồng dùng `GantMan/nsfw_model`, được chọn vì đơn giản/minh bạch, **không** phải quyết định cuối cùng — sẽ được xác nhận/hiệu chỉnh cùng lúc benchmark chọn ngưỡng mặc định (`BE-090`), đúng đúng tinh thần "chi tiết công thức để ở System Design" mà `IMG-014` đã uỷ quyền.

### 4.4 Bước 7 — Gửi kết quả

- **`VisionInferenceResult`** (đã chốt schema ở `03-ipc-communication.md` mục 3.3, không định nghĩa lại): `FrameId` (bộ đếm `Interlocked.Increment`, reset mỗi lần Vision khởi động lại — chỉ phục vụ chẩn đoán, không có ý nghĩa bảo mật), `WindowHandle` (cast HWND → `fixed64`), `MonitorId` (chỉ số output DXGI theo thứ tự `EnumOutputs`, ổn định trong 1 phiên chạy — lược đồ định danh bền vững hơn để ở Đợt 2), `RiskScore` (kết quả mục 4.3), `Bbox` (chính là `cropRectInMonitorSpace` đã dùng ở bước 2, quy đổi lại toạ độ màn hình tuyệt đối — **luôn điền**, không bỏ trống theo điều kiện ngưỡng, đơn giản hoá vì chi phí gần như 0 và vẫn hợp lệ với ngữ nghĩa "có thể rỗng" ở `03` là 1 khả năng được phép chứ không bắt buộc), `CapturedAtUnixMs` (timestamp lấy ở đầu bước 1).
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
| `byte[]` pixel BGRA8 thô (sau readback, trước resize) | Vision, tái dùng nếu cùng kích thước | Trong `finally` ngay sau `FrameResizerNormalizer` đọc xong | `Array.Clear(buffer, 0, buffer.Length)` |
| `DenseTensor<float>` input model (224×224×3, cố định) | Vision, tái dùng suốt vòng đời process | Trong `finally` ngay sau `session.Run(...)` trả về | `tensor.Buffer.Span.Clear()` |
| Output tensor (5 xác suất lớp) | Vision, cấp phát mới mỗi frame (kích thước 5 float — không đáng để pool) | Ngay sau khi `RiskScoreAggregator` đọc xong | `Array.Clear`/`Span.Clear()` trước khi biến ra khỏi scope |
| `VisionInferenceResult` (Protobuf message đã gửi) | Vision, tạm thời | Không chứa dữ liệu pixel — chỉ 6 field số/toạ độ (`03` mục 3.3) — không thuộc phạm vi `IMG-003` (không phải "dữ liệu ảnh"), không cần zero riêng |

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

## 11. Câu hỏi mở

- [ ] (v0.2.0) Cơ chế cụ thể để `Vision` nhận `WM_DISPLAYCHANGE` (message-only window riêng, hay mượn 1 window handle helper đã có) — chi tiết implement thuần tuý, không phải quyết định kiến trúc, để `feature-dev` tự chọn lúc code mục 3.5.
- [ ] Công thức tổng hợp risk score (`ADR-47`) và phép normalize chính xác (mục 4.2) là **PROPOSED**, cần xác nhận bằng benchmark thực nghiệm trước khi khoá cứng — cùng phạm vi câu hỏi mở đã có sẵn ở `09-image-processing-spec.md` mục 8 (phương pháp/dataset benchmark chọn ngưỡng risk score mặc định `BE-090`), không phải câu hỏi mở mới.
- [ ] Cần bổ sung 2 dòng `event_type` (`VisionCaptureFallbackMediumIl`, và cân nhắc `ModelIntegrityCheckFailed`) vào bảng `event_type` ở `04-data-architecture.md` mục 5.1 ở lượt sửa file đó kế tiếp (mục 8.2 file này) — nối tiếp câu hỏi mở tương tự đã ghi ở `06-security-architecture.md` mục 7 cho `VisionNetworkBlocked`, không chặn tiến độ Đợt 1.
- [ ] Giá trị X phút cụ thể cho test compliance mục 9 (đề xuất khởi điểm 5 phút) và ngưỡng heuristic phát hiện chuỗi base64 khả nghi trong log — tinh chỉnh thực nghiệm lúc `test-runner`/`security-privacy-auditor` viết test thật, không chốt số tuyệt đối ở tài liệu kiến trúc (cùng cách xử lý số liệu benchmark khác đã áp dụng ở `08-performance-cpu-spec.md`).

## 12. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.0 | 2026-09-19 | MINOR — Đợt 2 (`ROADMAP.md`): thêm mục 3.5 multi-monitor/đa cửa sổ phía `Vision` (`BE-080`–`082`, `IMG-020`) — enumerate `IDXGIOutput`, `monitor_id` cầu nối qua `HMONITOR` (ADR-63), thuật toán chọn danh sách cửa sổ candidate mỗi chu kỳ (foreground trước, tối đa 1 cửa sổ/màn hình còn lại theo Z-order, ADR-64), capture lazy theo output có nhu cầu + dispose sau 5 chu kỳ rảnh (ADR-65). Với đúng 1 màn hình, hành vi giữ nguyên y hệt v0.1.0 (nhánh multi-monitor bị bỏ qua). Phần Overlay/Service tiêu thụ `monitor_id`/danh sách nhiều cửa sổ để ở `07-overlay-architecture.md` (file mới), không lặp lại ở đây. 3 ADR mới (63-65), 1 câu hỏi mở nhỏ (cơ chế nhận `WM_DISPLAYCHANGE`, thuần implement). Viết cùng lượt với `07-overlay-architecture.md`/`03-ipc-communication.md` v0.3.0, theo chỉ đạo chủ dự án — không dừng chờ review từng file |
| v0.1.0 | 2026-09-17 | Khởi tạo — pipeline 7 bước chi tiết hoá (gộp buffer crop+readback vào 1 staging texture, ADR-41), threading model 2 luồng độc lập (IPC async vs Capture-Inference dedicated Thread, ADR-38/39) trả lời yêu cầu heartbeat không bị chặn, trả lời câu hỏi mở Pause/suspend `Vision` bằng cách tái dùng `MonitoringEnabled` (ADR-40, đóng câu hỏi mở `02-process-architecture.md` mục 8), ONNX Runtime session reuse + DirectML/CPU fallback qua `HeartbeatAck.DiagnosticState` (ADR-44/45), bảng zero-out buffer đầy đủ theo từng bước với `try/finally` cục bộ (đóng vai trò cross-cutting principle "không lưu trữ ảnh" ở `Architecture/01` mục 5), `MISC-090` checksum verify tự-đủ không qua IPC (ADR-46), công thức risk score PROPOSED (ADR-47, chờ benchmark), chiến lược fallback Low IL → Medium IL qua exit code riêng không cần runtime-detection phức tạp (ADR-49, đóng câu hỏi mở `06-security-architecture.md` mục 7 dòng 1), compliance check 3 lớp cho `IMG-040`/`041`, 14 ADR (38-51), 3 câu hỏi mở nhỏ (công thức risk score chờ benchmark — gộp vào câu hỏi mở sẵn có ở `09`, bổ sung `event_type` vào `04` ở lượt kế tiếp, số liệu compliance test tinh chỉnh thực nghiệm) |
