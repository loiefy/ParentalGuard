# ParentalGuard.Vision

Đợt 0 (`ROADMAP.md`): khung xương console app — nhận bootstrap (pipe handle kế thừa) từ `Service`
qua tham số dòng lệnh, đọc khoá HMAC + tên pipe (`ParentalGuard.Ipc.Client.ChildIpcBootstrap`),
connect vào `ParentalGuard.Svc.Vision` (retry backoff 200ms→3.2s, `Architecture/03` mục 4.1),
handshake `Hello`/`HelloAck`, giữ heartbeat sống (`ParentalGuard.Ipc.Client.IpcChildClient`,
dùng chung với `ParentalGuard.Overlay`).

Process này được `Service` spawn qua `CreateProcessAsUser` với Restricted Token + Low Integrity
Level vào đúng session tương tác (`BE-023a`, `Architecture/06` mục 2.1) — không tự khởi động
độc lập, không có entry Task Scheduler/Registry Run.

## Đợt 1 (`ROADMAP.md`, `Architecture/05-image-pipeline-architecture.md`)

Pipeline 7 bước đầy đủ: `Capture` (DXGI Desktop Duplication, `Capture/DesktopDuplicationCapture.cs`)
→ `Crop GPU-side` (`Capture/GpuWindowCropper.cs`, `IMG-012`) → `Resize/Normalize` (`Capture/FrameResizerNormalizer.cs`,
MobileNetV2 `[-1,1]`) → `Inference` ONNX Runtime DirectML→CPU fallback (`Inference/NsfwClassifier.cs`,
`PERF-030/031`) → `Zero-out buffer` ngay sau mỗi bước dùng xong (`IMG-003`, xem bảng
Architecture/05 mục 6) → `Tổng hợp risk score` (`Inference/RiskScoreAggregator.cs`, ADR-47) →
gửi `VisionInferenceResult` qua `IpcChildClient.EnqueueOutbound` (`BE-021`). Điều phối bởi
`Pipeline/CaptureLoopWorker.cs` trên 1 `Thread` chuyên dụng (ADR-38), foreground-window +
exclude-list ở `Capture/ForegroundWindowTracker.cs`/`Pipeline/ExcludeProcessMatcher.cs` (`BE-071`/`BE-073a`).

Model `.onnx` đọc từ đường dẫn cấu hình được (`Configuration/ModelPaths.cs`, mặc định
`<cài đặt>\models\nsfw_model.onnx`, override qua biến môi trường `PARENTALGUARD_MODEL_DIR` cho
dev/test cục bộ) — **chưa đóng gói sẵn file thật trong repo**, phải tự chuẩn bị trước khi chạy
runtime thật (convert trọng số `GantMan/nsfw_model` sang ONNX, `IMG-014`). Checksum verify
(`ModelIntegrity/OnnxChecksumVerifier.cs`, `MISC-090`) đã viết sẵn nhưng **chưa được bật** ở Đợt 1
(thuộc phạm vi Đợt 8 theo `ROADMAP.md`).

Fallback Low→Medium Integrity Level cho Desktop Duplication API (exit code 17) và checksum model
(exit code 18, chưa dùng) theo bảng `Architecture/05` mục 8.2 — xem `Pipeline/VisionExitCodes.cs`.

**Chỉ verify được ở mức build + unit test logic thuần trong môi trường dev/CI không có GPU/session
tương tác thật** (`tests/ParentalGuard.Vision.Tests`) — DXGI capture thật, ONNX inference thật với
model thật, và compliance check 3 lớp `IMG-040`/`IMG-041` (Architecture/05 mục 9) cần chủ dự án tự
chạy trên máy Windows thật có GPU.
