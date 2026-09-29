# Dependency Map (tạm thời)

> **Trạng thái**: TẠM THỜI — định dạng/vị trí chính thức của Dependency Map (`DEV-042`) chưa được
> chốt trong `Architecture/10-dev-automation-architecture.md` (file đó "Chưa viết", xem
> `Architecture/00-INDEX.md` mục 2). File này đáp ứng yêu cầu tối thiểu của `DEV-042`/`DEV-043`
> (hàm ↔ file ↔ caller/callee) cho phạm vi code đã viết tới nay (Đợt 0 — `ROADMAP.md`). Khi
> `architecture-writer` chốt định dạng chính thức ở `10-dev-automation-architecture.md`, file này
> sẽ được di chuyển/chuyển đổi format theo quyết định đó.

Quy ước: "Callers" = hàm nào gọi hàm này; "Callees" = hàm này gọi hàm nào (chỉ liệt kê lệnh gọi nội
bộ codebase có ý nghĩa cho impact analysis — không liệt kê từng lệnh gọi BCL cơ bản như
`File.Exists`).

## `src/ParentalGuard.Ipc/` (thư viện dùng chung — Service + Vision + Overlay)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `IpcFrameTransport.WriteFrameAsync` | `Framing/IpcFrameTransport.cs` | `ChildProcessSupervisor.*` (Service), `IpcChildClient.*` (Vision/Overlay) | `ComputeHmac` |
| `IpcFrameTransport.ReadFrameAsync` | `Framing/IpcFrameTransport.cs` | `ChildProcessSupervisor.*`, `IpcChildClient.*` | `ReadExactAsync`, `ComputeHmac` |
| `IpcFrameTransport.ComputeHmac` (private) | `Framing/IpcFrameTransport.cs` | `WriteFrameAsync`, `ReadFrameAsync` | — |
| `IpcFrameTransport.ReadExactAsync` (private) | `Framing/IpcFrameTransport.cs` | `ReadFrameAsync` | — |
| `IpcEnvelope.NewEnvelope` | `Framing/IpcEnvelope.cs` | `ChildProcessSupervisor.*`, `IpcChildClient.*`, `OverlayDecisionCoordinator.*` (Service) | — |
| `IpcMessageIdGenerator.Next` | `Framing/IpcEnvelope.cs` | `ChildProcessSupervisor.*`, `IpcChildClient.*` | — |
| `ChildIpcBootstrap.WriteTo` | `Client/ChildIpcBootstrap.cs` | `ChildProcessLauncher.LaunchWithToken` (Service) | — |
| `ChildIpcBootstrap.ReadFromInheritedStdHandle` | `Client/ChildIpcBootstrap.cs` | `Vision/Program.cs`, `Overlay/Program.cs` | `GetStdHandle` (P/Invoke `kernel32`) |
| `IpcChildClient.RunForeverAsync` | `Client/IpcChildClient.cs` | `Vision/Program.cs`, `Overlay/Program.cs` | `ConnectWithRetryAsync`, `HandshakeAsync`, `RunConnectionAsync` |
| `IpcChildClient.ConnectWithRetryAsync` (private) | `Client/IpcChildClient.cs` | `RunForeverAsync` | `NamedPipeClientStream.ConnectAsync` (BCL) |
| `IpcChildClient.HandshakeAsync` (private) | `Client/IpcChildClient.cs` | `RunForeverAsync` | `IpcFrameTransport.WriteFrameAsync/ReadFrameAsync`, `NewEnvelope` |
| `IpcChildClient.RunConnectionAsync` (private, **mới Đợt 1, ADR-39**) | `Client/IpcChildClient.cs` | `RunForeverAsync` | `ReaderLoopAsync`, `WriterLoopAsync` (song song, `Task.WhenAny`) |
| `IpcChildClient.ReaderLoopAsync` (private, **mới Đợt 1**) | `Client/IpcChildClient.cs` | `RunConnectionAsync` | `IpcFrameTransport.ReadFrameAsync`, `EnqueueHeartbeatAck`, `onBusinessMessage` (callback từ Vision/Overlay Program.cs) |
| `IpcChildClient.WriterLoopAsync` (private, **mới Đợt 1**) | `Client/IpcChildClient.cs` | `RunConnectionAsync` | `IpcFrameTransport.WriteFrameAsync` (đọc từ `_outbound` Channel) |
| `IpcChildClient.EnqueueHeartbeatAck` (private, **mới Đợt 1**, đổi tên từ `RespondHeartbeatAsync`) | `Client/IpcChildClient.cs` | `ReaderLoopAsync` | `NewEnvelope`, `EnqueueOutbound` |
| `IpcChildClient.EnqueueOutbound` (public, **mới Đợt 1**) | `Client/IpcChildClient.cs` | `EnqueueHeartbeatAck`, `Vision/Pipeline/CaptureLoopWorker.ProcessOneFrame`, `Overlay/Program.SendForceClose` | `Channel<IpcPayload>.Writer.TryWrite` (BCL) |
| `IpcChildClient.NewEnvelope` (public, **mới Đợt 1**) | `Client/IpcChildClient.cs` | `HandshakeAsync`, `EnqueueHeartbeatAck`, `Vision/Pipeline/CaptureLoopWorker.ProcessOneFrame`, `Overlay/Program.SendForceClose` | `IpcEnvelope.NewEnvelope` |
| `IpcChildClient.DiagnosticState` (public field, **mới Đợt 1**) | `Client/IpcChildClient.cs` | set bởi `Vision/Program.cs` (ADR-45 EP fallback), đọc bởi `EnqueueHeartbeatAck` | — |
| `UiIpcClient.ConnectAsync` (**mới Đợt 6**, ADR-118/119) | `Client/UiIpcClient.cs` | `ParentalGuard.UI/App.ConnectAndRouteAsync` | `DisconnectAsync`, `NamedPipeClientStream.ConnectAsync` (BCL), `HandshakeAsync` |
| `UiIpcClient.HandshakeAsync` (private, **mới Đợt 6**, ADR-82) | `Client/UiIpcClient.cs` | `ConnectAsync` | `IpcFrameTransport.WriteFrameAsync/ReadFrameAsync` (khoá hằng số 32-byte-zero), `NewEnvelope` |
| `UiIpcClient.SendRequestAsync<TResp>` (**mới Đợt 6**, ADR-119 — `SemaphoreSlim(1)`, không Reader/Writer loop song song khác `IpcChildClient`) | `Client/UiIpcClient.cs` | `ParentalGuard.UI/Services/IpcClient/AuthFacade.*`, `DashboardFacade.*`/`PauseFacade.*` (**mới, giai đoạn 2**) | `IpcFrameTransport.WriteFrameAsync/ReadFrameAsync` (session_key), `DisconnectCoreAsync` (khi lỗi pipe/framing) |
| `UiIpcClient.NewEnvelope` (**mới Đợt 6**) | `Client/UiIpcClient.cs` | `HandshakeAsync`, `AuthFacade.*` | `IpcEnvelope.NewEnvelope` |
| `UiIpcClient.DisconnectAsync`/`.DisconnectCoreAsync` (private) (**mới Đợt 6**) | `Client/UiIpcClient.cs` | `ConnectAsync` (dọn phiên cũ), `SendRequestAsync` (khi lỗi), `ParentalGuard.UI/App.OnWindowClosed` | `NamedPipeClientStream.DisposeAsync` (BCL) |

## `src/ParentalGuard.Vision/` (Đợt 1 — pipeline 7 bước, Architecture/05)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| top-level `Program` | `Program.cs` | entry point (OS) | `ChildIpcBootstrap.ReadFromInheritedStdHandle`, `OrtEnv.Instance().DisableTelemetryEvents` (IMG-015), `NsfwClassifier` ctor, `DesktopDuplicationCapture` ctor + `AcquireNextFrame` (probe IL, ADR-49), `GpuWindowCropper` ctor, `FrameClassificationPipeline` ctor, `CaptureLoopWorker.Start`, `IpcChildClient.RunForeverAsync` |
| `Program.HandleBusinessMessageAsync` (top-level local, static) | `Program.cs` | `IpcChildClient.RunForeverAsync` (callback `onBusinessMessage`) | `VisionRuntimeConfigHolder.Update`, `CaptureLoopWorker.WakeUp` — chỉ xử lý `ControlVisionCommand` |
| `ForegroundWindowTracker.GetForegroundWindowHandle` | `Capture/ForegroundWindowTracker.cs` | `CaptureLoopWorker.Run` | `GetForegroundWindow` (P/Invoke `user32`) |
| `ForegroundWindowTracker.ResolveProcessName` | `Capture/ForegroundWindowTracker.cs` | `CaptureLoopWorker.Run` | `GetWindowThreadProcessId`/`OpenProcess`/`QueryFullProcessImageNameW`/`CloseHandle` (P/Invoke) |
| `WindowRectResolver.Resolve` | `Capture/WindowRectResolver.cs` | `FrameClassificationPipeline.Process` | `DwmGetWindowAttribute` (P/Invoke `dwmapi`) |
| `MonitorSelector.SelectOutputForWindow` | `Capture/MonitorSelector.cs` | `CaptureLoopWorker.Run` | `MonitorFromWindow` (P/Invoke `user32`), `IDXGIFactory1.EnumAdapters1`/`IDXGIAdapter1.EnumOutputs` (Vortice.DXGI) |
| `DesktopDuplicationCapture` ctor | `Capture/DesktopDuplicationCapture.cs` | `Program.cs` | `D3D11.D3D11CreateDevice` (Vortice.Direct3D11) |
| `DesktopDuplicationCapture.AcquireNextFrame` (trả `IDisposable?`, **sửa 2026-09-18** — trước là `ID3D11Texture2D?`) | `Capture/DesktopDuplicationCapture.cs` | `Program.cs` (probe 1 lần lúc khởi động), `FrameClassificationPipeline.Process` (mỗi frame, qua `IFrameCapture`) | `EnsureDuplication`, `IDXGIOutputDuplication.AcquireNextFrame` |
| `DesktopDuplicationCapture.ReleaseFrame` | `Capture/DesktopDuplicationCapture.cs` | `FrameClassificationPipeline.ProcessFrame` (ADR-42, ngay sau crop, qua `IFrameCapture`) | `IDXGIOutputDuplication.ReleaseFrame` |
| `DesktopDuplicationCapture.EnsureDuplication` (private) | `Capture/DesktopDuplicationCapture.cs` | `AcquireNextFrame` | `IDXGIFactory1.EnumAdapters1`, `IDXGIAdapter1.EnumOutputs`, `IDXGIOutput1.DuplicateOutput` — ném `CaptureInitializationException` (ADR-49, exit code 17) |
| `IFrameCapture` (interface, **mới 2026-09-18**) | `Capture/IFrameCapture.cs` | `FrameClassificationPipeline` (ctor/field, thay `DesktopDuplicationCapture` cụ thể) | `DesktopDuplicationCapture` implement — seam test-only, `AcquireNextFrame` trả `IDisposable?` (không phải `ID3D11Texture2D?`) để test không cần phụ thuộc `Vortice.Direct3D11` |
| `IWindowCropper` (interface, **mới 2026-09-18**) | `Capture/IWindowCropper.cs` | `FrameClassificationPipeline` (ctor/field, thay `GpuWindowCropper` cụ thể) | `GpuWindowCropper` implement — seam test-only, `CropAndReadBack` nhận `IDisposable fullScreenFrame` (không phải `ID3D11Texture2D`) |
| `GpuWindowCropper.CropAndReadBack` (tham số `IDisposable`, **sửa 2026-09-18**) | `Capture/GpuWindowCropper.cs` | `FrameClassificationPipeline.ProcessFrame` (qua `IWindowCropper`) | ép kiểu `(ID3D11Texture2D)fullScreenFrame`, `EnsureStagingTexture`, `ID3D11DeviceContext.CopySubresourceRegion/Map/Unmap` — zero-out con trỏ Map trước Unmap (IMG-003 bảng mục 6) |
| `GpuWindowCropper.EnsureStagingTexture` (private) | `Capture/GpuWindowCropper.cs` | `CropAndReadBack` | `ZeroOutBeforeDispose` (khi đổi kích thước, ADR-43), `ID3D11Device.CreateTexture2D` |
| `FrameResizerNormalizer.Resize` (pure, unit test được) | `Capture/FrameResizerNormalizer.cs` | `FrameClassificationPipeline.ProcessFrame` | — (bilinear resize + normalize MobileNetV2 `[-1,1]`, ADR-51, ghi thẳng vào `DenseTensor<float>` theo `TensorLayout`) |
| `ExcludeProcessMatcher.IsExcluded` (pure, unit test được) | `Pipeline/ExcludeProcessMatcher.cs` | `CaptureLoopWorker.Run` | — |
| `NsfwClassifier` ctor | `Inference/NsfwClassifier.cs` | `Program.cs` | `InferenceSession` ctor (ONNX Runtime), `DetermineLayout` (ADR-48) |
| `NsfwClassifier.Classify` | `Inference/NsfwClassifier.cs` | `FrameClassificationPipeline.ProcessFrame` (qua `INsfwClassifier`) | `InferenceSession.Run` — zero-out output tensor ngay sau đọc (IMG-003) |
| `RiskScoreAggregator.Aggregate` (pure, unit test được) | `Inference/RiskScoreAggregator.cs` | `FrameClassificationPipeline.ProcessFrame` | — (ADR-47, PROPOSED chờ benchmark) |
| `OnnxChecksumVerifier.Verify` (pure, unit test được, **chưa dùng ở Đợt 1** — MISC-090 là Đợt 8) | `ModelIntegrity/OnnxChecksumVerifier.cs` | (chưa có caller production — sẵn sàng cho Đợt 8) | — |
| `VisionRuntimeConfigHolder.Update`/`.Current` | `Pipeline/VisionRuntimeConfig.cs` | `Program.HandleBusinessMessageAsync` (Update), `CaptureLoopWorker.Run` (Current), `Program.cs` (`onDisconnected` reset về default) | — |
| `FrameClassificationPipeline.Process` | `Pipeline/FrameClassificationPipeline.cs` | `CaptureLoopWorker.ProcessOneFrame` | `WindowRectResolver.Resolve`, `IFrameCapture.AcquireNextFrame`, `ProcessFrame` |
| `FrameClassificationPipeline.ProcessFrame` (internal, **mới 2026-09-18** — tách khỏi `Process` để test được không cần DWM/DXGI thật) | `Pipeline/FrameClassificationPipeline.cs` | `Process`, `tests/FrameClassificationPipelineZeroOutTests` (qua `InternalsVisibleTo`, `AssemblyInfo.cs`) | `EnsurePixelBuffer`, `IWindowCropper.CropAndReadBack`, `IFrameCapture.ReleaseFrame`, `IDisposable.Dispose` (frame), `FrameResizerNormalizer.Resize`, `INsfwClassifier.Classify`, `RiskScoreAggregator.Aggregate` — **đúng 1 try/finally** bao trọn thân, zero `_pixelBuffer`+`_inputTensor` vô điều kiện (bug 2026-09-18, xem "Bug đã sửa") |
| `FrameClassificationPipeline.EnsurePixelBuffer` (private, **sửa 2026-09-18**) | `Pipeline/FrameClassificationPipeline.cs` | `ProcessFrame` | `Array.Clear` mảng cũ trước khi thay (không để "mồ côi" chưa zero), `IFrameBufferAuditor.OnBufferAllocated` |
| `CaptureLoopWorker.Start`/`.Run`/`.ProcessOneFrame`/`.WakeUp` | `Pipeline/CaptureLoopWorker.cs` | `Program.cs` (Start/WakeUp), Thread riêng `IsBackground=true` (Run, ADR-38) | `ForegroundWindowTracker.*`, `ExcludeProcessMatcher.IsExcluded`, `MonitorSelector.SelectOutputForWindow`, `FrameClassificationPipeline.Process` (nay bọc try/catch, **sửa 2026-09-18** — xem "Bug đã sửa"), `IpcChildClient.NewEnvelope/EnqueueOutbound/DiagnosticState`, `Environment.Exit` (`VisionExitCodes.PipelineRepeatedFailure`, sau `_maxConsecutiveFailures=10` lỗi liên tiếp) |
| `ModelPaths.OnnxModelPath`/`.ModelDirectory` | `Configuration/ModelPaths.cs` | `Program.cs` | — |
| `VisionExitCodes.CaptureInitAccessDenied`/`.ModelIntegrityCheckFailed`/`.PipelineRepeatedFailure` (hằng số, unit test được, **`PipelineRepeatedFailure=19` mới 2026-09-18**) | `Pipeline/VisionExitCodes.cs` | `Program.cs`, `ChildProcessSupervisor.DetectCaptureInitAccessDeniedBestEffort` (Service, phải khớp `17`), `CaptureLoopWorker.ProcessOneFrame` (`19`, Service chưa có xử lý riêng — rơi vào nhánh respawn chung `RunLoopAsync`) | — |
| `IFrameBufferAuditor.OnBufferAllocated`/`.OnZeroed`/`NullFrameBufferAuditor` (**`OnBufferAllocated` mới 2026-09-18**) | `Pipeline/IFrameBufferAuditor.cs` | `FrameClassificationPipeline` ctor + `EnsurePixelBuffer` (`OnBufferAllocated`), `ProcessFrame` (`OnZeroed`) — mặc định no-op Production; `tests/FrameClassificationPipelineZeroOutTests.RecordingFrameBufferAuditor` đối chiếu số lần cấp phát vs zero (regression guard IMG-040/041) | — |

## `src/ParentalGuard.Overlay/` (Đợt 1 — blur overlay tối giản + force-close, BE-030/031/032)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `Program.Main` ([STAThread], **đổi từ top-level statements** vì WinForms cần STA) | `Program.cs` | entry point (OS) | `ChildIpcBootstrap.ReadFromInheritedStdHandle`, `OverlayCoordinator` ctor, `IpcChildClient.RunForeverAsync` (qua `Task.Run` riêng), `Application.Run` (message loop chính) |
| `Program.RunIpcAsync` (private static) | `Program.cs` | `Main` (qua `Task.Run`) | `IpcChildClient.RunForeverAsync` (`onBusinessMessage = HandleBusinessMessageAsync`), `OverlayCoordinator.Invoke(Application.Exit)` khi kết thúc |
| `Program.HandleBusinessMessageAsync` (private static) | `Program.cs` | `IpcChildClient.RunForeverAsync` (callback) | `OverlayCoordinator.ApplyOverlayList` (case `OverlayRects`), console log (case `ShowToast`, BE-061b — UI toast thật Đợt 6) |
| `Program.SendForceClose` (private static) | `Program.cs` | `OverlayCoordinator` ctor (delegate `sendForceClose`) | `IpcChildClient.NewEnvelope/EnqueueOutbound` |
| `OverlayCoordinator.ApplyOverlayList` | `Rendering/OverlayCoordinator.cs` | `Program.HandleBusinessMessageAsync` (Thread IPC — tự `Invoke` sang UI thread nếu cần) | `ContentBlurOverlayForm` ctor/`.ApplyRect`, `RemoveOverlay` |
| `OverlayCoordinator.HandleCloseButtonClicked` (private) | `Rendering/OverlayCoordinator.cs` | `ContentBlurOverlayForm` (delegate `onCloseButtonClicked`, chạy trên UI thread) | `_sendForceClose` (= `Program.SendForceClose`), `RemoveOverlay` |
| `OverlayCoordinator.RemoveOverlay` (private) | `Rendering/OverlayCoordinator.cs` | `ApplyOverlayList`, `HandleCloseButtonClicked` | `ContentBlurOverlayForm.Close/Dispose` |
| `ContentBlurOverlayForm` ctor/`.ApplyRect` | `Rendering/ContentBlurOverlayForm.cs` | `OverlayCoordinator.ApplyOverlayList` | — (vẽ WinForms `Form` theo đúng rect nhận từ Service — không tự biết `OverlayRect.Reason`, Architecture/02 mục 5.1) |
| `ContentBlurOverlayForm.WndProc` (override, **mới 2026-09-18**, chủ dự án chốt: CHẶN Alt+F4) | `Rendering/ContentBlurOverlayForm.cs` | WinForms message loop (Win32 callback) | chặn `WM_SYSCOMMAND`/`SC_CLOSE` (Alt+F4, Alt+Space→Đóng) — không gọi `base.WndProc`, form chỉ đóng được qua `HandleCloseButtonClicked` hoặc `OverlayCoordinator.RemoveOverlay` gọi `Close()`/`Dispose()` trực tiếp (không qua message nên không bị chặn); `ShowInTaskbar=false` (đã có từ trước) loại luôn đường "Close window" qua taskbar |
| `ContentBlurOverlayForm.HandleCloseButtonClicked` (private) | `Rendering/ContentBlurOverlayForm.cs` | nút "Tắt nội dung" (WinForms event) | `OverlayWindowInterop.RequestClose`, delegate `onCloseButtonClicked` (= `OverlayCoordinator.HandleCloseButtonClicked`) |
| `OverlayWindowInterop.RequestClose` | `Windows/OverlayWindowInterop.cs` | `ContentBlurOverlayForm.HandleCloseButtonClicked` | `PostMessageW` (P/Invoke `user32`, `WM_CLOSE`) — **xem ghi chú gap BE-032 ở cuối file** |

## `src/ParentalGuard.Service/Data/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `DataProtectionHelper.Protect` | `DataProtectionHelper.cs` | `ConfigDb.InsertMonitoringState/InsertPauseState/InsertIpcKey` | `ProtectedData.Protect` (BCL) |
| `DataProtectionHelper.Unprotect` | `DataProtectionHelper.cs` | `ConfigDb.DecryptAndParse`, `ConfigDb.ReadIpcKey` | `ProtectedData.Unprotect` (BCL) |
| `MonitoringStateData.CreateFirstRunDefault` | `MonitoringStateData.cs` | `FailSecureConfigLoader.CreateFirstRun` | — |
| `MonitoringStateData.CreateFailSecureDefault` | `MonitoringStateData.cs` | `FailSecureConfigLoader.RunFailSecureFlowAsync` | — |
| `PauseStateData.CreateDefault` | `PauseStateData.cs` | `FailSecureConfigLoader.CreateFirstRun/RunFailSecureFlowAsync` | — |
| `ConfigDb.Open` | `ConfigDb.cs` | `FailSecureConfigLoader.LoadAsync`, `ConfigDb.CreateFresh` | — |
| `ConfigDb.ReadSnapshot` | `ConfigDb.cs` | `FailSecureConfigLoader.LoadAsync` | `ReadSchemaMeta`, `ReadMonitoringState`, `ReadPauseState`, `ReadIpcKey` |
| `ConfigDb.CreateFresh` (static) | `ConfigDb.cs` | `FailSecureConfigLoader.CreateFirstRun/RunFailSecureFlowAsync` | `Open`, `CreateSchema`, `InsertSchemaMeta`, `InsertMonitoringState`, `InsertPauseState`, `InsertIpcKey`, `InsertAuditMeta` |
| `ConfigDb.CreateSchema` (private) | `ConfigDb.cs` | `CreateFresh` | — |
| `ConfigDb.InsertSchemaMeta` (private) | `ConfigDb.cs` | `CreateFresh` | — |
| `ConfigDb.ReadSchemaMeta` (private) | `ConfigDb.cs` | `ReadSnapshot` | — |
| `ConfigDb.InsertMonitoringState` (private) | `ConfigDb.cs` | `CreateFresh` | `DataProtectionHelper.Protect` |
| `ConfigDb.ReadMonitoringState` (private) | `ConfigDb.cs` | `ReadSnapshot` | `DecryptAndParse` |
| `ConfigDb.InsertPauseState` (private) | `ConfigDb.cs` | `CreateFresh` | `DataProtectionHelper.Protect` |
| `ConfigDb.ReadPauseState` (private) | `ConfigDb.cs` | `ReadSnapshot` | `DecryptAndParse` |
| `ConfigDb.InsertIpcKey` (private) | `ConfigDb.cs` | `CreateFresh` | `DataProtectionHelper.Protect` |
| `ConfigDb.ReadIpcKey` (private) | `ConfigDb.cs` | `ReadSnapshot` | `DataProtectionHelper.Unprotect` |
| `ConfigDb.InsertAuditMeta` (private) | `ConfigDb.cs` | `CreateFresh` | — |
| `ConfigDb.DecryptAndParse<T>` (private) | `ConfigDb.cs` | `ReadMonitoringState`, `ReadPauseState` | `DataProtectionHelper.Unprotect` |
| `FailSecureConfigLoader.LoadAsync` | `FailSecureConfigLoader.cs` | `Worker.ExecuteAsync` | `ConfigDb.Open`, `ConfigDb.ReadSnapshot`, `CreateFirstRun`, `RunFailSecureFlowAsync` |
| `FailSecureConfigLoader.CreateFirstRun` (private) | `FailSecureConfigLoader.cs` | `LoadAsync` | `MonitoringStateData.CreateFirstRunDefault`, `PauseStateData.CreateDefault`, `ConfigDb.CreateFresh` |
| `FailSecureConfigLoader.RunFailSecureFlowAsync` (private) | `FailSecureConfigLoader.cs` | `LoadAsync` | `AuditLogWriter.AppendAsync`, `MonitoringStateData.CreateFailSecureDefault`, `PauseStateData.CreateDefault`, `ConfigDb.CreateFresh` |

## `src/ParentalGuard.Service/Audit/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `AuditCheckpoint.CreateGenesis` | `AuditCheckpoint.cs` | `AuditLogWriter.InitializeAsync` | — |
| `AuditLogWriter.InitializeAsync` (static) | `AuditLogWriter.cs` | `Worker.ExecuteAsync`, `AuthCoordinatorTests`/`AuditLogWriterTests` (test isolation) | `ParseRecord`, `VerifyTail`, `AppendAsync` (private overload) |
| `AuditLogWriter.AppendAsync` (public, **sửa Đợt 3 fix — Bug 4, test-runner**: bỏ tham số `path`, luôn ghi vào path đã lưu nội bộ từ `InitializeAsync`, tránh caller lệch sang `InstallPaths.AuditLogPath` production khi writer được khởi tạo bằng path test cô lập) | `AuditLogWriter.cs` | `FailSecureConfigLoader.RunFailSecureFlowAsync`, `ChildProcessSupervisor.RunLoopAsync`, `Worker.HandleVisionNetworkBlockedAsync`, `Worker.OnVisionCaptureInitAccessDenied`, `UiSessionServer.RunLoopAsync`, `OverlayDecisionCoordinator.HandleForceCloseAsync`, `AuthCoordinator.*` (6 call site) | `AppendAsync` (private overload) |
| `AuditLogWriter.AppendAsync` (private overload) | `AuditLogWriter.cs` | `AppendAsync` (public), `InitializeAsync` | `BuildRecordObject`, `SortKeys` |
| `AuditLogWriter.BuildRecordObject` (private) | `AuditLogWriter.cs` | `AppendAsync` (private), `VerifyTail` | — |
| `AuditLogWriter.SortKeys` (private) | `AuditLogWriter.cs` | `AppendAsync` (private), `VerifyTail` | — |
| `AuditLogWriter.ParseRecord` (private) | `AuditLogWriter.cs` | `InitializeAsync` | — |
| `AuditLogWriter.VerifyTail` (private) | `AuditLogWriter.cs` | `InitializeAsync` | `BuildRecordObject`, `SortKeys` |

## `src/ParentalGuard.Service/Security/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `SessionInterop.*` (P/Invoke) | `SessionInterop.cs` | `RestrictedTokenFactory`, `SessionUserLookup`, `SessionWatcher` (namespace `Session`), `Worker` (`WTSGetActiveConsoleSessionId`) | Win32 (`kernel32`/`wtsapi32`) |
| `TokenInterop.*` (P/Invoke) | `TokenInterop.cs` | `RestrictedTokenFactory` | Win32 (`advapi32`) |
| `RestrictedTokenFactory.Create` (tham số `applyLowIntegrityLevel`, **mới Đợt 1** — Architecture/05 mục 8.1, ADR-49) | `RestrictedTokenFactory.cs` | `ChildProcessLauncher.Launch` | `SessionInterop.WTSQueryUserToken/CloseHandle`, `TokenInterop.DuplicateTokenEx`, `CreateRestrictedTokenWithoutAdministrators`, `ApplyLowIntegrityLevel` (bỏ qua nếu `applyLowIntegrityLevel=false` — fallback Medium IL) |
| `RestrictedTokenFactory.CreateRestrictedTokenWithoutAdministrators` (private) | `RestrictedTokenFactory.cs` | `Create` | `TokenInterop.CreateRestrictedToken` |
| `RestrictedTokenFactory.ApplyLowIntegrityLevel` (private) | `RestrictedTokenFactory.cs` | `Create` (chỉ khi `applyLowIntegrityLevel=true`) | `TokenInterop.SetTokenInformation` |
| `ProcessInterop.CreateProcessAsUser` (P/Invoke, nhận `ref StartupInfoEx`) | `ProcessInterop.cs` | `ChildProcessLauncher.LaunchWithToken` | Win32 (`advapi32`) |
| `ProcessInterop.CreateSingleHandleAttributeList` | `ProcessInterop.cs` | `ChildProcessLauncher.LaunchWithToken` | `InitializeProcThreadAttributeList`, `UpdateProcThreadAttribute` (P/Invoke `kernel32`) |
| `ProcessInterop.ProcThreadAttributeList.Dispose` | `ProcessInterop.cs` | `ChildProcessLauncher.LaunchWithToken` (`using`) | `DeleteProcThreadAttributeList` (P/Invoke `kernel32`) |
| `ChildProcessLauncher.Launch` (tham số `applyLowIntegrityLevel`, **mới Đợt 1**) | `ChildProcessLauncher.cs` | `ChildProcessSupervisor.RunLoopAsync` (truyền `resolveLowIntegrityLevel()`) | `RestrictedTokenFactory.Create`, `LaunchWithToken`, `SessionInterop.CloseHandle` |
| `ChildProcessLauncher.LaunchWithToken` (private) | `ChildProcessLauncher.cs` | `Launch` | `ProcessInterop.CreateSingleHandleAttributeList`, `ProcessInterop.CreateProcessAsUser`, `ChildIpcBootstrap.WriteTo`, `SessionInterop.CloseHandle` |
| `PipeSecurityInterop.*` (P/Invoke) | `PipeSecurityInterop.cs` | `PipeAclFactory.ApplyLowIntegrityMandatoryLabel` | Win32 (`advapi32`/`kernel32`) |
| `PipeAclFactory.CreateServerInstance` | `PipeAclFactory.cs` | `ChildProcessSupervisor.RunLoopAsync` | `NamedPipeServerStreamAcl.Create` (BCL), `ApplyLowIntegrityMandatoryLabel` |
| `PipeAclFactory.ApplyLowIntegrityMandatoryLabel` (private) | `PipeAclFactory.cs` | `CreateServerInstance` | `PipeSecurityInterop.*` |
| `WfpInterop.*` (P/Invoke + GUID const) | `WfpInterop.cs` | `WfpVisionBlocker.*` | Win32 (`fwpuclnt.dll`) |
| `WfpVisionBlocker.Apply` | `WfpVisionBlocker.cs` | `Worker.ApplyWfpBestEffort` | `EnsureProvider`, `EnsureSubLayer`, `GetAppIdBlob`, `EnsureFilter` ×4, `WfpInterop.*` |
| `WfpVisionBlocker.EnsureProvider` (private) | `WfpVisionBlocker.cs` | `Apply` | `WfpInterop.FwpmProviderGetByKey0/FwpmProviderAdd0` |
| `WfpVisionBlocker.EnsureSubLayer` (private) | `WfpVisionBlocker.cs` | `Apply` | `WfpInterop.FwpmSubLayerGetByKey0/FwpmSubLayerAdd0` |
| `WfpVisionBlocker.GetAppIdBlob` (private) | `WfpVisionBlocker.cs` | `Apply` | `WfpInterop.FwpmGetAppIdFromFileName0` |
| `WfpVisionBlocker.EnsureFilter` (private) | `WfpVisionBlocker.cs` | `Apply` (×4, 1 lần/layer) | `WfpInterop.FwpmFilterGetByKey0/FwpmFilterAdd0` |
| `AclProvisioner.EnsureProgramDataAcl` | `AclProvisioner.cs` | `Worker.ApplyAclBestEffort` | `DirectorySecurity`/`FileSystemAccessRule` (BCL) |
| `AclProvisioner.EnsureProgramFilesAcl` | `AclProvisioner.cs` | `Worker.ApplyAclBestEffort` | `DirectorySecurity`/`FileSystemAccessRule` (BCL) |
| `AuditPolicyInterop.AuditSetSystemPolicy` (P/Invoke) | `AuditPolicyInterop.cs` | `VisionNetworkWatcher.EnableFilteringPlatformConnectionAuditing` | Win32 (`advapi32`) |
| `VisionNetworkWatcher.Start` | `VisionNetworkWatcher.cs` | `Worker.StartVisionNetworkWatcherBestEffort` | `EnableFilteringPlatformConnectionAuditing`, `EventLogWatcher` (BCL) |
| `VisionNetworkWatcher.EnableFilteringPlatformConnectionAuditing` (private) | `VisionNetworkWatcher.cs` | `Start` | `AuditPolicyInterop.AuditSetSystemPolicy` |
| `VisionNetworkWatcher.OnEventRecordWritten` (private) | `VisionNetworkWatcher.cs` | `EventLogWatcher` (BCL callback) | raises `NetworkBlocked` → `Worker.OnVisionNetworkBlocked` |
| `VisionNetworkWatcher.Dispose` | `VisionNetworkWatcher.cs` | `Worker.StopAllAsync` | — |
| `PipeIdentityInterop.GetNamedPipeClientProcessId` (P/Invoke) | `PipeIdentityInterop.cs` | `ChildProcessSupervisor.VerifyClientIdentity` | Win32 (`kernel32`) |
| `SessionUserLookup.GetUserSid` | `SessionUserLookup.cs` | `ChildProcessSupervisor.RunLoopAsync` | `SessionInterop.WTSQueryUserToken/CloseHandle` |

## `src/ParentalGuard.Service/Session/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `WindowInterop.*` (P/Invoke) | `WindowInterop.cs` | `SessionWatcher.*` | Win32 (`user32`/`kernel32`) |
| `SessionWatcher.Start` | `SessionWatcher.cs` | `Worker.ExecuteAsync` | `ThreadMain` (new Thread) |
| `SessionWatcher.ThreadMain` (private) | `SessionWatcher.cs` | thread entry (từ `Start`) | `WindowInterop.CreateWindowExW/SetWindowLongPtrW/GetMessageW/TranslateMessage/DispatchMessageW`, `SessionInterop.WTSRegisterSessionNotification` |
| `SessionWatcher.WndProc` (private) | `SessionWatcher.cs` | `DispatchMessageW` (Win32 callback) | raises `SessionChanged` → `Worker.HandleSessionChangeAsync`; `WindowInterop.CallWindowProcW` |
| `SessionWatcher.Dispose` | `SessionWatcher.cs` | `Worker.StopAllAsync` | `SessionInterop.WTSUnRegisterSessionNotification`, `WindowInterop.PostThreadMessageW` |

## `src/ParentalGuard.Service/Ipc/` (Đợt 1: reader/writer/ping tách rời — ADR-39 đối xứng phía server, quyết định overlay)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `ChildProcessSupervisor.StartForSessionAsync` | `ChildProcessSupervisor.cs` | `Worker.StartChildrenForSessionAsync` | `StopCurrentAsync`, `RunLoopAsync` |
| `ChildProcessSupervisor.StopAsync` | `ChildProcessSupervisor.cs` | `Worker.StopAllAsync` | `StopCurrentAsync` |
| `ChildProcessSupervisor.RequestChildRestart` | `ChildProcessSupervisor.cs` | `Worker.HandleVisionNetworkBlockedAsync` | `KillIfAlive` |
| `ChildProcessSupervisor.TryEnqueueBusinessMessage` (public, **mới Đợt 1**) | `ChildProcessSupervisor.cs` | `OverlayDecisionCoordinator.PushCurrentList` (Overlay channel) | `IpcEnvelope.NewEnvelope`, `_outbound.Writer.TryWrite` — `false` nếu chưa/không còn kết nối (fail-secure: lần reconnect kế tiếp tự resend qua `configureInitialPush`) |
| `ChildProcessSupervisor.StopCurrentAsync` (private) | `ChildProcessSupervisor.cs` | `StartForSessionAsync`, `StopAsync` | `KillIfAlive` |
| `ChildProcessSupervisor.RunLoopAsync` (private) | `ChildProcessSupervisor.cs` | `StartForSessionAsync` (qua `Task.Run`) | `SessionUserLookup.GetUserSid`, `PipeAclFactory.CreateServerInstance`, `resolveLowIntegrityLevel` (lambda từ `Worker`, **mới**), `ChildProcessLauncher.Launch`, `VerifyClientIdentity`, `HandshakeAsync`, `RunConnectionAsync`, `SendGracefulStopAsync`, `AuditLogWriter.AppendAsync`, `DetectCaptureInitAccessDeniedBestEffort` (**mới**), `KillIfAlive`; gọi `configureInitialPush`/`oneTimeInitialMessages` (như cũ, nay ghi vào `_outbound` Channel thay vì trực tiếp pipe) |
| `ChildProcessSupervisor.RunConnectionAsync` (private, **mới Đợt 1**) | `ChildProcessSupervisor.cs` | `RunLoopAsync` | `ReaderLoopAsync`, `WriterLoopAsync`, `HeartbeatPingLoopAsync` (song song, `Task.WhenAny` — đối xứng `IpcChildClient.RunConnectionAsync`) |
| `ChildProcessSupervisor.ReaderLoopAsync` (private, **mới Đợt 1**) | `ChildProcessSupervisor.cs` | `RunConnectionAsync` | `IpcFrameTransport.ReadFrameAsync`; `HeartbeatAck` → `heartbeatAcks` Channel; message khác → `onBusinessMessage` (= `OverlayDecisionCoordinator.HandleVisionResultAsync`/`HandleForceCloseAsync`, tuỳ kênh) |
| `ChildProcessSupervisor.WriterLoopAsync` (private, **mới Đợt 1**) | `ChildProcessSupervisor.cs` | `RunConnectionAsync` | `IpcFrameTransport.WriteFrameAsync` (đọc từ `_outbound` Channel) |
| `ChildProcessSupervisor.HeartbeatPingLoopAsync` (private, đổi tên từ `HeartbeatLoopAsync`) | `ChildProcessSupervisor.cs` | `RunConnectionAsync` | `IpcEnvelope.NewEnvelope`, ghi `HeartbeatPing` vào `outbound` Channel, `WaitForAckAsync` |
| `ChildProcessSupervisor.WaitForAckAsync` (private static, **mới Đợt 1**) | `ChildProcessSupervisor.cs` | `HeartbeatPingLoopAsync` | đọc `heartbeatAcks` Channel với timeout `heartbeatInterval` |
| `ChildProcessSupervisor.DetectCaptureInitAccessDeniedBestEffort` (private, **mới Đợt 1**) | `ChildProcessSupervisor.cs` | `RunLoopAsync` (finally, sau khi child thoát) | `onCaptureInitAccessDeniedExitCode` (lambda = `Worker.OnVisionCaptureInitAccessDenied`, chỉ set cho kênh Vision) khi `childProcess.ExitCode == 17` |
| `ChildProcessSupervisor.HandshakeAsync` (private) | `ChildProcessSupervisor.cs` | `RunLoopAsync` | `IpcFrameTransport.ReadFrameAsync/WriteFrameAsync`, `IpcEnvelope.NewEnvelope` |
| `ChildProcessSupervisor.SendGracefulStopAsync` (private) | `ChildProcessSupervisor.cs` | `RunLoopAsync` | `IpcFrameTransport.WriteFrameAsync` |
| `ChildProcessSupervisor.VerifyClientIdentity` (private) | `ChildProcessSupervisor.cs` | `RunLoopAsync` | `PipeIdentityInterop.GetNamedPipeClientProcessId` |
| `ChildProcessSupervisor.KillIfAlive` (private static) | `ChildProcessSupervisor.cs` | `RunLoopAsync`, `StopCurrentAsync`, `RequestChildRestart` | — |
| `OverlayThresholdDecision.Violates` (pure, unit test được, **mới Đợt 1**) | `OverlayThresholdDecision.cs` | `OverlayDecisionCoordinator.HandleVisionResultAsync` | — (`IMG-013`/`BE-090`: `risk_score >= threshold`) |
| `OverlayDecisionCoordinator.ConfigureInitialPush` (**mới Đợt 1**) | `OverlayDecisionCoordinator.cs` | `Worker.cs` (lambda `configureInitialPush` của `_overlaySupervisor`) | `BuildCommand` |
| `OverlayDecisionCoordinator.HandleVisionResultAsync` (**mới Đợt 1**) | `OverlayDecisionCoordinator.cs` | `ChildProcessSupervisor.ReaderLoopAsync` (kênh Vision, `onBusinessMessage`) | `OverlayThresholdDecision.Violates`, `PushCurrentList` |
| `OverlayDecisionCoordinator.HandleForceCloseAsync` (**mới Đợt 1**) | `OverlayDecisionCoordinator.cs` | `ChildProcessSupervisor.ReaderLoopAsync` (kênh Overlay, `onBusinessMessage`) | `PushCurrentList`, `AuditLogWriter.AppendAsync` (`ForceCloseRequested`) |
| `OverlayDecisionCoordinator.PushCurrentList`/`BuildCommand` (private, **mới Đợt 1**) | `OverlayDecisionCoordinator.cs` | `HandleVisionResultAsync`, `HandleForceCloseAsync`, `ConfigureInitialPush` | `ChildProcessSupervisor.TryEnqueueBusinessMessage` (chỉ `PushCurrentList`) |

## `src/ParentalGuard.Service/Worker.cs`, `Program.cs`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `Worker.ExecuteAsync` (override) | `Worker.cs` | `BackgroundService` (Generic Host) | `ApplyAclBestEffort`, `AuditLogWriter.InitializeAsync`, `FailSecureConfigLoader.LoadAsync`, `ApplyWfpBestEffort`, `new ChildProcessSupervisor` ×2, `new OverlayDecisionCoordinator` (**mới Đợt 1**, giữa 2 supervisor vì Overlay supervisor cần trước, Vision supervisor cần tham chiếu coordinator), `StartVisionNetworkWatcherBestEffort`, `new SessionWatcher`, `WaitForActiveSessionAsync`, `StartChildrenForSessionAsync`, `StopAllAsync` |
| `Worker.OnVisionCaptureInitAccessDenied` (private, **mới Đợt 1**) | `Worker.cs` | `ChildProcessSupervisor` (lambda `onCaptureInitAccessDeniedExitCode` của `_visionSupervisor`) | set `_visionRequiresMediumIl = true`, `AuditLogWriter.AppendAsync` (`VisionCaptureFallbackMediumIl`, đúng 1 lần/phiên qua `Interlocked.Exchange`) |
| `Worker.ApplyAclBestEffort` (private) | `Worker.cs` | `ExecuteAsync` | `AclProvisioner.EnsureProgramDataAcl/EnsureProgramFilesAcl` |
| `Worker.ApplyWfpBestEffort` (private) | `Worker.cs` | `ExecuteAsync` | `WfpVisionBlocker.Apply` |
| `Worker.StartVisionNetworkWatcherBestEffort` (private) | `Worker.cs` | `ExecuteAsync` | `VisionNetworkWatcher.Start` |
| `Worker.OnVisionNetworkBlocked` (private) | `Worker.cs` | `VisionNetworkWatcher.NetworkBlocked` event | `HandleVisionNetworkBlockedAsync` |
| `Worker.HandleVisionNetworkBlockedAsync` (private) | `Worker.cs` | `OnVisionNetworkBlocked` | `AuditLogWriter.AppendAsync`, `ChildProcessSupervisor.RequestChildRestart` |
| `Worker.HandleSessionChangeAsync` (private) | `Worker.cs` | `SessionWatcher.SessionChanged` event | `SessionInterop.WTSGetActiveConsoleSessionId`, `StartChildrenForSessionAsync` |
| `Worker.StartChildrenForSessionAsync` (private) | `Worker.cs` | `ExecuteAsync`, `HandleSessionChangeAsync` | `ChildProcessSupervisor.StartForSessionAsync` ×2 |
| `Worker.StopAllAsync` (private) | `Worker.cs` | `ExecuteAsync` | `ChildProcessSupervisor.StopAsync` ×2, `VisionNetworkWatcher.Dispose`, `SessionWatcher.Dispose` |
| `Worker.WaitForActiveSessionAsync` (private static) | `Worker.cs` | `ExecuteAsync` | `SessionInterop.WTSGetActiveConsoleSessionId` |
| `Worker.BuildControlVisionCommand` (private static) | `Worker.cs` | `ExecuteAsync` (lambda truyền vào `ChildProcessSupervisor`) | — |
| `Worker.BuildOverlayOneTimeMessages` (private static) | `Worker.cs` | `ExecuteAsync` (truyền vào `new ChildProcessSupervisor` cho Overlay) | `BuildFailSecureToast` |
| `Worker.BuildFailSecureToast` (private static) | `Worker.cs` | `BuildOverlayOneTimeMessages` (lambda, chạy trong `ChildProcessSupervisor.RunLoopAsync`) | — |
| top-level `Program` | `Program.cs` | entry point (SCM/OS) | `Host.CreateApplicationBuilder`, `AddWindowsService`, `AddHostedService<Worker>` |

## Đợt 2 (Architecture/07-overlay-architecture.md) — vùng loại trừ 3 lớp, đa cửa sổ/z-index/gộp, multi-monitor

Chỉ liệt kê hàm mới/thay đổi quan hệ gọi so với Đợt 1 — các hàm Đợt 1 không đổi chữ ký/không đổi
caller/callee giữ nguyên ở bảng phía trên.

### `src/ParentalGuard.Ipc/` (schema)

| Thay đổi | File | Ghi chú |
|---|---|---|
| `ForceCloseRequest.Source` (field 4, enum `CloseSource`) | `Protos/ipc.proto` | `BE-089b` — Overlay luôn set, Service map sang `"manual"`/`"auto-timeout"` |
| `OverlayRect.IsMerged`/`.MergedWindowHandles` (field 6/7) | `Protos/ipc.proto` | `BE-088`/`089` |
| `MonitoringStatusUpdate`/`IconState` (field 63), `IconPositionUpdate` (field 64), `IconLayoutSync` (field 65) | `Protos/ipc.proto` | `FE-020`-`022` |

### `src/ParentalGuard.Service/Ipc/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `ChildProcessSupervisor` ctor (tham số `initialPushBuilders` thay `configureInitialPush` đơn lẻ; thêm `onSessionConnected`/`onSessionEnded`, **mới Đợt 2**) | `ChildProcessSupervisor.cs` | `Worker.ExecuteAsync` ×2 (Vision/Overlay) | — |
| `ChildProcessSupervisor.RunLoopAsync` (private, sửa Đợt 2) | `ChildProcessSupervisor.cs` | `StartForSessionAsync` | thêm: lặp `initialPushBuilders` (mỗi builder = 1 envelope riêng, resend mỗi lần reconnect — Architecture/03 mục 4.3), gọi `onSessionConnected` sau handshake, `onSessionEnded` trong catch (session lost) |
| `OverlayMergeThreshold.ShouldBeMerged` (pure, unit test được, **mới**) | `OverlayMergeThreshold.cs` | `OverlayDecisionCoordinator.BuildCommand` | — (`BE-088`/`089`, ADR-57 hysteresis) |
| `CloseSourceMapper.ToAuditLogValue` (pure, unit test được, **mới**) | `CloseSourceMapper.cs` | `OverlayDecisionCoordinator.HandleForceCloseAsync` | — (`BE-089b`) |
| `OverlayDecisionCoordinator.BuildCommand` (private, sửa Đợt 2) | `OverlayDecisionCoordinator.cs` | `PushCurrentList`, `ConfigureInitialPush` | thêm: `OverlayMergeThreshold.ShouldBeMerged`, `StableMergedOverlayId` khi gộp |
| `OverlayDecisionCoordinator.StableMergedOverlayId` (private, **mới**) | `OverlayDecisionCoordinator.cs` | `BuildCommand` | — (namespace `overlay_id` riêng cho chế độ gộp, mục 3.2) |
| `OverlayDecisionCoordinator.HandleForceCloseAsync` (sửa Đợt 2) | `OverlayDecisionCoordinator.cs` | `Worker.DispatchOverlayBusinessMessageAsync` | thêm: `CloseSourceMapper.ToAuditLogValue` — ghi field `source` vào audit log |
| `IconStatusCoordinator.SetState`/`.ConfigureInitialPush` (**mới**) | `IconStatusCoordinator.cs` | `Worker.cs` (`onSessionConnected`/`onSessionEnded` lambda ×2, `initialPushBuilders`) | `ChildProcessSupervisor.TryEnqueueBusinessMessage` (`SetState`) — `FE-021`, ADR-65 |
| `IconPositionCoordinator.HandleIconPositionUpdateAsync`/`.ConfigureInitialPush` (**mới**) | `IconPositionCoordinator.cs` | `Worker.DispatchOverlayBusinessMessageAsync` (Handle), `Worker.cs` (`initialPushBuilders`, ConfigureInitialPush) | `ConfigDb.Open/UpsertIconPosition/ReadAllIconPositions` — `FE-020a` |
| `Worker.DispatchOverlayBusinessMessageAsync` (private, **mới**) | `Worker.cs` | `ChildProcessSupervisor` (Overlay, `onBusinessMessage`) | `OverlayDecisionCoordinator.HandleForceCloseAsync` (case `ForceClose`), `IconPositionCoordinator.HandleIconPositionUpdateAsync` (case `IconPositionUpdate`) |

### `src/ParentalGuard.Service/Data/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `ConfigDb.CurrentSchemaVersion = 2`, `ConfigDb.MigrateFromV1ToV2` (private, **mới**) | `ConfigDb.cs` | `ReadSnapshot` (khi `schemaVersion < CurrentSchemaVersion`) | — (`CREATE TABLE IF NOT EXISTS icon_positions` + bump `schema_meta.schema_version`, idempotent) |
| `ConfigDb.UpsertIconPosition`/`.ReadAllIconPositions` (public, **mới**; **sửa 2026-09-20 audit fix**: `UpsertIconPosition` bọc `ExecuteNonQuery` → `ConfigLoadException`, trước đó `SqliteException` lọt ra ngoài dù `IconPositionCoordinator.HandleIconPositionUpdateAsync` đã catch `ConfigLoadException or IOException` — cùng lớp bug với `UpdatePauseState`) | `ConfigDb.cs` | `IconPositionCoordinator.HandleIconPositionUpdateAsync`/`.ConfigureInitialPush` | — |
| `IconPositionData` (record, **mới**) | `IconPositionData.cs` | `ConfigDb.*IconPosition*`, `IconPositionCoordinator.*` | — |

### `src/ParentalGuard.Overlay/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `Program.Main` (thêm `SetProcessDpiAwarenessContext`, sửa Đợt 2) | `Program.cs` | entry point (OS) | + `SetProcessDpiAwarenessContext` (ADR-61), `OverlayCoordinator` ctor (2 tham số, thêm `sendIconPosition`) |
| `Program.HandleBusinessMessageAsync` (sửa Đợt 2) | `Program.cs` | `IpcChildClient.RunForeverAsync` | thêm case `MonitoringStatus` → `OverlayCoordinator.ApplyMonitoringStatus`, case `IconLayoutSync` → `.ApplyIconLayoutSync` |
| `Program.SendIconPosition` (private static, **mới**) | `Program.cs` | `OverlayCoordinator` ctor (delegate `sendIconPosition`) | `IpcChildClient.NewEnvelope/EnqueueOutbound` |
| `OverlayCoordinator` ctor (sửa Đợt 2 — thêm `StatusIconManager`, `WinEventHookInterop.Register`) | `Rendering/OverlayCoordinator.cs` | `Program.Main` | `StatusIconManager` ctor, `WinEventHookInterop.Register` |
| `OverlayCoordinator.ApplyOverlayList` (sửa Đợt 2) | `Rendering/OverlayCoordinator.cs` | `Program.HandleBusinessMessageAsync` | thêm: diff theo `IsMerged` (xoá+tạo lại nếu đổi chế độ), `ResyncZOrder` cuối hàm |
| `OverlayCoordinator.ApplyMonitoringStatus`/`.ApplyIconLayoutSync`/`.ApplyDisconnected` (**mới**) | `Rendering/OverlayCoordinator.cs` | `Program.cs` (2 hàm đầu qua `onBusinessMessage`; `ApplyDisconnected` qua `IpcChildClient.RunForeverAsync` `onDisconnected`) | `StatusIconManager.ApplyStatus/.ApplyLayoutSync/.ApplyDisconnected` |
| `OverlayCoordinator.WndProc` (override, **mới** — bắt `WM_DISPLAYCHANGE`) | `Rendering/OverlayCoordinator.cs` | WinForms message loop | `StatusIconManager.RefreshMonitors` (`BE-083` hot-plug) |
| `OverlayCoordinator.HandleMergedCloseTriggered`/`.RemoveOverlayByOverlayId` (private, **mới**) | `Rendering/OverlayCoordinator.cs` | `ContentBlurOverlayForm` (delegate `onMergedCloseTriggered`) | `_sendForceClose` ×N (1/handle), `RemoveOverlayByOverlayId` |
| `OverlayCoordinator.OnWinEvent`/`.RestartDebounceTimer`/`.FlushWinEvents`/`.ResyncZOrder` (private, **mới**) | `Rendering/OverlayCoordinator.cs` | `WinEventHookInterop.Register` callback (`OnWinEvent`); `Timer.Tick` (`FlushWinEvents`); `ApplyOverlayList`+`FlushWinEvents` (`ResyncZOrder`) | `ContentBlurOverlayForm.OnTrackedWindowLocationChanged`, `ZOrderSync.Resync` (`FE-016b`/`BE-087`, debounce 50ms mục 2.6/3.5) |
| `ContentBlurOverlayForm` ctor (sửa Đợt 2 — thêm tham số `isMerged`/`onMergedCloseTriggered` qua `OverlayRect`; sửa FE-016g — thêm `AddCountdownLabel`) | `Rendering/ContentBlurOverlayForm.cs` | `OverlayCoordinator.CreateAndShow` | `ApplyMergedBounds`+`StartAutoTimeout`+`AddCountdownLabel` (nếu `IsMerged`) hoặc `ApplyRect` |
| `ContentBlurOverlayForm.ApplyRect`/`.OnTrackedWindowLocationChanged`/`.RecalculateExclusion`/`.UpgradeExclusionAsync`/`.ApplyExclusionRegion` (**mới/sửa**) | `Rendering/ContentBlurOverlayForm.cs` | `OverlayCoordinator` (`ApplyRect`, `OnTrackedWindowLocationChanged`) | `DwmInterop.GetExtendedFrameBounds`, `MonitorInterop.GetDpiScale`, `ExclusionRegionCalculator.FallbackRect/PaddedRect`, `CloseButtonLocator.LookupAsync` — `FE-016`/`016b`/`016c` |
| `ContentBlurOverlayForm.ApplyMergedBounds`/`.HandleCloseButtonClicked`/`.TriggerForceCloseAll`/`.StartAutoTimeout` (**mới**) | `Rendering/ContentBlurOverlayForm.cs` | ctor, nút "Tắt nội dung", `_autoTimeoutTimer.Tick` | `MonitorInterop.GetMonitorInfoForWindow`, `OverlayWindowInterop.RequestClose` ×N, delegate `onMergedCloseTriggered` — `BE-088a`/`089a`/`089b` |
| `ContentBlurOverlayForm.AddCountdownLabel` (**mới, FE-016g v0.9.1**) | `Rendering/ContentBlurOverlayForm.cs` | ctor (nhánh `IsMerged`, sau `StartAutoTimeout`) | `OverlayStrings.AutoTimeoutCountdown` (text ban đầu + subscribe `CountdownTick` để cập nhật mỗi giây) — chỉ tạo ở overlay full-screen lock, không ở chế độ thường (`BE-089b`) |
| `ExclusionRegionCalculator.FallbackRect`/`.PaddedRect` (pure, unit test được, **mới**) | `Windows/ExclusionRegionCalculator.cs` | `ContentBlurOverlayForm.RecalculateExclusion`/`.UpgradeExclusionAsync` | — |
| `CloseButtonMatcher.FindBestMatch` (pure, unit test được, **mới**) | `Windows/CloseButtonMatcher.cs` | `CloseButtonLocator.RunLookup` | — (ADR-55) |
| `CloseButtonLocator.LookupAsync`/`.RunLookup` (**mới**) | `Windows/CloseButtonLocator.cs` | `ContentBlurOverlayForm.UpgradeExclusionAsync` | COM `IUIAutomation` (`Interop.UIAutomationClient`), `CloseButtonMatcher.FindBestMatch` — Thread STA riêng (ADR-53) |
| `DwmInterop.GetExtendedFrameBounds` (**mới**) | `Windows/DwmInterop.cs` | `ContentBlurOverlayForm.*`, `CloseButtonLocator` (`windowRect` truyền vào) | `DwmGetWindowAttribute` (P/Invoke) |
| `MonitorInterop.GetMonitorInfoForWindow`/`.EnumerateMonitors`/`.GetDpiScale` (**mới**) | `Windows/MonitorInterop.cs` | `ContentBlurOverlayForm.ApplyMergedBounds`, `StatusIconManager.RefreshMonitors`, `ContentBlurOverlayForm`/`StatusIconForm` (DPI) | `MonitorFromWindow`/`GetMonitorInfoW`/`EnumDisplayMonitors`/`GetDpiForWindow` (P/Invoke) |
| `WinEventHookInterop.Register` (**mới**) | `Windows/WinEventHookInterop.cs` | `OverlayCoordinator` ctor | `SetWinEventHook`/`UnhookWinEvent` (P/Invoke) — `BE-087`/`FE-016b` |
| `ZOrderSync.ComputeBackToFrontApplicationOrder` (pure, unit test được, **mới**), `.EnumerateTopLevelWindowsInZOrder`/`.Resync` | `Windows/ZOrderSync.cs` | `OverlayCoordinator.ResyncZOrder` | `EnumWindows`/`SetWindowPos` (P/Invoke) — `BE-087`, ADR-56 |
| `StatusIconManager.Start`/`.RefreshMonitors`/`.ApplyStatus`/`.ApplyLayoutSync`/`.ApplyDisconnected` (**mới**) | `Icons/StatusIconManager.cs` | `OverlayCoordinator.*` | `MonitorInterop.EnumerateMonitors`, `StatusIconForm` ctor/`.ApplyAppearance`, delegate `sendIconPosition` (= `Program.SendIconPosition`) — `FE-020`-`022` |
| `StatusIconForm.ApplyAppearance`/`.Render`/`OnMouseDown`/`OnMouseMove`/`OnMouseUp` (**mới**) | `Icons/StatusIconForm.cs` | `StatusIconManager.*` | `LayeredIconRenderer.Render`, `MonitorInterop.GetDpiScale`, delegate `onPositionCommitted` (`OnMouseUp`) |
| `LayeredIconRenderer.Render` (**mới**) | `Icons/LayeredIconRenderer.cs` | `StatusIconForm.Render` | `UpdateLayeredWindow`/`CreateDIBSection` (P/Invoke) — ADR-64 |
| `OverlayStrings.IconTooltipActive`/`.IconTooltipError`/`.IconTooltipPaused` (**mới**) | `Resources/OverlayStrings.cs` | `StatusIconManager.ApplyAppearance` | `ResourceManager.GetString` (`OverlayStrings.resx`) — `FE-022`/`FE-060` |
| `OverlayStrings.AutoTimeoutCountdown` (**mới, FE-016g v0.9.1**, internal) | `Resources/OverlayStrings.cs` | `ContentBlurOverlayForm.AddCountdownLabel` (text ban đầu + listener `CountdownTick`); `tests/AutoTimeoutCountdownTests` (qua `InternalsVisibleTo("ParentalGuard.Overlay.Tests")`, `src/ParentalGuard.Overlay/AssemblyInfo.cs`, **mới**) | `ResourceManager.GetString` (`OverlayStrings.resx`) — `FE-016g`/`BE-089b`/`FE-060` |

### `src/ParentalGuard.Vision/` (`BE-080`–`082`, `IMG-020` — capture đa màn hình, Architecture/05 mục 3.5 v0.2.0)

Chỉ liệt kê hàm mới/thay đổi so với bảng "Đợt 1" phía trên — các hàm Đợt 1 không liệt kê lại ở đây
giữ nguyên chữ ký/callee, TRỪ các hàm dưới đây (đã đổi).

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `MonitorSelector.EnumerateOutputs` (**mới**, thay `SelectOutputForWindow` đã xoá) | `Capture/MonitorSelector.cs` | `CaptureLoopWorker.Run` (1 lần/chu kỳ, không cache giữa các chu kỳ — tự phản ánh thêm/rút màn hình mà không cần bắt `WM_DISPLAYCHANGE`, câu hỏi mở Architecture/05 mục 11) | `IDXGIFactory1.EnumAdapters1`/`IDXGIAdapter1.EnumOutputs` (Vortice.DXGI) |
| `MonitorSelector.GetMonitorForWindow`/`.MatchOutputByMonitor` (pure)/`.ResolveOutputForWindow` (**mới**, ADR-63) | `Capture/MonitorSelector.cs` | `CaptureLoopWorker.ProcessCycle` (`GetMonitorForWindow` truyền làm delegate `monitorOf` cho `CandidateWindowSelector`; `ResolveOutputForWindow` để map candidate → `(adapterIndex, outputIndex)`) | `MonitorFromWindow` (P/Invoke `user32`, chỉ trong `GetMonitorForWindow`) |
| `WindowZOrderEnumerator.EnumerateTopLevelWindowsInZOrder` (**mới**) | `Capture/WindowZOrderEnumerator.cs` | `CaptureLoopWorker.ProcessCycle` (chỉ khi `outputs.Count > 1`, `BE-082`/`PERF-020`) | `EnumWindows` (P/Invoke `user32`) |
| `CandidateWindowChecks.IsVisibleTopLevelWindow` (**mới**) | `Capture/CandidateWindowChecks.cs` | `CaptureLoopWorker.ProcessCycle` (lambda `isCandidateWindow`, kết hợp `ExcludeProcessMatcher.IsExcluded`) | `IsWindowVisible`/`IsIconic`/`GetWindowTextLengthW` (P/Invoke `user32`) |
| `CandidateWindowSelector.SelectCandidates` (pure, unit test được, **mới**, ADR-64) | `Pipeline/CandidateWindowSelector.cs` | `CaptureLoopWorker.ProcessCycle` | — (nhận delegate `monitorOf`/`isCandidateWindow` thay vì gọi P/Invoke trực tiếp) |
| `OutputCaptureContext` ctor/`.Dispose` (**mới**) | `Pipeline/OutputCaptureContext.cs` | `Program.cs` (context đầu tiên, tái dùng cặp probe IL), `CaptureLoopWorker.CreateOutputCaptureContext` | `IFrameCapture.Dispose`/`IWindowCropper.Dispose` |
| `OutputCaptureContextPool.GetOrCreate`/`.EndCycle`/`.DisposeAll` (unit test được qua seam `createContext`, **mới**, ADR-65) | `Pipeline/OutputCaptureContextPool.cs` | `CaptureLoopWorker.ProcessCycle` (`GetOrCreate`/`EndCycle`), `CaptureLoopWorker.Run` (`DisposeAll`, khi thoát vòng lặp) | `OutputCaptureContext.Dispose` (khi idle ≥ 5 chu kỳ liên tiếp hoặc `DisposeAll`) |
| `FrameClassificationPipeline.Process`/`.ProcessFrame` (chữ ký **sửa Đợt 2** — nhận `IFrameCapture`/`IWindowCropper` theo tham số thay vì field constructor; pipeline **không còn `IDisposable`**, classifier không còn bị pipeline sở hữu/dispose) | `Pipeline/FrameClassificationPipeline.cs` | `CaptureLoopWorker.ProcessOneFrame` (truyền `context.Capture`/`context.Cropper` của đúng `OutputCaptureContext`) | như cũ (`WindowRectResolver.Resolve`, `IFrameCapture.AcquireNextFrame/ReleaseFrame`, `IWindowCropper.CropAndReadBack`, `FrameResizerNormalizer.Resize`, `INsfwClassifier.Classify`, `RiskScoreAggregator.Aggregate`) |
| `CaptureLoopWorker` ctor (**sửa** — thêm tham số `OutputCaptureContext initialOutputContext`), `.Run`/`.ProcessCycle` (**mới**, thay đường `hwnd = GetForegroundWindow()` đơn lẻ)/`.CreateOutputCaptureContext` (private static, **mới**)/`.ProcessOneFrame` (tham số **sửa** — nhận `OutputCaptureContext context`) | `Pipeline/CaptureLoopWorker.cs` | `Program.cs` (ctor/Start/WakeUp), Thread riêng (`Run`, ADR-38) | `MonitorSelector.EnumerateOutputs/GetMonitorForWindow/ResolveOutputForWindow`, `WindowZOrderEnumerator.EnumerateTopLevelWindowsInZOrder`, `CandidateWindowSelector.SelectCandidates`, `CandidateWindowChecks.IsVisibleTopLevelWindow`, `ExcludeProcessMatcher.IsExcluded`, `OutputCaptureContextPool.GetOrCreate/EndCycle/DisposeAll`, `FrameClassificationPipeline.Process`, `IpcChildClient.*` |
| top-level `Program` (wiring **sửa Đợt 2**) | `Program.cs` | entry point (OS) | thêm: `OutputCaptureContext` ctor (tái dùng `capture`/`GpuWindowCropper` đã probe IL làm context output 0), `FrameClassificationPipeline` ctor (nay chỉ nhận `classifier`, không còn `capture`/`cropper`, không còn `using`), `CaptureLoopWorker` ctor (thêm `initialOutputContext`) |

### Khoảng trống đã biết — Đợt 2 (báo cáo lại, không tự quyết định)

- **Icon glyph/mã màu hex chính xác** (`FE-021`): dùng tạm `Color.FromArgb` (xanh `(46,160,67)`,
  vàng `(255,193,7)`, đỏ `(220,53,69)`) — đúng ghi chú Architecture/07 mục 4.1.1 "chi tiết asset/theme,
  không chặn thiết kế kiến trúc", có thể đổi khi có Design System chính thức.
- **Padding 16px lớp 2 / fallback 160×50 lớp 3** (`FE-016c`): vẫn là giá trị tạm thời theo đúng ghi
  chú ở `Architecture/07-overlay-architecture.md` mục 6 — cần đo thực tế trên app ưu tiên `BE-075`
  trước khi khoá cứng, chưa thực hiện ở lượt này (cần session tương tác thật để đo).
- **Validate thực nghiệm UIA dưới Low Integrity Level** (câu hỏi mở `Architecture/07` mục 6): code
  đã viết đúng thiết kế (Thread STA riêng, try/catch bọc toàn bộ COM call) nhưng chưa chạy thử trên
  máy Windows thật với `Overlay` ở Low IL — lớp 3 fallback không phụ thuộc nên không chặn, nhưng cần
  xác nhận thực nghiệm trước khi coi lớp 1 là "đã hoạt động đúng".

## Đợt 3 (Architecture/08-password-authentication-architecture.md) — Password & Authentication

### `src/ParentalGuard.Ipc/` (schema + memory hygiene dùng chung)

| Thay đổi | File | Ghi chú |
|---|---|---|
| 12 message mới field 80-91 (`SetInitialPasswordRequest/Response`, `ConfirmRecoveryKeySavedRequest/Response`, `AuthVerifyRequest/Response`, `ChangePasswordRequest/Response`, `RecoveryResetRequest/Response`, `AuthStatusQuery/Response`) + 4 enum (`SetupResult`/`ConfirmResult`/`AuthResult`/`ChangeResult`/`RecoveryResetResult`) | `Protos/ipc.proto` | `PWD-0xx`, Architecture/08 mục 7. Giá trị enum đặt tiền tố tên type (`SETUP_RESULT_SUCCESS`, `AUTH_RESULT_SUCCESS`...) khác bare `SUCCESS` ở văn bản Architecture/08 — proto3 C++ enum scoping không cho phép nhiều enum cùng package dùng chung tên bare `SUCCESS`/`LOCKED_OUT`/`NEW_PASSWORD_TOO_LONG` (lỗi build thật lúc implement) — thuần đổi định danh, không đổi field number/ý nghĩa |
| `CredentialBytes.UnsafeGetBuffer`/`.Zero` (**mới**) | `Security/CredentialBytes.cs` | `AuthCoordinator.*` (Service) — ADR-73 (mục 5.2). **Lệch cài đặt so với Architecture/08**: văn bản nêu `Google.Protobuf.UnsafeByteOperations.UnsafeGetBuffer` — API này KHÔNG tồn tại ở `Google.Protobuf 3.32.1` (đã pin từ Đợt 0, xác nhận qua reflection). Thay bằng `MemoryMarshal.AsMemory(byteString.Memory)` + `MemoryMarshal.TryGetArray` (cùng mục đích: lấy buffer nội bộ không copy thêm, verify bằng thực nghiệm — xem XML-doc trong file) |

### `src/ParentalGuard.Service/Auth/` (mới — toàn bộ business logic, transport-agnostic)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `Argon2Params.Official` (hằng số, `m=32768,t=2,p=2`) | `Argon2Params.cs` | `AuthCoordinator.*` | — |
| `Argon2idHasher.Hash`/`.Verify` (unit test được) | `Argon2idHasher.cs` | `AuthCoordinator.*` | `Konscious.Security.Cryptography.Argon2id` (thư viện ngoài), `ParsePhc`/`FormatPhc`/`ComputeHash` (private) |
| `Argon2idHasher.ComputeAndDiscard` (**mới Đợt 3 fix — FAIL 3, ADR-84**: chạy `ComputeHash` thật rồi `ZeroMemory` kết quả ngay, không so sánh/trả về gì) | `Argon2idHasher.cs` | `AuthCoordinator.RunDecoyArgon2idAsync` | `ComputeHash` (private, dùng chung với `Hash`/`Verify`) |
| `RecoveryKeyGenerator.GenerateCanonical`/`.FormatGrouped`/`.NormalizeUtf8` (pure, unit test được) | `RecoveryKeyGenerator.cs` | `AuthCoordinator.*` | `RandomNumberGenerator.GetBytes` (chỉ `GenerateCanonical`) |
| `RateLimitPolicy.DelayMsFor` (pure, unit test được) | `RateLimitPolicy.cs` | `AuthCoordinator.RegisterFailureAsync` | — |
| `MonotonicClock.UtcNowUnixMs` (`virtual` — CHỈ để `FakeMonotonicClock` test-double override, production luôn dùng chính class này) | `MonotonicClock.cs` | `AuthCoordinator.*` | `Environment.TickCount64` |
| `AuthDataStore.Exists`/`.TryLoad`/`.Save` (unit test được) | `AuthDataStore.cs` (+ DTO trong `AuthData.cs`) | `AuthCoordinator.*` | `DataProtectionHelper.Protect/Unprotect` (Service/Data), `JsonSerializer` |
| `PendingSetup.IsExpired`/`.TokenMatches` (**sửa Đợt 3 fix — FAIL 1, ADR-83**: bỏ field `RecoveryKeyPlaintextUtf8Pinned`/`.ZeroSecrets` — pending chỉ còn giữ hash), `AuthState` (container RAM-only, không tự khoá) + `AuthState.DecoySalt` (**mới — FAIL 3, ADR-84**: 16 byte sinh 1 lần lúc ctor, RAM-only) | `AuthState.cs` | `AuthCoordinator.*` | `CryptographicOperations.FixedTimeEquals`, `RandomNumberGenerator.GetBytes` (`DecoySalt`) |
| `AuthCoordinator.HandleAsync` (dispatcher theo `BodyOneofCase`, reset `_pendingAfterSendCleanup`) + 6 `Handle*Async` private (`AuthStatusQuery`/`SetInitialPassword`/`ConfirmRecoveryKeySaved`/`AuthVerify`/`ChangePassword`/`RecoveryReset`) | `AuthCoordinator.cs` | `UiSessionServer.RunConnectionAsync` | `Argon2idHasher.*`, `RecoveryKeyGenerator.*`, `AuthDataStore.*`, `RateLimitPolicy.DelayMsFor`, `CredentialBytes.*`, `AuditLogWriter.AppendAsync`, `MonotonicClock.UtcNowUnixMs`, `RunDecoyArgon2idAsync` (**mới**) |
| `AuthCoordinator.ZeroRecoveryKeyPlaintextAfterSend` (**mới Đợt 3 fix — FAIL 1, ADR-83**: `internal`, tiêu thụ `_pendingAfterSendCleanup` qua `Interlocked.Exchange`) | `AuthCoordinator.cs` | `UiSessionServer.RunConnectionAsync` (ngay sau `IpcFrameTransport.WriteFrameAsync`, trong `finally`) | `CryptographicOperations.ZeroMemory`, `CredentialBytes.Zero`/`UnsafeGetBuffer` (qua closure đã set ở `Handle*Async`) |
| `AuthCoordinator.RunDecoyArgon2idAsync` (**mới Đợt 3 fix — FAIL 3, ADR-84**: KHÔNG bọc `Task.Run` — khớp threading nhánh verify thật) | `AuthCoordinator.cs` | `HandleAuthVerifyAsync`/`HandleChangePasswordAsync`/`HandleRecoveryResetAsync` (nhánh `_dataCorrupt \|\| _cached is null`) | `Argon2idHasher.ComputeAndDiscard` |
| `AuthCoordinator.IsLockedOut`/`.RegisterFailureAsync`/`.ResetRateLimitAsync`/`.GenerateNewRecoveryKeyEntry` (**sửa** — trả `byte[] PlaintextUtf8Pinned` thay vì `string Grouped`, ADR-83)/`.NewResponse` (private) | `AuthCoordinator.cs` | các `Handle*Async` ở trên | như trên |

### `src/ParentalGuard.Service/Ipc/`, `Security/`, `Configuration/`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `UiSessionServer.Start`/`.StopAsync`/`.RunLoopAsync`/`.HandshakeAsync`/`.RunConnectionAsync`/`.VerifyClientIdentity` (**mới**; `.RunConnectionAsync` **sửa Đợt 3 fix — FAIL 1, ADR-83**: gọi `AuthCoordinator.ZeroRecoveryKeyPlaintextAfterSend` trong `finally` ngay sau `WriteFrameAsync`) | `Ipc/UiSessionServer.cs` | `Worker.StartUiSessionServer`/`.StopAllAsync` | `PipeAclFactory.CreateUiServerInstance`, `PipeIdentityInterop.GetNamedPipeClientProcessId`, `IpcFrameTransport.ReadFrameAsync/WriteFrameAsync`, `IpcEnvelope.NewEnvelope`, `AuthCoordinator.HandleAsync`, `AuthCoordinator.ZeroRecoveryKeyPlaintextAfterSend` (**mới**), `AuditLogWriter.AppendAsync` |
| `PipeAclFactory.CreateUiServerInstance` (**mới**) | `Security/PipeAclFactory.cs` | `UiSessionServer.RunLoopAsync` | `NamedPipeServerStreamAcl.Create` (BCL) — ACL `NT AUTHORITY\INTERACTIVE` + SYSTEM (Architecture/03 mục 2.2, khác Vision/Overlay dùng SID cụ thể), KHÔNG áp Mandatory Label Low (Architecture/06 mục 2.5 — UI chạy IL bình thường) |
| `UiSessionServer.RunLoopAsync` (sửa **2026-09-20 audit fix**: thêm `ConfigLoadException` vào danh sách catch — lưới an toàn tầng ngoài, phòng khi 1 coordinator domain để lọt exception này thay vì tự bắt tại chỗ như `PauseCoordinator.TryPersist`) | `Ipc/UiSessionServer.cs` | `Start` (qua `Task.Run`) | `PipeAclFactory.CreateUiServerInstance`, `VerifyClientIdentity`, `HandshakeAsync`, `RunConnectionAsync` |
| `InstallPaths.UiExecutablePath` (**mới**) | `Configuration/InstallPaths.cs` | `Worker.StartUiSessionServer` (truyền `UiSessionServer` làm `expectedExecutablePath`) | — |
| `Worker.StartUiSessionServer` (**mới**) | `Worker.cs` | `Worker.ExecuteAsync` | `MonotonicClock` ctor, `AuthCoordinator` ctor, `UiSessionServer` ctor + `.Start` |
| `Worker.StopAllAsync` (sửa Đợt 3 — thêm `_uiSessionServer.StopAsync()` đầu hàm) | `Worker.cs` | `Worker.ExecuteAsync` | thêm: `UiSessionServer.StopAsync` |

### Khoảng trống đã biết — Đợt 3 (báo cáo lại, không tự quyết định)

- ~~GAP hành vi khi `auth.dat` corrupt~~ — **ĐÃ ĐÓNG (2026-09-20)**: chính thức hoá vào
  `Architecture/08-password-authentication-architecture.md` v0.2.0 mục 7.8 + ADR-81. Xác nhận hành vi
  hiện tại của `AuthCoordinator` (coi corrupt = "đã configured", mọi verify trả "sai" đồng nhất, không
  tự sửa/wipe/regenerate) là đúng/an toàn theo fail-secure `Architecture/01` mục 5; xác nhận KHÔNG
  phải gap WHAT (tương đương kịch bản "mất cả mật khẩu lẫn Recovery Key" đã được
  `Specification/06-password-management-spec.md` mục 5 + `ANTI-020`/`040`/`041` chấp nhận đánh đổi từ
  trước). 1 residual risk chưa implement được ghi nhận minh bạch (audit log/Toast cho sự kiện corrupt
  — không bắt buộc, tech debt nhẹ để Đợt sau).
- **`UiSessionServer.VerifyClientIdentity`** chỉ implement điều kiện 3a (đường dẫn cài đặt) của
  Architecture/03 mục 4.2 — điều kiện 3b (chữ ký code-signing) chưa có, giống hệt gap đã ghi nhận sẵn
  ở `ChildProcessSupervisor.VerifyClientIdentity` (Đợt 9, `SEC-030`/`DEV-012`).
- ~~"Chữ ký rỗng" của khung `Hello` đầu tiên từ UI~~ — **ĐÃ ĐÓNG (2026-09-20)**: cụ thể hoá vào
  `Architecture/03-ipc-communication.md` v0.5.1 mục 5.3 + ADR-82 — xác nhận khoá HMAC hằng số
  32-byte-zero là lựa chọn kỹ thuật hợp lý (giữ `IpcFrameTransport` chỉ 1 định dạng frame duy nhất;
  an toàn vì xác thực danh tính thật của `UI` đã xảy ra ở mục 4.2 trước khi đọc `Hello`). Không đổi
  code/ý nghĩa ADR-19.
- **Chưa có test tích hợp qua Named Pipe thật** cho `UiSessionServer` (chỉ có unit test cho
  `AuthCoordinator.HandleAsync` — đúng production code path business logic, nhưng KHÔNG đi qua
  `PipeAclFactory.CreateUiServerInstance`/handshake ephemeral key/`VerifyClientIdentity` thật). Lý do:
  `ParentalGuard.UI` (Đợt 6) chưa tồn tại nên không có client thật để test end-to-end; `VerifyClientIdentity`
  so khớp đường dẫn exe cụ thể nên test tự-kết-nối (test host tự làm "UI giả") cần thêm seam
  (constructor cho phép override `expectedExecutablePath` — đã có sẵn, chưa viết test tận dụng nó ở
  lượt này). Đề xuất bổ sung ở Đợt 6 khi có `ParentalGuard.UI` thật để test round-trip đầy đủ.
- **Benchmark Argon2id thực tế** (`Argon2Params.Official`, `m=32768 KiB, t=2, p=2`) trên máy sandbox
  hiện tại (không phải máy yếu/cũ đại diện người dùng thật): 5 lần đo ~47-161ms/lần (Release build,
  JIT đã warm-up) — THẤP HƠN cả 2 mốc ước tính (150-800ms theo gợi ý nhiệm vụ, 300-1500ms theo ngân
  sách UX Architecture/08 mục 4.2). Không phải dấu hiệu bất thường xấu (nhanh hơn ngân sách là tốt),
  nhưng máy sandbox không đại diện "máy yếu/cũ" mục tiêu thật của `PWD-011` — vẫn cần chủ dự án tự đo
  lại trên máy Windows thật cấu hình thấp trước khi coi ngân sách UX đã được xác nhận đầy đủ (cùng
  tinh thần khoảng trống `IMG-040`/`041` đã ghi ở Đợt 1/2 — sandbox không thay thế được máy thật).

## Đợt 4 (Architecture/09-anti-tamper-architecture.md) — Dual Watchdog, Custom Uninstaller, Anti-Tamper

### `src/ParentalGuard.Ipc/` (schema + hạ tầng dùng chung Service ↔ Watchdog ↔ Uninstaller)

| Thay đổi | File | Ghi chú |
|---|---|---|
| `ProcessType.UNINSTALLER=6` (bỏ ghi chú "reserved" trên `WATCHDOG=4`), `WatchdogReportEvent`(100)/`Ack`(101) + `WatchdogEventType`, `UninstallExecuteRequest`(120)/`Response`(121) + `UninstallResult` | `Protos/ipc.proto` | ANTI-010/020, Architecture/09 mục 8.3 |
| `IpcProtocol.WatchdogPipeKey` (**mới** — khoá HMAC hằng số 32-byte-zero, dùng SUỐT kết nối, không ephemeral) | `IpcProtocol.cs` | `WatchdogSessionServer.*` (Service), `WatchdogPeerConnection.*` (Watchdog) — mục 3.2: ACL SYSTEM-only + `Hello.process_type` đã đủ, không cần lớp HMAC ephemeral |
| `SlidingWindowCounter.RecordEventAndCheckThreshold` (pure, unit test được) | `Tamper/SlidingWindowCounter.cs` | `AttackPatternCoordinator.RecordServiceSideEvent` (Service), `WatchdogPeerConnection.RecoverServiceAsync` (Watchdog) — 2 instance ĐỘC LẬP | — |
| `ScProcessRunner.RunAsync` | `Tamper/ScProcessRunner.cs` | `ScFailureConfigurator.ConfigureAsync`, `PeerServiceRecovery.RecoverAsync`, `UninstallCoordinator.TryDeleteServiceRegistrationAsync` | `Process` (BCL, spawn `sc.exe`) |
| `ScFailureConfigurator.ConfigureAsync` (ANTI-011) | `Tamper/ScFailureConfigurator.cs` | `Worker.ApplyScRecoveryOptionsBestEffortAsync` (Service), `Watchdog.Worker.ApplyScRecoveryOptionsBestEffortAsync` | `ScProcessRunner.RunAsync` |
| `PeerServiceRecovery.RecoverAsync`/`.ResolvePeerBinaryPath` (unit test được)/`.BuildCreateArguments` (unit test được) (ADR-88) | `Tamper/PeerServiceRecovery.cs` | `WatchdogSessionServer.RecoverWatchdogAsync` (Service), `WatchdogPeerConnection.RecoverServiceAsync` (Watchdog) | `ServiceController` (BCL), `Process.GetProcessesByName`, `ScProcessRunner.RunAsync` |
| `RegistryTamperInterop.*` (P/Invoke `advapi32`) | `Tamper/RegistryTamperInterop.cs` | `RegistryStartValueWatcher.*` | Win32 `RegOpenKeyEx`/`RegNotifyChangeKeyValue`/`RegQueryValueEx`/`RegSetValueEx`/`RegCloseKey` |
| `RegistryStartValueWatcher.Start`/`.Run`/`.CheckAndSelfHeal` (ANTI-031, integration-test được qua overload root-hive-tuỳ-ý) | `Tamper/RegistryStartValueWatcher.cs` | `Worker.StartRegistryTamperWatchersBestEffort` (Service, 2 instance: key Service + key Watchdog), `Watchdog.Worker.StartRegistryTamperWatchersBestEffort` (2 instance tương tự) | `RegistryTamperInterop.*` |
| `RestartedByWatchdogDetector.WasRestartedByWatchdog` (pure, unit test được) | `Tamper/RestartedByWatchdogDetector.cs` | `Worker.LogSelfRestartedByWatchdogBestEffortAsync` (Service), `Watchdog.Worker.ExecuteAsync` | — |
| `ParentalGuard.Ipc.csproj` đổi `TargetFramework` `net10.0`→`net10.0-windows` (**sửa**) + `System.ServiceProcess.ServiceController` package mới | `ParentalGuard.Ipc.csproj` | — | `Tamper/*` cần Win32-only API (registry P/Invoke, `ServiceController`) — mọi consumer thật vốn đã `-windows` |

### `src/ParentalGuard.Service/` — pipe mới, uninstaller, rate-limit/banner

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `PipeAclFactory.CreateWatchdogServerInstance` (**mới** — Allow SYSTEM only, Deny cả INTERACTIVE) | `Security/PipeAclFactory.cs` | `WatchdogSessionServer.RunLoopAsync` | `NamedPipeServerStreamAcl.Create` |
| `WfpInterop.FwpmFilterDeleteByKey0`/`.FwpmSubLayerDeleteByKey0`/`.FwpmProviderDeleteByKey0` (**mới**) | `Security/WfpInterop.cs` | `WfpVisionBlocker.Remove` | `fwpuclnt.dll` |
| `WfpVisionBlocker.Remove` (**mới**, best-effort/idempotent) | `Security/WfpVisionBlocker.cs` | `UninstallCoordinator.ExecuteAsync` (bước 3, mục 5.5) | `WfpInterop.*DeleteByKey0` |
| `KnownFolderInterop.SHGetKnownFolderPath` (**mới**) | `Security/KnownFolderInterop.cs` | `DesktopAuditLogExporter.TryExport` | `shell32.dll` |
| `DesktopAuditLogExporter.TryExport` (ADR-97) | `Tamper/DesktopAuditLogExporter.cs` | `UninstallCoordinator.ExecuteAsync` (bước 2) | `SessionInterop.WTSQueryUserToken`, `KnownFolderInterop.SHGetKnownFolderPath` |
| `AuthCoordinator.TryConsumeActionTokenAsync` (**mới** — dùng 1 lần, xoá khỏi `PendingActionTokens`) | `Auth/AuthCoordinator.cs` | `UninstallCoordinator.HandleAsync` | `AuthState.PendingActionTokens` (qua `_gate`) |
| `UninstallCoordinator.HandleAsync`/`.ExecuteAsync`/`.ConsumeShutdownRequested` (mục 5.5, bất biến an toàn ADR-98 thực thi Ở PHÍA SERVICE: chỉ trả `SUCCESS` sau khi đã thực thi xong) | `Tamper/UninstallCoordinator.cs` | `UninstallerSessionServer.RunConnectionAsync` | `AuthCoordinator.TryConsumeActionTokenAsync`, `WatchdogSessionServer.SuppressRecovery` (**mới** — gọi ĐẦU TIÊN trong `ExecuteAsync`, chặn race ADR-95 chiều Service→Watchdog: nếu không, pipe vỡ do chính bước dừng Watchdog dưới đây sẽ khiến `WatchdogSessionServer` hiểu nhầm "Watchdog mất tích" và tự khởi động lại nó giữa uninstall), `AuditLogWriter.AppendAsync`, `DesktopAuditLogExporter.TryExport`, `WfpVisionBlocker.Remove`, `ServiceController` (dừng Watchdog, ADR-95 — TRƯỚC bước tự thoát), `ScProcessRunner` (`sc delete` ×2), xoá file `%ProgramData%` |
| `WatchdogSessionServer.SuppressRecovery` (**mới**) | `Ipc/WatchdogSessionServer.cs` | `UninstallCoordinator.ExecuteAsync` | set `_recoverySuppressed` — `RunLoopAsync` catch-block đọc cờ này, bỏ qua `RecoverWatchdogAsync` + thoát loop nếu đang uninstall |
| `UninstallerSessionServer.Start`/`.RunLoopAsync`/`.HandshakeAsync`/`.RunConnectionAsync` (whitelist CHỈ `AuthVerifyReq`/`UninstallExecuteReq`) | `Ipc/UninstallerSessionServer.cs` | `Worker.StartUninstallerSessionServer`/`.StopAllAsync` | `PipeAclFactory.CreateUiServerInstance` (tái dùng — ACL giống hệt UI), `AuthCoordinator.HandleAsync`, `UninstallCoordinator.HandleAsync`/`.ConsumeShutdownRequested`, `IHostApplicationLifetime.StopApplication` (qua `requestServiceShutdown` delegate, bước 9 ADR-95) |
| `WatchdogSessionServer.Start`/`.RunLoopAsync`/`.HandshakeAsync`/`.RunConnectionAsync`/`.HeartbeatPingLoopAsync`/`.RecoverWatchdogAsync`/`.HandleWatchdogReportEventAsync` (heartbeat 3s/3-miss, ADR-87/88) | `Ipc/WatchdogSessionServer.cs` | `Worker.StartWatchdogSessionServer`/`.StopAllAsync` | `PipeAclFactory.CreateWatchdogServerInstance`, `PeerServiceRecovery.RecoverAsync`, `AuditLogWriter.AppendAsync`, `AttackPatternCoordinator.RecordWatchdogSideThresholdExceeded` |
| `AttackPatternCoordinator.RecordServiceSideEvent`/`.RecordWatchdogSideThresholdExceeded` (ANTI-060, N=5/T=30 phút) | `Tamper/AttackPatternCoordinator.cs` | `Worker` (hook `ChildProcessSupervisor.onChildRestarted`, `HandleVisionNetworkBlockedAsync`, `OnRegistryTamperDetectedAndRestored`), `WatchdogSessionServer.HandleWatchdogReportEventAsync` | `SlidingWindowCounter.RecordEventAndCheckThreshold`, `AttackBannerTimer.Arm`, `AuditLogWriter.AppendAsync` |
| `AttackBannerTimer.Arm` (unit test được — edge-triggered, trả `true` đúng 1 lần/đợt) | `Tamper/AttackBannerTimer.cs` | `AttackPatternCoordinator.*` | `Timer` (BCL), callback → `IconStatusCoordinator.SetAttackBannerActive` |
| `IconStatusCoordinator.SetAttackBannerActive` (**mới**, ANTI-061/ADR-100 — giữ ERROR, không tạo state/message IPC mới) | `Ipc/IconStatusCoordinator.cs` | `AttackBannerTimer` callback (qua `Worker`) | `ChildProcessSupervisor.TryEnqueueBusinessMessage` |
| `ChildProcessSupervisor` tham số `onChildRestarted` (**mới**) | `Ipc/ChildProcessSupervisor.cs` | `Worker` (Vision + Overlay supervisor ctor) | gọi ngay sau ghi audit `ProcessRestarted` |
| `Worker.ApplyScRecoveryOptionsBestEffortAsync`/`.LogSelfRestartedByWatchdogBestEffortAsync`/`.StartWatchdogSessionServer`/`.StartUninstallerSessionServer`/`.StartRegistryTamperWatchersBestEffort`/`.OnRegistryTamperDetectedAndRestored` (**mới**), ctor nhận thêm `IHostApplicationLifetime`/`StartupArgs` | `Worker.cs` | `Worker.ExecuteAsync` | như liệt kê ở trên |
| `StartupArgs` (record, **mới**) | `Worker.cs` | `Program.cs` (đăng ký DI từ `Main(args)`) | — |
| `InstallPaths.WatchdogExecutablePath`/`.UninstallerExecutablePath`/`.ServiceRegistryKeyPath`/`.WatchdogRegistryKeyPath`/`.ServiceName`/`.WatchdogServiceName`/`.ServiceDisplayName`/`.WatchdogDisplayName` (**mới**) | `Configuration/InstallPaths.cs` | `Worker.*`, `WatchdogSessionServer.*` | — |

### `src/ParentalGuard.Watchdog/` (mới — Windows Service #2, ADR-86 tối giản)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| top-level `Program` | `Program.cs` | entry point (SCM) | `AddWindowsService`, `Worker` (DI) |
| `Worker.ExecuteAsync`/`.ApplyScRecoveryOptionsBestEffortAsync`/`.StartRegistryTamperWatchersBestEffort` | `Worker.cs` | Generic Host (`IHostedService`) | `ScFailureConfigurator.ConfigureAsync`, `RegistryStartValueWatcher` ×2, `WatchdogPeerConnection.RunForeverAsync`, `RestartedByWatchdogDetector.WasRestartedByWatchdog` |
| `WatchdogPeerConnection.RunForeverAsync`/`.ConnectWithRetryAsync`/`.HandshakeAsync`/`.RunConnectionAsync`/`.ReaderLoopAsync`/`.HangMonitorAsync`/`.RecoverServiceAsync` (mục 3.3 — CẢ 2 hướng phát hiện: pipe vỡ NGAY LẬP TỨC qua exception, HOẶC hết 9s không có `HeartbeatPing` mới qua `HangMonitorAsync`) | `WatchdogPeerConnection.cs` | `Worker.ExecuteAsync` | `IpcFrameTransport.*`, `PeerServiceRecovery.RecoverAsync`, `SlidingWindowCounter.RecordEventAndCheckThreshold` |
| `WatchdogPaths.*` (hằng số — CỐ Ý không tham chiếu `ParentalGuard.Service.Configuration.InstallPaths`, ADR-86) | `WatchdogPaths.cs` | `Worker.*`, `WatchdogPeerConnection.RecoverServiceAsync` | — |

### `src/ParentalGuard.Uninstaller/` (mới — 7th executable, `requireAdministrator`)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| top-level `Program.Main` ([STAThread]) | `Program.cs` | entry point (OS, sau UAC — lớp gate #1) | `UninstallPipeClient`/`LocalCleanup`/`UninstallFlow` ctor, `Application.Run` |
| `UninstallFlow.RunAsync` (BẤT BIẾN AN TOÀN CỐT LÕI ADR-98, unit test được đầy đủ qua fake — `UninstallFlowTests`) | `UninstallFlow.cs` | `Program.RunFlowAsync` | `IUninstallServiceConnection.*` (qua `UninstallPipeClient` production), `ILocalCleanup.CleanupAsync` (qua `LocalCleanup` production — CHỈ gọi khi `UninstallResult.Success`) |
| `UninstallPipeClient.ConnectAsync`/`.VerifyPasswordAsync`/`.ExecuteUninstallAsync` (implements `IUninstallServiceConnection`) | `UninstallPipeClient.cs` | `UninstallFlow.RunAsync` | `IpcFrameTransport.*` (khoá phiên ephemeral qua `Hello` chưa ký, giống UI) |
| `LocalCleanup.CleanupAsync` (mục 5.5 bước 10-13, implements `ILocalCleanup`) | `LocalCleanup.cs` | `UninstallFlow.RunAsync` (CHỈ sau `SUCCESS`) | `Process.GetProcessesByName` (chờ Service thoát), `RegistryDeleteInterop.RegDeleteTree`, `MoveFileExInterop.MoveFileEx` (self-delete `DELAY_UNTIL_REBOOT`) |
| `PasswordPromptForm`/`ConfirmUninstallForm` (WinForms, code-only — cùng convention `ContentBlurOverlayForm`) | `Forms/*.cs` | `Program.PromptPasswordAsync`/`.ConfirmProceedAsync` | — |

### Khoảng trống đã biết — Đợt 4 (báo cáo lại, không tự quyết định)

- **`WatchdogSessionServer`/`UninstallerSessionServer.VerifyClientIdentity`** chỉ implement điều kiện 3a
  (đường dẫn cài đặt) của Architecture/03 mục 4.2 — điều kiện 3b (chữ ký code-signing) chưa có, cùng gap
  đã ghi nhận ở `ChildProcessSupervisor`/`UiSessionServer` (Đợt 9, `SEC-030`/`DEV-012`).
- **Chưa có test tích hợp qua Named Pipe/SCM thật** cho `WatchdogSessionServer`↔`WatchdogPeerConnection`
  và `UninstallerSessionServer`↔`UninstallPipeClient` (chỉ unit test business logic qua fake/HKCU) — lý
  do: sandbox dev không chạy dưới SYSTEM, không có `ParentalGuard.Service`/`Watchdog` cài đặt thật dưới
  SCM để test round-trip đầy đủ (mục tiêu thật cần máy Windows thật, cùng tinh thần khoảng trống
  `IMG-040`/`041`/benchmark Argon2id đã ghi ở Đợt 1-3).
- **Mục 3.6 (`--restarted-by-watchdog` self-detection qua SCM start-args)**: `Worker`/`Watchdog.Worker`
  đọc `startupArgs` (từ `Main(args)`) làm nguồn PHỤ (best-effort) — GHI CHÚ TRIỂN KHAI (không phải gap
  WHAT): `ServiceController.Start(string[] args)`/SCM start-args KHÔNG được `Microsoft.Extensions.Hosting.WindowsServices`
  (`WindowsServiceLifetime : ServiceBase`) phơi ra `Main(string[] args)`/DI một cách đáng tin cậy trong
  .NET Generic Host — nguồn sự thật CHÍNH cho việc ghi `ProcessRestarted` 2 chiều là bước 5 mục 3.4 (bên
  THỰC HIỆN khôi phục tự báo cáo, đã implement đầy đủ ở `WatchdogSessionServer.RecoverWatchdogAsync` +
  `WatchdogPeerConnection.RecoverServiceAsync`) — không phụ thuộc plumbing SCM args chưa chắc hoạt động.
- **Recovery Options thật (`sc failure`) và `RegistryStartValueWatcher` dưới `HKLM`** chưa verify được
  trên máy Windows thật với quyền SYSTEM (sandbox dev không có) — best-effort try/catch đã có (giống
  `AclProvisioner`/`WfpVisionBlocker` Đợt 0), nhưng hành vi thật cần xác nhận lại ngoài sandbox.

## Đợt 5 (Architecture/02-process-architecture.md mục 3a) — Pause/Resume

### `src/ParentalGuard.Ipc/` (schema)

| Thay đổi | File | Ghi chú |
|---|---|---|
| `PauseMonitoringRequest`(92)/`Response`(93), `ResumeMonitoringRequest`(94)/`Response`(95), `PauseStatusQuery`(96)/`Response`(97) + enum `PauseDuration`/`PauseResult`/`ResumeResult` (giá trị `*Result` đặt tiền tố tên type — bare `SUCCESS`/`INVALID_TOKEN` đã bị `UninstallResult` chiếm, cùng lý do đã áp dụng cho `SetupResult`/`AuthResult`; protoc-gen-csharp tự rút gọn C# thành `PauseResult.Success`/`.InvalidToken`/`.AlreadyPaused`, `ResumeResult.Success`/`.InvalidToken`/`.NotPaused`) | `Protos/ipc.proto` | `PAUSE-001`-`004`, Architecture/03 mục 3.6 |

### `src/ParentalGuard.Service/Pause/` (mới — toàn bộ business logic Pause/Resume, transport-agnostic)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `PauseCoordinator.Start` | `PauseCoordinator.cs` | `Worker.ExecuteAsync` | `StartTick` (nếu boot thẳng vào `Running·Paused`) |
| `PauseCoordinator.HandleAsync` (định tuyến theo `BodyCase`) | `PauseCoordinator.cs` | `UiSessionServer.DispatchAsync` | `HandlePauseAsync`/`HandleResumeAsync`/`HandleStatusQueryAsync` |
| `PauseCoordinator.HandlePauseAsync` (private, mục 3a.1) | `PauseCoordinator.cs` | `HandleAsync` | `ConsumeTokenAsync`, `PauseDurationCalculator.ComputeExpiresAtUnixMs`, `TryPersist`, `ChildProcessSupervisor.TryEnqueueBusinessMessage`/`.SetHeartbeatInterval` (Vision), `OverlayDecisionCoordinator.ClearForPause` (ADR-106), `IconStatusCoordinator.SetState`, `AuditLogWriter.AppendAsync` (`PauseActivated`), `PauseDurationMapper.ToAuditLogValue`, `CheckDailyFrequencyAnomalyAsync`, `StartTick` |
| `PauseCoordinator.HandleResumeAsync` (private, mục 3a.2) | `PauseCoordinator.cs` | `HandleAsync` | `ConsumeTokenAsync`, `ApplyResumeAsync` |
| `PauseCoordinator.HandleStatusQueryAsync` (private) | `PauseCoordinator.cs` | `HandleAsync` | — (đọc `_state` snapshot) |
| `PauseCoordinator.ApplyResumeAsync` (private — dùng chung cho resume thủ công lẫn tự động) | `PauseCoordinator.cs` | `HandleResumeAsync`, `OnTickAsync` (nhánh hết hạn) | `TryPersist` (fail-secure NGƯỢC chiều `HandlePauseAsync` — vẫn resume trong RAM dù ghi lỗi), `ChildProcessSupervisor.TryEnqueueBusinessMessage`/`.SetHeartbeatInterval`, `IconStatusCoordinator.SetState`, `AuditLogWriter.AppendAsync` (`PauseResumed`), `StopTick` |
| `PauseCoordinator.TickLoopAsync`/`.OnTickAsync`/`.StartTick`/`.StopTick` (ADR-102, chu kỳ 30s cố định — KHÔNG `Timer` bắn đúng 1 lần) | `PauseCoordinator.cs` | `Start`, `HandlePauseAsync`, `ApplyResumeAsync`, `OnTickAsync` (tự dừng khi resume) | `ApplyResumeAsync` (khi hết hạn), `SendBannerReminder` (khi đủ 10 phút, ADR-103) |
| `PauseCoordinator.SendBannerReminder` (private, ADR-108 — tái dùng `ShowToastCommand`, không message mới) | `PauseCoordinator.cs` | `OnTickAsync` | `ChildProcessSupervisor.TryEnqueueBusinessMessage` (Overlay) |
| `PauseCoordinator.TriggerTickForTestAsync`/`.LastBannerShownAtUnixMsForTest` (internal, test hook) | `PauseCoordinator.cs` | `tests/PauseCoordinatorTests` | `OnTickAsync` |
| `PauseDurationCalculator.ComputeExpiresAtUnixMs` (pure, unit test được, ADR-105) | `PauseDurationCalculator.cs` | `PauseCoordinator.HandlePauseAsync` | — (`trustedNowUnixMs` truyền vào, không đọc đồng hồ hệ thống trực tiếp — chống bypass đổi giờ) |
| `PauseDurationMapper.ToAuditLogValue` (pure, unit test được qua `PauseCoordinatorTests`) | `PauseDurationMapper.cs` | `PauseCoordinator.HandlePauseAsync` | — (Architecture/04 mục 5.1) |
| `PauseStateRecovery.Decide` (pure, unit test được, mục 3a.3) | `PauseStateRecovery.cs` | `Worker.RecoverPauseStateAtBootAsync` | — |
| `PauseFrequencyGuard.CountPauseActivatedTodayAsync` (`PAUSE-021`, ngưỡng &gt;5/ngày lịch UTC) | `PauseFrequencyGuard.cs` | `PauseCoordinator.CheckDailyFrequencyAnomalyAsync` | đọc trực tiếp `audit.log` (không cache riêng, đúng Architecture/04 mục 3.4) |

### `src/ParentalGuard.Service/Ipc/`, `Data/`, `Worker.cs` (sửa)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `ChildProcessSupervisor.SetHeartbeatInterval` (**mới**, ADR-104 — đổi cadence Vision 1s↔10s NGAY giữa 1 kết nối đang chạy, không chờ reconnect) | `Ipc/ChildProcessSupervisor.cs` | `PauseCoordinator.HandlePauseAsync`/`.ApplyResumeAsync` | `Interlocked.Exchange`/`.Increment` (`_heartbeatIntervalTicks`/`_cadenceGeneration`) |
| `ChildProcessSupervisor.HeartbeatPingLoopAsync` (private, sửa — đọc `_heartbeatIntervalTicks` mỗi vòng lặp thay vì tham số cố định, reset `consecutiveMisses` khi `_cadenceGeneration` đổi) | `Ipc/ChildProcessSupervisor.cs` | `RunConnectionAsync` | như cũ + đọc cadence động |
| `OverlayDecisionCoordinator.ClearForPause` (**mới**, ADR-106) | `Ipc/OverlayDecisionCoordinator.cs` | `PauseCoordinator.HandlePauseAsync` | xoá `_active`/`_mergedModeActive`/`_mergedOverlayIdByMonitor`, `PushCurrentList` |
| `UiSessionServer` ctor (thêm tham số `PauseCoordinator`), `.DispatchAsync` (**mới** — định tuyến `PauseMonitoringReq`/`ResumeMonitoringReq`/`PauseStatusQuery` sang `PauseCoordinator`, còn lại sang `AuthCoordinator`) | `Ipc/UiSessionServer.cs` | `Worker.StartUiSessionServer` (ctor), `RunConnectionAsync` (`DispatchAsync`) | `PauseCoordinator.HandleAsync`, `AuthCoordinator.HandleAsync` |
| `ConfigDb.UpdatePauseState` (**mới** — `UPDATE` khác `InsertPauseState` chỉ dùng lúc `CreateFresh`; **sửa 2026-09-20 audit fix**: bọc `ExecuteNonQuery` trong try/catch `SqliteException`→`ConfigLoadException`, trước đó lọt nguyên bản ra ngoài) | `Data/ConfigDb.cs` | `PauseCoordinator.TryPersist`, `Worker.RecoverPauseStateAtBootAsync` | `DataProtectionHelper.Protect` |
| `ConfigDb.Open(string, int?)` (internal overload, **mới 2026-09-20 audit fix**) | `Data/ConfigDb.cs` | `ConfigDb.Open(string)` (public 1-arg, luôn truyền `null` — hành vi production không đổi), `ConfigDbTests` (test regression lock-contention, dùng `SqliteConnectionStringBuilder.DefaultTimeout` thay vì nối chuỗi PRAGMA để tránh CA2100) | `SqliteConnection.Open`, `PRAGMA journal_mode=WAL` |
| `Worker.RecoverPauseStateAtBootAsync` (**mới**, mục 3a.3 — chỉ phần I/O, quyết định thuần ở `PauseStateRecovery.Decide`) | `Worker.cs` | `ExecuteAsync` | `PauseStateRecovery.Decide`, `ConfigDb.Open/.UpdatePauseState`, `AuditLogWriter.AppendAsync` (`PauseResumed`, `trigger="auto_expired_while_offline"`) |
| `Worker.OnChildSessionConnected` (**mới** — thay 2 lambda `SetState(IconState.Active)` cũ của Vision/Overlay `onSessionConnected`, phản ánh đúng `Running·Paused` nếu reconnect trong lúc Pause) | `Worker.cs` | `ChildProcessSupervisor` ctor ×2 (`onSessionConnected`) | `PauseCoordinator.IsPaused`/`.PauseExpiresAtUnixMs`, `IconStatusCoordinator.SetState` |
| `Worker.BuildControlVisionCommand` (sửa — thêm tham số `monitoringEnabled` tách khỏi `MonitoringStateData.MonitoringEnabled`) | `Worker.cs` | `ExecuteAsync` (initial push Vision), `PauseCoordinator` (qua lambda `buildControlVisionCommand` truyền vào ctor) | — |
| `Worker.ExecuteAsync` (sửa — thêm `MonotonicClock` dùng chung Auth+Pause, `RecoverPauseStateAtBootAsync`, cadence Vision khởi tạo 10s nếu boot vào `Running·Paused`, `new PauseCoordinator`, dời `StartUiSessionServer` xuống sau khi `PauseCoordinator` sẵn sàng) | `Worker.cs` | `BackgroundService` (Generic Host) | như bảng Đợt 0 + `RecoverPauseStateAtBootAsync`, `new PauseCoordinator`, `PauseCoordinator.Start` |

### Khoảng trống đã biết — Đợt 5 (báo cáo lại, không tự quyết định)

- **Chưa có test tích hợp qua Named Pipe thật** cho `UiSessionServer` định tuyến `PauseMonitoringRequest`/
  `ResumeMonitoringRequest`/`PauseStatusQuery` (chỉ unit test `PauseCoordinator.HandleAsync` trực tiếp,
  cùng lý do/tinh thần khoảng trống đã ghi ở Đợt 3/4 — sandbox dev không có `ParentalGuard.UI` thật để
  round-trip qua pipe `ParentalGuard.Svc.UI`).
- **`PAUSE-021` — sự kiện audit log `PauseFrequencyAnomalyDetected`** chưa có trong bảng `event_type` ở
  `Architecture/04-data-architecture.md` mục 5.1 (chỉ mới ghi nhận `PauseActivated`/`PauseResumed`) —
  đặt tên theo đúng mẫu hình các event khác (`AuthBruteForceThresholdReached`, `TamperDetected`), cùng
  cách "phát sinh từ code thật, chờ `architecture-writer` bổ sung amendment chính thức" đã áp dụng nhiều
  lần trước đó ở file này (`ForceCloseRequested`/`VisionNetworkBlocked`...). Chi tiết `detail`:
  `{count_today, threshold_per_day}`. Không phải gap WHAT (ngưỡng đã `ĐÃ CHỐT v0.2.2`, hành vi "ghi audit
  log cảnh báo" đã rõ trong chỉ đạo Đợt 5) — thuần thiếu 1 dòng bảng tài liệu.
- **Banner nhắc (`ShowToastCommand`, ADR-108) chưa verify hiển thị thật trên Windows Toast** (chỉ verify
  logic thời điểm gửi qua `PauseCoordinatorTests` — `TryEnqueueBusinessMessage` không có kết nối Overlay
  thật trong test) — cùng nhóm khoảng trống "cần máy Windows thật" đã ghi nhận nhiều lần ở các Đợt trước.

## Đợt 6 (Architecture/10-ui-architecture.md) — `ParentalGuard.UI` giai đoạn 1 (nền tảng)

Phạm vi lượt này: project skeleton, `UiIpcClient` (bảng đã thêm ở mục `src/ParentalGuard.Ipc/` phía
trên), Facade layer (`IAuthFacade` implement đầy đủ, 4 facade còn lại stub rỗng cho giai đoạn 2-4),
`NavigationService`/`LocalizationService`, single-instance, `S1` Onboarding, `S5` Auth Modal,
`App.xaml.cs`. KHÔNG bao gồm `S2`-`S4` đầy đủ (chỉ placeholder "Đang phát triển").

### `src/ParentalGuard.UI/Services/IpcClient/` (Facade layer, ADR-116)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `AuthFacade.GetAuthStatusAsync` | `AuthFacade.cs` | `App.ConnectAndRouteAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `AuthFacade.SetInitialPasswordAsync` | `AuthFacade.cs` | `OnboardingViewModel.SubmitPasswordAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync`, `MapSetInitialPasswordResponse`, `CredentialBytes.UnsafeGetBuffer/.Zero`, `CryptographicOperations.ZeroMemory` |
| `AuthFacade.MapSetInitialPasswordResponse` (private static) | `AuthFacade.cs` | `SetInitialPasswordAsync` | `CredentialBytes.UnsafeGetBuffer` (lấy `recovery_key_plaintext`/`setup_token`) |
| `AuthFacade.ConfirmRecoveryKeySavedAsync` | `AuthFacade.cs` | `OnboardingViewModel.ConfirmRecoveryKeySavedAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `AuthFacade.AuthVerifyAsync` | `AuthFacade.cs` | `AuthPromptViewModel.SubmitAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync`, `MapAuthVerifyResponse`, `CredentialBytes.UnsafeGetBuffer/.Zero`, `CryptographicOperations.ZeroMemory` |
| `AuthFacade.MapAuthVerifyResponse` (private static) | `AuthFacade.cs` | `AuthVerifyAsync` | `CredentialBytes` (nếu cần), `resp.ActionToken.ToByteArray` |
| `ConfigFacade` (stub rỗng, **CHƯA có method** — placeholder DI cho `S4`, giai đoạn 4) | `ConfigFacade.cs` | đăng ký DI ở `App.BuildServiceProvider`, chưa ai gọi | — |
| `AuditFacade.GetAuditLogAsync`/`.MapResponse` (**mới giai đoạn 3**) | `AuditFacade.cs` | `AuditLogViewModel.LoadPageAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `AuditFacade.MarkFalsePositiveAsync`/`.MapMarkFalsePositiveResponse` (**mới giai đoạn 3**) | `AuditFacade.cs` | `AuditLogViewModel.MarkFalsePositiveWithTokenAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `DashboardFacade.GetStatusAsync` (**mới giai đoạn 2**) | `DashboardFacade.cs` | `DashboardViewModel.PollAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` ×2 liên tiếp (`DashboardStatusQuery` rồi `PauseStatusQuery`, mục 3.3/ADR-120) |
| `DashboardFacade.AcknowledgePauseAnomalyAsync` (**mới giai đoạn 2**) | `DashboardFacade.cs` | `DashboardViewModel.AcknowledgeAnomalyAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `DashboardFacade.GetAuditChartAsync` (**mới giai đoạn 2**) | `DashboardFacade.cs` | `DashboardViewModel.LoadChartAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `PauseFacade.PauseMonitoringAsync`/`.MapDuration`/`.MapPauseResponse` (**mới giai đoạn 2**) | `PauseFacade.cs` | `DashboardViewModel.PauseAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |
| `PauseFacade.ResumeMonitoringAsync`/`.MapResumeResponse` (**mới giai đoạn 2**) | `PauseFacade.cs` | `DashboardViewModel.ResumeAsync` | `UiIpcClient.NewEnvelope/SendRequestAsync` |

### `src/ParentalGuard.UI/Services/` (NavigationService, LocalizationService, SingleInstanceGuard)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `NavigationService.Initialize` | `NavigationService.cs` | `App.OnLaunched` | — (lưu `Frame` root) |
| `NavigationService.NavigateToConnectionError`/`.NavigateToOnboarding`/`.NavigateToMainShell` | `NavigationService.cs` | `App.ConnectAndRouteAsync`, `Views/OnboardingPage.*` | `Frame.Navigate` (BCL) |
| `NavigationService.NavigateToRecovery` (**implement thật, giai đoạn 5 (`S6`)** — trước là `throw NotImplementedException` tạm) | `NavigationService.cs` | `ShowAuthPromptAsync` (khi `AuthPromptDialog.ForgotPasswordRequested`), `Views/SettingsPage.OnForgotOldPasswordClick` | `Frame.Navigate` (→ `Views/RecoveryPage`) |
| `NavigationService.ShowAuthPromptAsync` (`S5`, mục 6.5, implement `IAuthPromptService`) | `NavigationService.cs` | `DashboardViewModel.PauseAsync`/`.ResumeAsync`/`.PauseWithTokenAsync`/`.ResumeWithTokenAsync` (giai đoạn 2), `AuditLogViewModel.InitializeAsync`/`.MarkFalsePositiveAsync`/`.MarkFalsePositiveWithTokenAsync` (**mới giai đoạn 3**, qua interface `IAuthPromptService`), `S4` (giai đoạn sau) | `Views/AuthPromptDialog` ctor + `.RequestActionTokenAsync`, `NavigateToRecovery` (nếu bấm "Quên mật khẩu?") |
| `IAuthPromptService` (**mới**, `Services/IAuthPromptService.cs` — seam test-only, sửa gap `test-runner` 2026-09-24) | — (interface) | `NavigationService` (implement thật), `tests/ParentalGuard.UI.Tests/DashboardViewModelTests.cs` (`FakeAuthPromptService`, fake token/CallCount) |
| `LocalizationService.Get`/`.GetFormatted` | `LocalizationService.cs` | mọi `Views/*.xaml.cs`, `ViewModels/*.cs` | `ResourceManager.GetString` (BCL) |
| `SingleInstanceGuard` ctor/`.Dispose` | `SingleInstanceGuard.cs` | `App.OnLaunched`, `App.OnWindowClosed` | `Mutex` (BCL) |
| `SingleInstanceGuard.ActivateExistingInstance` (static) | `SingleInstanceGuard.cs` | `App.OnLaunched` (khi `IsFirstInstance=false`) | `FindWindow`/`SetForegroundWindow` (P/Invoke `user32`) |

### `src/ParentalGuard.UI/ViewModels/`, `Views/` (`S1` Onboarding, `S5` Auth Modal, Main Shell)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `OnboardingViewModel.AcknowledgeIntro` (`[RelayCommand]`) | `OnboardingViewModel.cs` | `Views/OnboardingWelcomePage.xaml` (`Command` binding) | raises `NavigateToSetPasswordRequested` |
| `OnboardingViewModel.SubmitPasswordAsync` | `OnboardingViewModel.cs` | `Views/OnboardingSetPasswordPage.OnContinueClick` | `AuthFacade.SetInitialPasswordAsync`, raises `NavigateToRecoveryKeyRequested`/`AlreadyConfiguredDetected` |
| `OnboardingViewModel.ConfirmRecoveryKeySavedAsync` | `OnboardingViewModel.cs` | `Views/OnboardingRecoveryKeyPage.OnContinueClick` | `AuthFacade.ConfirmRecoveryKeySavedAsync`, `ZeroRecoveryKeyBuffer`, raises `OnboardingCompleted`/`NavigateBackToSetPasswordRequested` |
| `OnboardingViewModel.ZeroRecoveryKeyBuffer` | `OnboardingViewModel.cs` | `ConfirmRecoveryKeySavedAsync` (mọi nhánh kết quả), `Dispose` | `CryptographicOperations.ZeroMemory` |
| `OnboardingViewModel.Dispose` (**mới, security audit BUG B fix**) | `OnboardingViewModel.cs` | `App.OnWindowClosed` (safety net qua `App._activeOnboardingViewModel`) | `ZeroRecoveryKeyBuffer` (idempotent — no-op nếu buffer đã null) |
| `AuthPromptViewModel.SubmitAsync` | `AuthPromptViewModel.cs` | `Views/AuthPromptDialog.OnPrimaryButtonClick` | `AuthFacade.AuthVerifyAsync` |
| `Views/OnboardingPage.OnLoaded` | `Views/OnboardingPage.xaml.cs` | `Page.Loaded` (XAML event) | `new OnboardingViewModel`, `App.RegisterActiveOnboardingViewModel` (**mới, BUG B fix**), `StepFrame.Navigate` (→ `OnboardingWelcomePage`), đăng ký 5 event handler của `OnboardingViewModel` |
| `Views/OnboardingPage.OnAlreadyConfiguredAsync` | `Views/OnboardingPage.xaml.cs` | `OnboardingViewModel.AlreadyConfiguredDetected` (event) | `ContentDialog.ShowAsync`, `GoToMainShell` |
| `Views/OnboardingPage.GoToMainShell` (**sửa, BUG B fix**) | `Views/OnboardingPage.xaml.cs` | `OnAlreadyConfiguredAsync`, `OnboardingViewModel.OnboardingCompleted` (event, đăng ký trong `OnLoaded`) | `App.UnregisterActiveOnboardingViewModel`, `NavigationService.NavigateToMainShell` |
| `Views/OnboardingSetPasswordPage.OnContinueClick` | `Views/OnboardingSetPasswordPage.xaml.cs` | nút "Tiếp tục" (XAML event) | `OnboardingViewModel.SubmitPasswordAsync` (byte[] pinned từ `PasswordBox.Password`) |
| `Views/OnboardingSetPasswordPage.ComputeStrengthLabel` (private static, `PWD-002` thuần UX) | `Views/OnboardingSetPasswordPage.xaml.cs` | `OnPasswordChanged` | `LocalizationService.Get` |
| `Views/OnboardingRecoveryKeyPage.OnCopyClick` | `Views/OnboardingRecoveryKeyPage.xaml.cs` | nút "Sao chép" (XAML event) | `Clipboard.SetContent` (WinRT) |
| `Views/OnboardingRecoveryKeyPage.OnContinueClick` | `Views/OnboardingRecoveryKeyPage.xaml.cs` | nút "Tiếp tục" (XAML event) | `OnboardingViewModel.ConfirmRecoveryKeySavedAsync` |
| `Views/AuthPromptDialog.RequestActionTokenAsync` | `Views/AuthPromptDialog.xaml.cs` | `NavigationService.ShowAuthPromptAsync` | `ContentDialog.ShowAsync` |
| `Views/AuthPromptDialog.OnPrimaryButtonClick` | `Views/AuthPromptDialog.xaml.cs` | `ContentDialog.PrimaryButtonClick` (XAML event) | `AuthPromptViewModel.SubmitAsync` (byte[] pinned từ `PasswordBox.Password`), `Hide` (nếu SUCCESS) |
| `Views/AuthPromptDialog.OnForgotPasswordClick` | `Views/AuthPromptDialog.xaml.cs` | `HyperlinkButton.Click` (XAML event) | set `ForgotPasswordRequested=true`, `Hide` |
| `Views/MainShellPage.OnSelectionChanged` | `Views/MainShellPage.xaml.cs` | `NavigationView.SelectionChanged` (XAML event) | `ContentFrame.Navigate` (→ `DashboardPage`/`AuditLogPage`/`SettingsPage`, placeholder "Đang phát triển" giai đoạn này) |
| `Views/ConnectionErrorPage.OnRetryClick` | `Views/ConnectionErrorPage.xaml.cs` | nút "Thử lại" (XAML event) | `App.RetryConnectAsync` |

### `src/ParentalGuard.UI/App.xaml.cs`, `MainWindow.xaml.cs`

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `App.OnLaunched` | `App.xaml.cs` | Windows App SDK (entry point) | `SingleInstanceGuard` ctor/`.ActivateExistingInstance`, `BuildServiceProvider`, `new MainWindow`, `NavigationService.Initialize`, `ConnectAndRouteAsync` |
| `App.ConnectAndRouteAsync` (private) | `App.xaml.cs` | `OnLaunched`, `RetryConnectAsync` | `UiIpcClient.ConnectAsync`, `AuthFacade.GetAuthStatusAsync`, `NavigationService.NavigateToMainShell/.NavigateToOnboarding/.NavigateToConnectionError` |
| `App.RetryConnectAsync` (public) | `App.xaml.cs` | `Views/ConnectionErrorPage.OnRetryClick` | `ConnectAndRouteAsync` |
| `App.BuildServiceProvider` (private static) | `App.xaml.cs` | `OnLaunched` | `ServiceCollection.AddSingleton` ×7 (BCL DI) |
| `App.RegisterActiveOnboardingViewModel`/`.UnregisterActiveOnboardingViewModel` (public, **mới, BUG B fix**) | `App.xaml.cs` | `Views/OnboardingPage.OnLoaded`/`.GoToMainShell` | set/clear field `_activeOnboardingViewModel` (đọc bởi `OnWindowClosed`) |
| `App.OnWindowClosed` (private async void, **sửa, BUG B fix**) | `App.xaml.cs` | `MainWindow.Closed` (XAML event) | `OnboardingViewModel.Dispose` (nếu `_activeOnboardingViewModel` != null — user đóng app giữa chừng ở màn Recovery Key), `UiIpcClient.DisconnectAsync`, `SingleInstanceGuard.Dispose` |
| `MainWindow` ctor | `MainWindow.xaml.cs` | `App.OnLaunched` | — |

### Khoảng trống đã biết — Đợt 6 giai đoạn 1 (báo cáo lại, không tự quyết định)

- **Chưa có test tích hợp/UI thật** cho toàn bộ `ParentalGuard.UI` (chỉ build + 2 unit test cho
  `UiIpcClient` ở `tests/ParentalGuard.Service.Tests/UiIpcClientTests.cs`, dùng named pipe loopback
  thật mô phỏng `UiSessionServer` — KHÔNG round-trip qua `UiSessionServer`/`AuthCoordinator` thật) —
  sandbox dev không có màn hình/không chạy được app WinUI 3 tương tác để verify UX/accessibility thật,
  cùng nhóm khoảng trống "cần máy Windows thật" đã ghi nhận nhiều lần ở các Đợt trước.
- **`SingleInstanceGuard` tìm cửa sổ đang chạy theo TIÊU ĐỀ cửa sổ, không phải class name cố định**
  như câu chữ gốc ADR-117a mô tả — Windows App SDK không expose 1 class name literal ổn định/tài liệu
  hoá qua các phiên bản để hard-code an toàn cho `Microsoft.UI.Xaml.Window` unpackaged; đã ghi rõ lý do
  trong XML doc của `SingleInstanceGuard`. Không đổi mục tiêu/hành vi UX của ADR-117a, chỉ khác cách
  hiện thực bit-level — nếu `architecture-writer` muốn chốt lại câu chữ ADR-117a cho khớp, đây là 1
  dòng dễ sửa.
- **`WindowsAppSDKSelfContained=true` + `RuntimeIdentifier=win-x64` (thay vì Framework-dependent thuần
  hoặc đa kiến trúc x86/x64/arm64)** — quyết định implement nhỏ của lượt này, chưa được chốt tường
  minh ở `Architecture/11-deployment-release-architecture.md` (chưa viết, theo đúng mục 2.1 file `10`
  đã xác nhận "MSIX packaging cho cả app chưa được quyết định... không tự phát minh trước"). Chọn
  `win-x64` đơn kiến trúc để build nhanh/đơn giản trong giai đoạn phát triển — Đợt 11 (deployment) cần
  xác nhận lại kiến trúc CPU mục tiêu chính thức (có cần x86/arm64 không) trước khi đóng gói installer.
- **`CA5392` bị `NoWarn` ở `ParentalGuard.UI.csproj`** (không nới `WarningsAsErrors` chung) — chỉ nổ ở
  `UndockedRegFreeWinRT-AutoInitializer.cs`, file auto-include từ gói `Microsoft.WindowsAppSDK.Foundation`
  (bắt buộc cho app unpackaged), không phải code dự án — không có gì để sửa `DefaultDllImportSearchPaths`
  trong file không sở hữu.
- **Thuật toán đo độ mạnh mật khẩu (`ComputeStrengthLabel`) tự chọn** — thuần UX polish theo đúng câu
  chữ `PWD-002`/Architecture/10 mục 11 (không phải cơ chế bảo mật, không chặn thiết kế).
- **`IAuthFacade`/`AuthPromptDialog`/`OnboardingViewModel` đã được `security-privacy-auditor` review**
  (Đợt 6 giai đoạn 1, domain `PWD-*`) — 2 FAIL cứng (TEST-001 zero-tolerance) đã tìm thấy và fix trong
  lượt này: (1) `OnboardingSetPasswordPage.OnContinueClick` không clear `PasswordBox.Password` ở 2
  nhánh early-return (rỗng/không khớp) — sửa: clear cả 2 `PasswordBox` ngay sau khi đọc vào biến cục
  bộ, TRƯỚC mọi guard; (2) Recovery Key plaintext buffer không được zero nếu user đóng app giữa chừng
  ở màn Recovery Key trước khi Confirm — sửa: safety net `OnboardingViewModel.Dispose` +
  `App.RegisterActiveOnboardingViewModel`/`.UnregisterActiveOnboardingViewModel` (xem 2 dòng ở trên).
  Có test hồi quy cho bug (2) ở `tests/ParentalGuard.UI.Tests/OnboardingViewModelTests.cs`; bug (1) khó
  test tự động (phụ thuộc `PasswordBox` XAML control, sandbox không render UI tương tác) — đã review
  thủ công logic.

## Đợt 6 (Architecture/10-ui-architecture.md) — `ParentalGuard.UI` giai đoạn 2 (`S2` Dashboard)

Phạm vi lượt này: `DashboardFacade`/`PauseFacade` implement đầy đủ (trước là stub rỗng), `DashboardViewModel`
(poll 5s, ADR-120), `DashboardPage.xaml` layout thật (5 phần mục 6.2). Amendment `ParentalGuard.Ipc/Protos/ipc.proto`
— thêm message Đợt 6 field 98/99 (`DashboardStatusQuery`/`Response`) + 140/141/144/145
(`AcknowledgePauseAnomalyRequest`/`Response`, `AuditChartQuery`/`Response`, `DailyBlockCount`) đúng
`03-ipc-communication.md` mục 3.7 — field 142/143/146-153 (`S3`/`S4`) CHƯA định nghĩa, để dành giai đoạn sau
(142/143/146/147 đã định nghĩa ở giai đoạn 3 — xem bên dưới; 148-153 vẫn để dành `S4`).

### `src/ParentalGuard.UI/ViewModels/DashboardViewModel.cs`

| Hàm | Callers | Callees |
|---|---|---|
| `DashboardViewModel.Start` | `Views/DashboardPage.OnNavigatedTo` | `DispatcherQueue.CreateTimer`, `PollAsync`, `LoadChartAsync` (fire-and-forget lần đầu), `DispatcherQueueTimer.Start` |
| `DashboardViewModel.Stop` | `Views/DashboardPage.OnNavigatedFrom` | `DispatcherQueueTimer.Stop` (KHÔNG poll nền khi rời trang, mục 3.3) |
| `DashboardViewModel.PollAsync` (private) | `Start` (lần đầu), `DispatcherQueueTimer.Tick` (mỗi 5s), `PauseAsync`/`ResumeAsync` (refresh ngay sau khi Pause/Resume thành công) | `IDashboardFacade.GetStatusAsync`, `ApplyStatus` |
| `DashboardViewModel.ApplyStatus` (**internal, seam test-only** — `InternalsVisibleTo` ở `AssemblyInfo.cs`) | `PollAsync`, `tests/ParentalGuard.UI.Tests/DashboardViewModelTests.cs` | set `CardState`/`WatchdogAlive`/`VisionConnected`/`VisionCpuFallback`/`OverlayConnected`/`DiskSpaceLow`/`UsingFallbackConfig`/`VisionDiagnosticStateDetail`/`ShowAnomalyBanner`/`IsPaused`/`PauseCountdownText`, `FormatCountdown` |
| `DashboardViewModel.PauseAsync`/`.ResumeAsync` | `Views/DashboardPage.OnPauseClick`/`.OnResumeClick` | `IAuthPromptService.ShowAuthPromptAsync("pause_monitoring", xamlRoot)` (mục 6.2.2 — gate `S5` TRƯỚC request thật), `PauseWithTokenAsync`/`ResumeWithTokenAsync` |
| `DashboardViewModel.PauseWithTokenAsync`/`.ResumeWithTokenAsync` (private, mục 6.5 — sửa gap `test-runner` phát hiện 2026-09-24) | `PauseAsync`/`ResumeAsync`, chính nó (đệ quy đúng 1 lần khi retry) | `IPauseFacade.PauseMonitoringAsync`/`.ResumeMonitoringAsync`, `PollAsync` (refresh khi Success/idempotent) — `InvalidToken`: set `ErrorMessage` rồi tự gọi lại `IAuthPromptService.ShowAuthPromptAsync` NGAY (`allowRetry=true`→`false`, chống đệ quy vô hạn); token mới → gọi lại chính nó với token mới; user huỷ dialog retry hoặc retry vẫn `InvalidToken` → dừng, giữ `ErrorMessage` |
| `DashboardViewModel.AcknowledgeAnomalyAsync` (`[RelayCommand]` → `AcknowledgeAnomalyCommand`) | `Views/DashboardPage.xaml` (`InfoBar.ActionButton` Command binding) | `IDashboardFacade.AcknowledgePauseAnomalyAsync` |
| `DashboardViewModel.SetChartRangeAsync`/`.LoadChartAsync` (private) | `Views/DashboardPage.OnChart7Click`/`.OnChart30Click`, `Start` (lần đầu) | `IDashboardFacade.GetAuditChartAsync` |

### `src/ParentalGuard.UI/Services/IpcClient/DashboardFacade.cs`, `PauseFacade.cs` (implement đầy đủ, xem bảng ở trên)

### `src/ParentalGuard.UI/Views/DashboardPage.xaml(.cs)`

| Hàm | Callers | Callees |
|---|---|---|
| `DashboardPage` ctor | `Views/MainShellPage.OnLoaded`/`.OnSelectionChanged` (`Frame.Navigate`) | `new DashboardViewModel` (Facade lấy qua `App.Services`), `ApplyStaticLabels` |
| `DashboardPage.OnNavigatedTo`/`.OnNavigatedFrom` | Windows App SDK (`Frame` navigation lifecycle) | `DashboardViewModel.Start`/`.Stop` |
| `DashboardPage.RenderChart` | `OnViewModelPropertyChanged` (khi `ChartData`/`IsChartEmpty` đổi), `OnChartCanvasSizeChanged` | `Canvas.Children.Clear`, vẽ `Rectangle` trực tiếp (mục 6.2.5/11 — KHÔNG thêm NuGet chart mới, xem "Quyết định implement tự chọn" bên dưới) |

### Quyết định implement tự chọn (giai đoạn 2)

- **Chart tự vẽ bằng `Microsoft.UI.Xaml.Shapes.Rectangle`/`Canvas`, KHÔNG thêm package `LiveChartsCore.SkiaSharpView.WinUI`**
  (Architecture/10 mục 6.2.5/11 để ngỏ 2 lựa chọn) — sandbox dev không có màn hình để verify UI thật
  (không thể xác nhận 1 thư viện chart mới render đúng/không lỗi runtime WinRT), nên 0 dependency mới
  là lựa chọn rủi ro thấp hơn ở giai đoạn này; `feature-dev` tương lai có thể thay bằng LiveCharts nếu
  cần biểu đồ phức tạp hơn (đường xu hướng, tooltip...) mà không đổi hợp đồng `IDashboardFacade`.
- **Picker thời lượng Pause implement bằng `ComboBox` inline** (không phải `Flyout`/dialog riêng) —
  Architecture/10 mục 6.2.2 chỉ nói "mở picker 5 lựa chọn", không chỉ định control cụ thể; `ComboBox`
  đơn giản nhất, đúng tinh thần "chi tiết implement không chặn kiến trúc".
- **`x:Bind` không hỗ trợ toán tử `!` trong markup ở bản Windows App SDK 2.5.1 đang dùng** (xác nhận
  qua build thật — lỗi `token recognition error at: '!'`) — thêm property phủ định tường minh
  (`IsNotPaused`/`IsNotBusy`/`HasChartData`) trên `DashboardViewModel` thay vì phủ định trong XAML.

### Test mới (`tests/ParentalGuard.UI.Tests/`)

`DashboardFacadeTests.cs`/`PauseFacadeTests.cs` — loopback named pipe thật (cùng mẫu hình
`UiIpcClientTests`), verify round-trip 2 query liên tiếp + mapping enum kết quả. `DashboardViewModelTests.cs`
— verify `ApplyStatus` (CardState/Error precedence/cpu-fallback substring/disk threshold ADR-123),
`AcknowledgeAnomalyCommand`, `SetChartRangeAsync`/`IsChartEmpty` (`FE-040`) qua fake facade. **Cập nhật
2026-09-24** (sửa gap `test-runner` phát hiện — `PauseAsync`/`ResumeAsync` nhánh `InvalidToken` không tự
mở lại `S5` như mục 6.5 yêu cầu): tách `IAuthPromptService` khỏi `NavigationService` (seam test-only) →
`PauseAsync`/`ResumeAsync` giờ TEST ĐƯỢC end-to-end qua `FakeAuthPromptService`/`FakePauseFacade`
(không cần `XamlRoot`/`ContentDialog` thật — fake bỏ qua tham số `xamlRoot`, gọi `PauseAsync(null!)` an
toàn trong unit test). 5 test mới: `PauseAsync`/`ResumeAsync` × (retry thành công với token mới, user
huỷ dialog retry → giữ `ErrorMessage`, retry cũng `InvalidToken` → dừng sau đúng 1 lần — không test
riêng nhánh cuối cho Resume vì logic `ResumeWithTokenAsync` giống hệt `PauseWithTokenAsync`, đã cover
qua `PauseAsync` tương ứng).

## Đợt 6 (Architecture/10-ui-architecture.md) — `ParentalGuard.UI` giai đoạn 3 (`S3` Lịch sử / Audit log)

Phạm vi lượt này: `AuditFacade` implement đầy đủ (trước là stub rỗng), `AuditLogViewModel` mới (gate
`S5` bắt buộc mỗi lần vào tab, phân trang, mapping `event_type`→tiếng Việt, "Đánh dấu sai"),
`AuditLogPage.xaml` layout thật (mục 6.3), đồng bộ `NavigationView.SelectedItem` khi điều hướng nội bộ
`ContentFrame` (không qua `OnSelectionChanged`). Amendment `ParentalGuard.Ipc/Protos/ipc.proto` — định
nghĩa message field 142/143 (`AuditLogQuery`/`Response`, `AuditLogEntry`, enum `AuditLogQueryResult`) và
146/147 (`MarkFalsePositiveRequest`/`Response`, enum `MarkFalsePositiveResult`) đúng
`03-ipc-communication.md` mục 3.7 — field number xác nhận lại từ file đó (không tin comment placeholder
cũ trong `.proto`, khớp chính xác). 2 enum `*Result` dùng tiền tố tên type (`AUDIT_LOG_QUERY_RESULT_*`/
`MARK_FALSE_POSITIVE_RESULT_*`) vì `SUCCESS`/`INVALID_TOKEN` bare đã bị `UninstallResult` chiếm trong
cùng file (cùng quy ước đã áp dụng cho `SetupResult`/`ConfirmResult`/`PauseResult`/`ResumeResult`) — xác
nhận qua build thật `protoc-gen-csharp` rút gọn đúng thành `AuditLogQueryResult.Success`/`.InvalidToken`,
`MarkFalsePositiveResult.Success`/`.InvalidToken`/`.AlreadyListed`. Field 148-153 (`ConfigQuery`/
`ConfigUpdateRequest`/`RemoveWhitelistEntry`, `S4`) vẫn CHƯA định nghĩa, để dành giai đoạn 4.

### `src/ParentalGuard.UI/ViewModels/AuditLogViewModel.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `AuditLogViewModel.InitializeAsync` | `Views/AuditLogPage.OnNavigatedTo` | `IAuthPromptService.ShowAuthPromptAsync("view_audit_log", xamlRoot)` (mục 6.3 — gate TRƯỚC khi render bất kỳ nội dung nào), `LoadPageAsync(page=0)` — huỷ hoặc `InvalidToken` không phục hồi được (mở lại `S5` 1 lần, mục 6.5) → `GateCancelled=true` |
| `AuditLogViewModel.LoadMoreAsync` | `Views/AuditLogPage.OnLoadMoreClick` | `LoadPageAsync(_noToken, _nextPage)` — trang ≥1 gửi `action_token` RỖNG (mục 6.3: Service tự nhớ "đã qua gate" theo session pipe), `InvalidToken` ở trang này → `HasMore=false` + `ErrorMessage` (không mở lại `S5`, khác race token trang đầu) |
| `AuditLogViewModel.LoadPageAsync` (private) | `InitializeAsync`, `LoadMoreAsync` | `IAuditFacade.GetAuditLogAsync`, `ToRowViewData` (mỗi entry), `CryptographicOperations.ZeroMemory` (zero `action_token` ngay sau khi facade dùng xong, kể cả khi `InvalidToken`) |
| `AuditLogViewModel.MarkFalsePositiveAsync` | `Views/AuditLogPage.OnMarkFalsePositiveClick` | `IAuthPromptService.ShowAuthPromptAsync("manage_whitelist", xamlRoot)` (gate RIÊNG, khác token `view_audit_log`), `MarkFalsePositiveWithTokenAsync` |
| `AuditLogViewModel.MarkFalsePositiveWithTokenAsync` (private, mục 6.5 — cùng mẫu hình `DashboardViewModel.PauseWithTokenAsync`) | `MarkFalsePositiveAsync`, chính nó (đệ quy đúng 1 lần khi retry) | `IAuditFacade.MarkFalsePositiveAsync`, `CryptographicOperations.ZeroMemory` — `Success`/`AlreadyListed`: set `StatusMessage`; `InvalidToken`: set `ErrorMessage` rồi tự mở lại `S5` NGAY (`allowRetry=true`→`false`) |
| `AuditLogViewModel.ToRowViewData` (private static) | `LoadPageAsync` | `EventTypeDisplay.ToText`, `LocalizationService.Get/.GetFormatted` (`AuditProcessNameFormat`/`AuditRiskScoreFormat`, chỉ khi `event_type="ContentBlocked"`) |
| `EventTypeDisplay.ToText` (internal static, `04-data-architecture.md` mục 5.1) | `ToRowViewData` | `LocalizationService.Get` — `event_type` không có trong bảng tra cứu → trả nguyên văn literal (không rớt lỗi, không ẩn thông tin) |

### `src/ParentalGuard.UI/Services/IpcClient/AuditFacade.cs` (implement đầy đủ, xem bảng ở trên)

### `src/ParentalGuard.UI/Views/AuditLogPage.xaml(.cs)`

| Hàm | Callers | Callees |
|---|---|---|
| `AuditLogPage` ctor | `Views/MainShellPage` (`ContentFrame.Navigate`, lúc chọn tab hoặc `OnNavigatedTo` tự điều hướng lại `S2`) | `new AuditLogViewModel` (Facade lấy qua `App.Services`), set `EmptyText.Text`/`LoadMoreButton.Content` |
| `AuditLogPage.OnNavigatedTo` | Windows App SDK (`Frame` navigation lifecycle) | `AuditLogViewModel.InitializeAsync(XamlRoot)` |
| `AuditLogPage.OnViewModelPropertyChanged` | `AuditLogViewModel.PropertyChanged` (đăng ký ở ctor) | `Frame.Navigate(typeof(DashboardPage))` khi `GateCancelled=true` (mục 6.3 — huỷ `S5` → quay lại `S2`) |
| `AuditLogPage.OnLoadMoreClick`/`.OnMarkFalsePositiveClick` | nút "Tải thêm"/"Đánh dấu sai" (XAML event) | `AuditLogViewModel.LoadMoreAsync`/`.MarkFalsePositiveAsync` |
| `AuditLogPage.OnMarkFalsePositiveButtonLoaded` | `Button.Loaded` trong `DataTemplate` (mỗi dòng `ContentBlocked`, XAML event) | `LocalizationService.Get` (set `Content` — nút nằm trong `DataTemplate` lặp lại theo dòng, không có nơi "static label" chung như control đơn lẻ) |

### `src/ParentalGuard.UI/Views/MainShellPage.xaml.cs` (sửa, đồng bộ `NavigationView.SelectedItem`)

| Hàm | Callers | Callees |
|---|---|---|
| `MainShellPage` ctor — thêm `ContentFrame.Navigated` handler (**mới giai đoạn 3**) | `Views/MainShellPage.xaml.cs` | đồng bộ `Nav.SelectedItem` theo `e.SourcePageType` — cần thiết vì `AuditLogPage.OnViewModelPropertyChanged` điều hướng `Frame.Navigate` trực tiếp (không qua `OnSelectionChanged`), nếu không đồng bộ thì `NavigationViewItem` đang chọn sẽ sai sau khi huỷ gate `S5` |
| `MainShellPage.OnSelectionChanged` (sửa — thêm guard `CurrentSourcePageType != pageType`) | `NavigationView.SelectionChanged` (XAML event) | `ContentFrame.Navigate` — tránh Navigate lặp vô ích khi `Navigated` handler vừa set lại `SelectedItem` cho đúng trang đang hiển thị |

### Quyết định implement tự chọn (giai đoạn 3)

- **`ListView`/`DataTemplate` cho danh sách audit log** (Architecture/10 mục 6.3 chỉ nói "danh sách" —
  không chỉ định control cụ thể) — chuẩn WinUI 3, có accessibility/keyboard nav sẵn (mục 8), không tự vẽ.
- **Nút "Đánh dấu sai" set `Content` qua `Button.Loaded` trong `DataTemplate`** thay vì `x:Bind` gọi hàm
  tĩnh `LocalizationService.Get` trực tiếp trong markup — đơn giản hơn, không cần khai báo hàm
  `x:Bind`-compatible, nhất quán cách các control khác trong dự án set text qua `LocalizationService`
  ở code-behind (`ApplyStaticLabels` các trang khác), chỉ khác là set theo từng instance vì nằm trong
  `DataTemplate` lặp lại.
- **`IsNotLoadingMore` property phủ định tường minh** trên `AuditLogViewModel` — cùng lý do đã ghi ở
  giai đoạn 2 (`x:Bind` không hỗ trợ `!` trong markup ở bản Windows App SDK đang dùng).
- **Zero `action_token`** cho cả luồng `view_audit_log` (trang đầu — trang sau không có gì để zero vì
  gửi mảng rỗng) và `manage_whitelist` — nhất quán mẫu hình `AuthFacade`, dù `PauseFacade`/
  `DashboardViewModel` giai đoạn 2 KHÔNG làm việc này (không phải regression — bổ sung thêm an toàn ở
  giai đoạn 3 theo đúng nhắc nhở lúc giao việc, "an toàn là ưu tiên nếu dễ làm mà không tốn công";
  không sửa lại `PauseFacade`/`DashboardViewModel` giai đoạn 2 vì ngoài phạm vi lượt này).

### Test mới (`tests/ParentalGuard.UI.Tests/`)

`AuditFacadeTests.cs` — loopback named pipe thật (cùng mẫu hình `DashboardFacadeTests`/`PauseFacadeTests`),
verify mapping cả 2 outcome (`Success`/`InvalidToken`) của `AuditLogQuery` và cả 3 outcome
(`Success`/`InvalidToken`/`AlreadyListed`) của `MarkFalsePositiveRequest`. `AuditLogViewModelTests.cs` —
qua `FakeAuditFacade`/`FakeAuthPromptService` (không cần `XamlRoot`/`ContentDialog` thật, cùng mẫu hình
`DashboardViewModelTests`): gate S5 thành công → `IsGated=true` + `Entries` populated; huỷ dialog →
`GateCancelled=true`; `InvalidToken` ở trang đầu → tự mở lại `S5` 1 lần (thành công/huỷ/vẫn `InvalidToken`
— 3 nhánh, cùng mẫu hình `PauseAsync` giai đoạn 2); `LoadMoreAsync` gửi `action_token` RỖNG cho trang
≥1 + nối thêm `Entries` (không thay thế); `IsEmpty`/`ShowEntries` (`FE-040`) đúng theo `IsGated`/`IsBusy`/
số lượng `Entries`; `MarkFalsePositiveAsync` set `StatusMessage` đúng (`Success`/`AlreadyListed`) hoặc
tự mở lại `S5` khi `InvalidToken`.

## Đợt 6 (Architecture/10-ui-architecture.md) — `ParentalGuard.UI` giai đoạn 4 (`S4` Cài đặt nâng cao)

Phạm vi lượt này: `ConfigFacade` implement đầy đủ (trước là stub rỗng), `IAuthFacade`/`AuthFacade` thêm
`ChangePasswordAsync` (trước chưa có), `SettingsViewModel` mới (đổi thông điệp overlay `FE-012`/`012a`,
quản lý whitelist `MISC-030`, chế độ hiệu năng `PERF-050b`/ADR-125, đổi mật khẩu `PWD-040`/`041`),
`SettingsPage.xaml` layout thật (mục 6.4). Amendment `ParentalGuard.Ipc/Protos/ipc.proto` — định nghĩa
nốt field 148-153 (`ConfigQuery`/`ConfigResponse`/`ConfigUpdateRequest`/`ConfigUpdateResponse`/
`RemoveWhitelistEntryRequest`/`RemoveWhitelistEntryResponse`, enum `PerformanceMode`/`ConfigUpdateResult`/
`RemoveWhitelistEntryResult`) đúng `03-ipc-communication.md` mục 3.7 — khối 140-159 (UI Dashboard/Settings)
nay đã dùng hết. Xác nhận không xung đột với giai đoạn 3 (`S3`, chạy song song): field 140-147 đã có sẵn
trong `.proto` lúc bắt đầu lượt này (`AcknowledgePauseAnomalyRequest/Response`, `AuditLogQuery/Response`,
`AuditChartQuery/Response`, `MarkFalsePositiveRequest/Response`) — giữ nguyên, chỉ nối thêm 148-153 vào
cuối khối `oneof`, không sửa/xoá dòng nào của giai đoạn 3.

### `src/ParentalGuard.UI/Services/IpcClient/ConfigFacade.cs`, `IConfigFacade.cs` (implement đầy đủ)

| Hàm | Callers | Callees |
|---|---|---|
| `ConfigFacade.GetConfigAsync` | `SettingsViewModel.InitializeAsync` | `UiIpcClient.NewEnvelope`/`.SendRequestAsync`, `MapConfigResponse` |
| `ConfigFacade.UpdateOverlayMessageAsync`/`.UpdatePerformanceModeAsync` (cả 2 gọi chung `UpdateConfigAsync` private — "full update", mục 6.4) | `SettingsViewModel.SaveOverlayMessageAsync`/`.ResetOverlayMessageToDefaultAsync`/`.SetPerformanceModeAsync` | `UiIpcClient.SendRequestAsync`, `MapConfigUpdateResponse`, `MapPerformanceMode` (2 overload, POCO↔proto) |
| `ConfigFacade.RemoveWhitelistEntryAsync` | `SettingsViewModel.RemoveWhitelistEntryWithTokenAsync` | `UiIpcClient.SendRequestAsync`, `MapRemoveWhitelistResponse` |

### `src/ParentalGuard.UI/Services/IpcClient/IAuthFacade.cs`, `AuthFacade.cs` (thêm `ChangePasswordAsync`)

| Hàm | Callers | Callees |
|---|---|---|
| `AuthFacade.ChangePasswordAsync` (mới) | `SettingsViewModel.ChangePasswordAsync` | `UiIpcClient.NewEnvelope`/`.SendRequestAsync`, `MapChangePasswordResponse`, `CryptographicOperations.ZeroMemory`/`CredentialBytes.Zero` (zero cả 2 buffer pinned gốc + 2 `ByteString` nội bộ trong `finally`, Architecture/08 mục 5.3) |
| `AuthFacade.MapChangePasswordResponse` (private static, mới) | `ChangePasswordAsync` | `CredentialBytes.UnsafeGetBuffer` (chỉ khi `Success` + `new_recovery_key_plaintext` không rỗng, ADR-83) |

### `src/ParentalGuard.UI/ViewModels/SettingsViewModel.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `SettingsViewModel.InitializeAsync` | `Views/SettingsPage.OnNavigatedTo` | `IConfigFacade.GetConfigAsync` (không gate, mục 6.4) — set `OverlayMessage`/`_lastSavedOverlayMessage`/`PerformanceMode`/`WhitelistedProcessNames` |
| `SettingsViewModel.SaveOverlayMessageAsync` | `Views/SettingsPage.OnSaveOverlayMessageClick`, `ResetOverlayMessageToDefaultAsync` | `OverlayMessageValidation.IsValid` (defense in depth trước khi gửi), `IConfigFacade.UpdateOverlayMessageAsync` (kèm `PerformanceMode` hiện hành — "full update") — `InvalidCharacters`/`TooLong`: revert `OverlayMessage` về `_lastSavedOverlayMessage` (giữ nguyên giá trị cũ đã lưu, mục 6.4) |
| `SettingsViewModel.ResetOverlayMessageToDefaultAsync` | `Views/SettingsPage.OnResetOverlayMessageClick` | set `OverlayMessage=""`, `SaveOverlayMessageAsync` (gửi luôn, không cần xác nhận thêm) |
| `SettingsViewModel.SetPerformanceModeAsync` | `Views/SettingsPage.OnPerformanceModeSelectionChanged` | `IConfigFacade.UpdatePerformanceModeAsync` (kèm `_lastSavedOverlayMessage` ĐÃ LƯU — KHÔNG phải `OverlayMessage` đang gõ dở, tránh vô tình lưu draft khi user chỉ đổi mode) — thất bại: revert `PerformanceMode` về giá trị trước đó |
| `SettingsViewModel.RemoveWhitelistEntryAsync` | `Views/SettingsPage.OnRemoveWhitelistEntryClick` | `IAuthPromptService.ShowAuthPromptAsync("manage_whitelist", xamlRoot)` (gate TRƯỚC request thật, `MISC-030`/ADR-122), `RemoveWhitelistEntryWithTokenAsync` |
| `SettingsViewModel.RemoveWhitelistEntryWithTokenAsync` (private, mục 6.5 — cùng mẫu hình `AuditLogViewModel.MarkFalsePositiveWithTokenAsync`) | `RemoveWhitelistEntryAsync`, chính nó (đệ quy đúng 1 lần khi retry) | `IConfigFacade.RemoveWhitelistEntryAsync`, `CryptographicOperations.ZeroMemory` — `Success`/`NotFound` (idempotent guard): xoá khỏi `WhitelistedProcessNames`; `InvalidToken`: tự mở lại `S5` NGAY (`allowRetry=true`→`false`) |
| `SettingsViewModel.ChangePasswordAsync` | `Views/SettingsPage.OnChangePasswordClick` | `IAuthFacade.ChangePasswordAsync` (tự gate qua `old_password`, KHÔNG qua `S5`, `08` mục 7.4) — `Success`+có `NewRecoveryKeyPlaintextUtf8`: **fix bug security audit Đợt 6 S6** — nếu `_discarded` (tab đã bị rời/app đã đóng trong lúc chờ IPC), `CryptographicOperations.ZeroMemory` buffer mới nhận NGAY, KHÔNG publish; ngược lại zero `_newRecoveryKeyPlaintextBuffer` CŨ trước (fix bug security audit Đợt 6 S4, nếu chưa acknowledge) rồi mới ghi đè, decode UTF-8 1 lần → `NewRecoveryKeyDisplay` (cùng mẫu hình `OnboardingViewModel.SubmitPasswordAsync` bước 3) |
| `SettingsViewModel.CanSubmitChangePassword` (mới, computed — fix bug security audit Đợt 6 S4) | `Views/SettingsPage.xaml` (`ChangePasswordButton.IsEnabled`, thay `IsNotChangingPassword`) | `IsNotChangingPassword && !HasNewRecoveryKeyDisplay` (defense in depth lớp 2 — khoá nút khi Recovery Key mới chưa acknowledge, chặn đường tái hiện qua UI thật) |
| `SettingsViewModel.MarkDiscarded` (mới — fix bug security audit Đợt 6 S6, idempotent) | `Views/SettingsPage.OnNavigatedFrom`, `App.OnWindowClosed` (qua `_activeSettingsViewModel`) | set field `_discarded = true` (đọc bởi `ChangePasswordAsync` nhánh `Success`) |
| `SettingsViewModel.AcknowledgeNewRecoveryKeyDisplayed`/`.Dispose` | `Views/SettingsPage.OnAcknowledgeNewRecoveryKeyClick`/`.OnNavigatedFrom`, `App.OnWindowClosed` (qua `RegisterActiveSettingsViewModel`, safety net BUG B) | `CryptographicOperations.ZeroMemory` (zero buffer Recovery Key mới ngay, idempotent) |
| `OverlayMessageValidation.IsValid`/`.IsAllowedChar` (static, mới — cùng file) | `SaveOverlayMessageAsync`, `Views/SettingsPage.OnOverlayMessageBeforeTextChanging` | thuần logic `char.IsLetter`/`.IsDigit`/dấu câu cho phép (`FE-012a`) — dùng CẢ ở ViewModel (defense in depth) LẪN code-behind (chặn ngay khi nhập) |

### `src/ParentalGuard.UI/Views/SettingsPage.xaml(.cs)` (thay placeholder bằng layout thật)

| Hàm | Callers | Callees |
|---|---|---|
| `SettingsPage` ctor | `Views/MainShellPage` (`ContentFrame.Navigate`, lúc chọn tab) | `new SettingsViewModel` (Facade lấy qua `App.Services`), set label tĩnh qua `LocalizationService.Get` |
| `SettingsPage.OnNavigatedTo` | Windows App SDK (`Frame` navigation lifecycle) | `App.RegisterActiveSettingsViewModel`, `SettingsViewModel.InitializeAsync` (không gate), set `PerformanceModeRadios.SelectedIndex` ban đầu (guard `_performanceModeInitialized` trước khi cho `OnPerformanceModeSelectionChanged` gọi facade) |
| `SettingsPage.OnNavigatedFrom` | Windows App SDK (`Frame` navigation lifecycle) | `App.UnregisterActiveSettingsViewModel`, `SettingsViewModel.MarkDiscarded` (fix bug security audit Đợt 6 S6 — TRƯỚC Acknowledge, phòng `ChangePasswordAsync` còn treo IPC), `.AcknowledgeNewRecoveryKeyDisplayed` (safety net rời tab khi Recovery Key mới còn hiển thị) |
| `SettingsPage.OnOverlayMessageBeforeTextChanging` | `TextBox.BeforeTextChanging` (XAML event, `OverlayMessageInput`) | `OverlayMessageValidation.IsValid` — `args.Cancel=true` nếu chứa ký tự cấm (chặn CẢ gõ lẫn dán, `FE-012a`) |
| `SettingsPage.OnPerformanceModeSelectionChanged` | `RadioButtons.SelectionChanged` (XAML event) | `SettingsViewModel.SetPerformanceModeAsync` (bỏ qua nếu `!_performanceModeInitialized`, tránh tự gọi lúc set giá trị ban đầu từ `OnNavigatedTo`) |
| `SettingsPage.OnSaveOverlayMessageClick`/`.OnResetOverlayMessageClick` | nút "Lưu"/"Khôi phục mặc định" (XAML event) | `SettingsViewModel.SaveOverlayMessageAsync`/`.ResetOverlayMessageToDefaultAsync` |
| `SettingsPage.OnRemoveWhitelistEntryClick`/`.OnRemoveWhitelistButtonLoaded` | nút Xoá mỗi dòng whitelist (`DataTemplate`, XAML event) | `SettingsViewModel.RemoveWhitelistEntryAsync`, `LocalizationService.Get` (set `Content` theo instance, cùng mẫu hình `AuditLogPage.OnMarkFalsePositiveButtonLoaded`) |
| `SettingsPage.OnChangePasswordClick` | nút "Đổi mật khẩu" (XAML event) | đọc `PasswordBox.Password` × 3 → `byte[]` pinned NGAY, clear cả 3 `PasswordBox` TRƯỚC guard/validate (Architecture/08 mục 5.3, cùng mẫu hình `OnboardingSetPasswordPage.OnContinueClick`), `SettingsViewModel.ChangePasswordAsync` |
| `SettingsPage.OnForgotOldPasswordClick` | link "Quên mật khẩu cũ?" (XAML event, `ForgotOldPasswordLink.IsEnabled` bind `ViewModel.IsNotChangingPassword` — thêm ở fix bug security audit Đợt 6 S6, tránh rời tab không cần thiết trong lúc `ChangePasswordAsync` đang treo IPC) | `NavigationService.NavigateToRecovery` (điều hướng thẳng `S6`, KHÔNG qua `S5` — implement thật từ giai đoạn 5, xem mục "Đợt 6 giai đoạn 5" cuối file) |
| `SettingsPage.OnCopyNewRecoveryKeyClick` | nút "Sao chép" trên Recovery Key mới (XAML event) | `Clipboard.SetContent` (cùng residual risk đã ghi nhận ở `OnboardingRecoveryKeyPage`) |

### `src/ParentalGuard.UI/App.xaml.cs` (sửa — safety net BUG B cho `SettingsViewModel`)

| Hàm | Callers | Callees |
|---|---|---|
| `App.RegisterActiveSettingsViewModel`/`.UnregisterActiveSettingsViewModel` (mới) | `Views/SettingsPage.OnNavigatedTo`/`.OnNavigatedFrom` | set/clear field `_activeSettingsViewModel` |
| `App.OnWindowClosed` (sửa — thêm nhánh Settings) | Windows App SDK (`Window.Closed`) | `SettingsViewModel.MarkDiscarded` (fix bug security audit Đợt 6 S6, TRƯỚC `Dispose` — `UiIpcClient.DisconnectAsync` bên dưới bị chặn chờ request `ChangePasswordAsync` đang treo xong nên vẫn có thể nhận `Success` thật sau khi window đã đóng), `.Dispose` (cùng lý do BUG B đã sửa cho `OnboardingViewModel` giai đoạn 1 — đóng app giữa chừng khi Recovery Key mới còn hiển thị vẫn phải zero buffer) |

### Quyết định implement tự chọn (giai đoạn 4)

- **"Full update" cho `ConfigUpdateRequest` luôn gửi giá trị ĐÃ LƯU của field không phải mục đích chính
  của lần Save** (`_lastSavedOverlayMessage` khi đổi `performance_mode`; `PerformanceMode` hiện hành khi
  lưu `overlay_message`) — Architecture/10 mục 6.4 chỉ nói "luôn gửi đầy đủ giá trị hiện hành của cả 2
  field", không nói rõ "hiện hành" là bản đang gõ dở hay bản đã lưu; chọn bản ĐÃ LƯU để tránh tác dụng phụ
  bất ngờ (đổi radio hiệu năng vô tình lưu luôn 1 câu overlay đang gõ dở, chưa bấm Lưu).
- **`RadioButtons` (WinUI 3, ADR-125) đọc/ghi qua guard `_performanceModeInitialized` ở code-behind**
  (không dùng `x:Bind` `SelectedIndex` hai chiều) — `SelectionChanged` fire cả khi set giá trị ban đầu từ
  `OnNavigatedTo` lẫn khi user tự chọn; guard đơn giản hơn phân biệt nguồn gốc sự kiện qua binding.
- **`TextBox.BeforeTextChanging` cho `FE-012a`** — chặn TOÀN BỘ `NewText` (không chỉ ký tự vừa gõ) nên
  cùng logic xử lý đúng cả gõ tay lẫn dán (paste) trong 1 chỗ, không cần xử lý `Paste` event riêng.
- **Không tạo `UserControl` `RecoveryKeyDisplayControl` riêng** dù mục 6.4 văn bản Architecture gợi ý
  "tái dùng" — xác nhận qua đọc code thật `OnboardingRecoveryKeyPage.xaml` (giai đoạn 1): không có
  `UserControl` nào như vậy, chỉ có inline `TextBlock`/`Button` bind thẳng property `ViewModel`. Lặp lại
  đúng pattern inline tương tự ở `SettingsPage.xaml` (không phát minh abstraction mới ngoài phạm vi,
  DEV-026) — chấp nhận trùng lặp nhỏ layout giữa 2 trang, cùng tinh thần các trùng lặp nhỏ khác đã chấp
  nhận trong dự án (vd default overlay message giữa `UI`/`Overlay`).
- **`OverlayMessageValidation` là `public static class` top-level trong `SettingsViewModel.cs`** (không
  file riêng) — dùng chung ở cả `SettingsViewModel` (defense in depth) và `SettingsPage.xaml.cs`
  (`BeforeTextChanging`), đặt cạnh nơi dùng chính, tránh tạo thêm 1 file chỉ cho 1 lớp nhỏ.

### Test mới (`tests/ParentalGuard.UI.Tests/`)

`ConfigFacadeTests.cs` — loopback named pipe thật (cùng mẫu hình `DashboardFacadeTests`/`PauseFacadeTests`),
verify `GetConfigAsync` mapping + `UpdateOverlayMessageAsync`/`UpdatePerformanceModeAsync` luôn gửi ĐỦ CẢ
2 field ("full update", assert trực tiếp trên request nhận được ở server giả) + mapping 3 outcome
`ConfigUpdateResult`/3 outcome `RemoveWhitelistEntryResult`. `AuthFacadeTests.cs` (mới — trước đây chưa
có file này, chỉ phủ `ChangePasswordAsync` vừa thêm) — mapping 4 outcome `ChangeResult`, verify
`NewRecoveryKeyPlaintextUtf8` giải mã đúng UTF-8 khi `Success`+`regenerate_recovery_key=true`.
`SettingsViewModelTests.cs` — qua `FakeConfigFacade`/`FakeAuthFacade`/`FakeAuthPromptService` (cùng mẫu
hình `AuditLogViewModelTests`): load config đúng field; lỗi kết nối → `LoadErrorMessage`; Save overlay
message thành công gửi kèm `PerformanceMode` hiện hành; Service từ chối (`InvalidCharacters`/`TooLong`) →
revert về giá trị đã lưu; Reset-to-default; đổi `performance_mode` gửi kèm overlay message ĐÃ LƯU (không
phải draft), bỏ qua nếu trùng giá trị hiện tại, revert khi thất bại; xoá whitelist entry (`Success`/
`NotFound` idempotent/huỷ gate/retry `InvalidToken` đúng 1 lần — cùng 3 nhánh mẫu hình giai đoạn 3); đổi
mật khẩu (`Success` có/không `regenerate`, `WrongOldPassword`), `Dispose` idempotent zero buffer Recovery
Key mới; **fix bug security audit Đợt 6 S4**: gọi `ChangePasswordAsync` 2 lần liên tiếp TRƯỚC khi
acknowledge → buffer Recovery Key #1 phải bị zero khi ghi đè bằng #2; `CanSubmitChangePassword` phải
`false` trong lúc `HasNewRecoveryKeyDisplay=true`, `true` lại sau `AcknowledgeNewRecoveryKeyDisplayed`.
`OverlayMessageValidationTests.cs` — `FE-012a`: câu mặc định + chữ/số/dấu câu cho phép hợp lệ;
symbol cấm/emoji/tab/control-char/quá 255 ký tự đều bị từ chối; chữ tiếng Việt có dấu hợp lệ.

## Ghi chú khoảng trống đã biết (xem báo cáo bàn giao)

- `ChildProcessSupervisor.VerifyClientIdentity` chỉ implement điều kiện 3a (đường dẫn cài đặt) của
  Architecture/03 mục 4.2 — điều kiện 3b (chữ ký code-signing khớp thumbprint SignPath) chưa có vì
  chưa có chứng chỉ thật (`SEC-030`/`DEV-012`, Đợt 9).

- **`BE-032` (Session 0 Isolation, force-close)** — ĐÃ ĐỒNG BỘ ĐẦY ĐỦ (2026-09-19, xác nhận lại bởi
  `architecture-writer`): `Architecture/02-process-architecture.md` mục 2.4/5 (v0.1.1) và
  `Specification/02-backend-spec.md` BE-032 (v0.11.3, ĐÃ CHỐT) đều mô tả đúng luồng thật — `Service`
  không có quyền gọi API `user32` lên HWND session tương tác, nên `Overlay` tự thực hiện
  `PostMessage(WM_CLOSE)` cục bộ (`OverlayWindowInterop.RequestClose`), đồng thời gửi
  `ForceCloseRequest` lên `Service` để `OverlayDecisionCoordinator` cập nhật danh sách overlay
  active + ghi audit log. Không còn gap Spec/Architecture nào cần xử lý thêm cho mục này.
- Threshold risk score dùng `config.MonitoringState.RiskThreshold` chụp 1 lần lúc `Worker.ExecuteAsync`
  khởi động (qua closure `() => config.MonitoringState.RiskThreshold`) — Đợt 1 chưa có đường nào đổi
  giá trị này lúc runtime (UI chỉnh ngưỡng là Đợt 6, Pause là Đợt 5), nên chưa phát sinh vấn đề, nhưng
  cần nhớ đổi thành đọc từ 1 state holder cập nhật được khi các Đợt đó tới.

## Bug đã sửa (lịch sử, để tham chiếu khi tổng hợp báo cáo cuối)

- **2026-09-18** — `security-privacy-auditor` phát hiện FAIL cứng (`TEST-001`): bootstrap khoá HMAC
  (`ChildProcessLauncher`) truyền handle của anonymous pipe qua command-line argument + dùng
  `inheritHandles: true` với `STARTUPINFO` cổ điển (kế thừa toàn bộ handle inheritable, không giới
  hạn) — sai với thiết kế đã duyệt ở `Architecture/03-ipc-communication.md` mục 5.2 (ADR-18: phải
  qua `STARTUPINFOEX` + `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, không qua command-line/env var). Đã sửa:
  `ProcessInterop` thêm `StartupInfoEx`/`CreateSingleHandleAttributeList` (giới hạn kế thừa còn đúng
  1 handle), `ChildProcessLauncher` truyền handle qua slot `StdInput` (`STARTF_USESTDHANDLES`) thay
  vì command-line, `ChildIpcBootstrap.ReadFromInheritedStdHandle` (thay `ReadFromClientHandle`) đọc
  lại bằng `GetStdHandle(STD_INPUT_HANDLE)`. Build/format/test (11/11) đã verify lại sạch sau khi
  sửa; hiệu lực runtime thật (spawn vào session tương tác) vẫn cần xác nhận trên máy Windows thật
  của chủ dự án — sandbox này không có quyền SYSTEM/session tương tác. `security-privacy-auditor`
  re-audit xác nhận PASS.

- **2026-09-18** — re-audit trên phát hiện thêm 1 bug nhỏ (reliability, không phải bảo mật):
  `ChildProcessLauncher.LaunchWithToken` leak `processInfo.Process` handle nếu
  `ChildIpcBootstrap.WriteTo`/`GetProcessById` throw giữa chừng (ví dụ child crash ngay sau spawn).
  Đã sửa: bọc đoạn từ `DisposeLocalCopyOfClientHandle` tới `GetProcessById` trong `try/finally`,
  đóng `processInfo.Process` trong `finally` thay vì chỉ ở nhánh thành công. Build/format/test
  (11/11) đã verify lại sạch.

- **2026-09-18** — `security-privacy-auditor` FAIL cứng `TEST-001` (Image Processing Pipeline, Đợt
  1) + 2 vấn đề `test-runner` flag thêm — cả 4 việc đã sửa, build/test xanh (45/45: 21 Service +
  24 Vision, gồm 3 test mới):
  1. **Zero-out không đảm bảo ở mọi exception path** (`FrameClassificationPipeline.cs`) — trước đây
     nhiều `try/finally` tách rời theo từng bước (crop/resize/classify), mỗi finally chỉ lo 1 việc
     hẹp; nếu `GpuWindowCropper.CropAndReadBack` throw GIỮA vòng lặp copy dòng ảnh (SAU KHI đã ghi 1
     phần dữ liệu pixel thật vào `_pixelBuffer`), buffer dirty này không bao giờ được zero — tương
     tự nếu `FrameResizerNormalizer.Resize` throw giữa chừng, `_inputTensor` chỉ được zero nếu luồng
     chạy tới được `Classify()`. Đã sửa: tách thân xử lý thành `ProcessFrame` (internal, xem bảng
     trên), bọc **đúng 1 try/finally** từ sau `EnsurePixelBuffer` tới hết — finally luôn
     `Array.Clear(_pixelBuffer)` + `_inputTensor.Buffer.Span.Clear()` **vô điều kiện** dù bước nào
     throw. `EnsurePixelBuffer` cũng sửa: `Array.Clear()` mảng cũ trước khi thay bằng mảng mới khi
     cửa sổ đổi kích thước (trước đây để lại object "mồ côi" chưa zero chờ GC dọn tại thời điểm bất
     định).
  2. **Crash-guard quanh `Process()`** (`CaptureLoopWorker.ProcessOneFrame`) — trước đây gọi
     `pipeline.Process(...)` không có `try/catch`; exception bay lên `Run()` (Thread riêng,
     `IsBackground=true`, ADR-38) làm crash toàn bộ `Vision.exe` (unhandled exception mặc định
     .NET). Đã sửa: bọc `try/catch` quanh `pipeline.Process`, không log dữ liệu ảnh/chi tiết
     exception (Vision không ghi file, BE-022/SEC-017) — chỉ đếm `_consecutiveFailures` + báo qua
     `IpcChildClient.DiagnosticState` (giống pattern `PERF-030/031` DirectML fallback). Nếu lỗi lặp
     liên tiếp ≥ `_maxConsecutiveFailures` (chọn `10`, không có con số nào được spec chốt sẵn — đủ
     lớn để bỏ qua lỗi thoáng qua, đủ nhỏ để không giám sát "chết lâm sàng" quá lâu), tự thoát có
     kiểm soát qua `Environment.Exit(VisionExitCodes.PipelineRepeatedFailure)` (`19`, mới) để
     `ChildProcessSupervisor.RunLoopAsync` (Service) tự respawn tiến trình sạch qua nhánh
     `catch (Exception ex) when (...)` chung sẵn có (không cần Service biết riêng exit code `19`).
  3. **Test còn thiếu cho zero-out** — `IFrameBufferAuditor` có sẵn nhưng không test nào dùng, và
     `GpuWindowCropper`/`DesktopDuplicationCapture` là `sealed class` cụ thể không mock được. Đã
     thêm seam `IWindowCropper`/`IFrameCapture` (`Capture/IWindowCropper.cs`, `Capture/IFrameCapture.cs`)
     cho `FrameClassificationPipeline` nhận qua constructor injection — **lưu ý quan trọng**: tham
     số frame gõ kiểu `IDisposable` (không phải `ID3D11Texture2D`) *cố ý* để 2 interface này (và
     test dùng chúng) không cần phụ thuộc `Vortice.Direct3D11`; lý do: lần thử đầu thêm
     `PackageReference Vortice.Direct3D11` vào `ParentalGuard.Vision.Tests.csproj` để dựng
     `new ID3D11Texture2D(nint.Zero)` khiến `dotnet test` báo
     `FileLoadException: An Application Control policy has blocked this file` khi load
     `ParentalGuard.Vision.Tests.dll` (WDAC/Smart App Control trên máy build chặn assembly test
     tham chiếu thư viện COM-interop) — đổi sang `IDisposable` loại bỏ hoàn toàn phụ thuộc đó khỏi
     bề mặt interface, test dùng 1 `IDisposable` giả trơn. **Nếu ai đó sau này thêm lại
     `PackageReference` tới `Vortice.*`/thư viện native-interop khác vào bất kỳ project `*.Tests`
     nào, cần biết trước rủi ro này.** Thêm `FrameClassificationPipeline.ProcessFrame` (internal,
     `InternalsVisibleTo("ParentalGuard.Vision.Tests")` qua `src/ParentalGuard.Vision/AssemblyInfo.cs`)
     + test hook `PixelBufferForTest`/`InputTensorForTest`. Viết
     `tests/ParentalGuard.Vision.Tests/FrameClassificationPipelineZeroOutTests.cs` (3 test): fake
     `IWindowCropper` throw giữa chừng sau khi ghi 1 phần dữ liệu → assert `_pixelBuffer`/
     `_inputTensor` zero hoàn toàn; fake `INsfwClassifier` throw sau khi `FrameResizerNormalizer`
     thật đã ghi dữ liệu vào tensor → assert tensor zero; success path → assert
     `OnBufferAllocated`/`OnZeroed` đúng 1 lần/buffer. Thêm `IFrameBufferAuditor.OnBufferAllocated`
     (mới, đối chiếu với `OnZeroed` có sẵn) theo đề xuất `security-privacy-auditor`. Bỏ qua phần
     tuỳ chọn "quét IL đảm bảo không gọi `File.*Write*`/`Convert.ToBase64String`" (không bắt buộc,
     để dành Đợt sau nếu cần).
  4. **Chặn Alt+F4 trên overlay** (`ContentBlurOverlayForm.cs`, quyết định chủ dự án: CHẶN) — override
     `WndProc` chặn `WM_SYSCOMMAND`/`SC_CLOSE` (Alt+F4, Alt+Space→Đóng) không cho chạy tới
     `base.WndProc` (nên không bao giờ phát sinh `FormClosing`/`Close()` từ nguồn này); `ShowInTaskbar`
     đã `false` sẵn từ trước nên đường "Close window" qua taskbar đã bị loại. Đóng hợp lệ chỉ còn 2
     đường: nút "Tắt nội dung" (`HandleCloseButtonClicked`) hoặc `OverlayCoordinator.RemoveOverlay`
     gọi `Close()`/`Dispose()` trực tiếp bằng code (không qua message nên không bị chặn).

- **2026-09-19** — re-audit tiếp theo (`security-privacy-auditor`) FAIL cứng `IMG-003`: cách sửa
  "1 finally duy nhất" ở mục `2026-09-18` (dòng trên) tuy thoả bất biến "luôn zero dù throw ở đâu",
  nhưng lại giữ `_pixelBuffer` (pixel BGRA8 thô) **chưa zero suốt thời gian `Classify()` (ONNX
  inference) chạy** dù đã đọc xong ở `FrameResizerNormalizer.Resize` — vi phạm câu chữ `IMG-003`
  ("ghi đè 0 ngay sau khi không còn cần dùng") và đúng bảng buffer/thời điểm zero đã duyệt ở
  `Architecture/05-image-pipeline-architecture.md` mục 6 (dòng "byte[] pixel BGRA8": zero "trong
  `finally` ngay sau `FrameResizerNormalizer` đọc xong", không phải "ngay sau `Classify`"). Đã sửa
  `FrameClassificationPipeline.ProcessFrame` (`Pipeline/FrameClassificationPipeline.cs`): đổi lại
  thành **nested try/finally đúng theo bước**, nhưng khác thiết kế gốc trước `2026-09-18` (đã fail)
  ở chỗ mỗi finally bọc **toàn bộ các bước có thể làm buffer đó dirty**, không chỉ đúng 1 bước hẹp —
  cụ thể: 1 `try` ngoài bọc {crop + resize} với `finally` zero `_pixelBuffer` ngay sau (bọc CẢ crop
  lẫn resize nên cropper throw giữa chừng sau khi ghi dữ liệu thật vẫn bị bắt bởi finally này, giữ
  đúng bất biến regression-guard bug `2026-09-18` cho buffer này); `Classify()` gọi sau finally đó
  (ngoài phạm vi zero pixel buffer — đúng ADR-43/IMG-003); 1 `try/finally` ngoài cùng bọc toàn bộ
  (kể cả `Classify()`) zero `_inputTensor` ngay sau — vẫn vô điều kiện dù bước nào throw. Không đổi
  chữ ký hàm/quan hệ caller-callee nào trong bảng phần trên (`ProcessFrame`/`EnsurePixelBuffer`/
  `FrameResizerNormalizer.Resize`/`NsfwClassifier.Classify` giữ nguyên caller/callee) — chỉ đổi cấu
  trúc try/finally nội bộ, dòng bảng liên quan ở trên không cần sửa. Cả 3 test
  `FrameClassificationPipelineZeroOutTests` (crop throw giữa chừng, classifier throw sau resize
  thật, success path) PASS không sửa gì — cấu trúc mới vẫn thoả đúng assertion cũ (`ZeroedCount`
  đúng 1 lần/buffer ở mọi nhánh). Thêm 1 test khoá cứng `VisionExitCodes.PipelineRepeatedFailure=19`
  (`VisionExitCodesTests.cs`, trước đây thiếu — chỉ có `17`/`18`) và cập nhật XML-doc
  `OverlayDecisionCoordinator.cs` (mô tả `BE-032` từ "khoảng trống đang treo" thành đúng thiết kế đã
  CHỐT ở `Architecture/02-process-architecture.md` v0.1.1 — Overlay tự `PostMessage(WM_CLOSE)` cục
  bộ, Service chỉ cập nhật state/audit qua `ForceCloseRequest`; không đổi hành vi code, chỉ đổi
  comment cho khớp thiết kế thật). Build 0 Warning/0 Error, test 46/46 (21 Service + 25 Vision, +1
  so với `2026-09-18`). Không sửa `GpuWindowCropper.cs` (case `CopySubresourceRegion` throw trước
  `Map()` để staging texture GPU chưa zero tại đúng thời điểm đó) — auditor xác nhận đây là rủi ro
  thấp (VRAM, không dễ bị RAM-dump đọc), không bắt buộc; để nguyên, ghi nhận lại ở đây cho minh bạch.

- **2026-09-20** — `security-privacy-auditor` re-audit Đợt 3 (Password & Authentication) phát hiện 3
  FAIL cứng (`TEST-001`) + `test-runner` phát hiện thêm 1 bug thật (hardcode path) — cả 4 đã sửa,
  build 0 Warning/0 Error, test 190/190 (18 Overlay + 45 Vision + 127 Service, +48 so với trước gồm
  test mới cho cả 4 fix). Thiết kế đầy đủ đã chốt trước ở `Architecture/08-password-authentication-architecture.md`
  v0.3.0 mục 5.5/ADR-83 (FAIL 1) và mục 7.9/ADR-84 (FAIL 3) — `feature-dev` chỉ cụ thể hoá code theo
  đúng thiết kế đã Approved, không tự quyết định thêm hành vi mới.
  1. **FAIL 1 — Recovery Key plaintext không zero được** (`ipc.proto` field 3 chỗ đã đổi `string`→`bytes`
     sẵn từ trước, chỉ cần code C# dùng lại): `AuthCoordinator` (`HandleSetInitialPasswordAsync`/
     `HandleChangePasswordAsync`/`HandleRecoveryResetAsync`) build Recovery Key plaintext vào 1 buffer
     `byte[]` pinned duy nhất, dùng cho cả hash Argon2id lẫn `ByteString.CopyFrom` vào response. Thêm
     cơ chế mới **`_pendingAfterSendCleanup`** (field `Action?`) — mỗi handler trên set 1 closure zero
     (pinned array gốc + buffer `ByteString` nội bộ Protobuf qua `CredentialBytes.UnsafeGetBuffer`) rồi
     `UiSessionServer.RunConnectionAsync` gọi `AuthCoordinator.ZeroRecoveryKeyPlaintextAfterSend()`
     (internal, mới) trong `finally` NGAY SAU `IpcFrameTransport.WriteFrameAsync` — không phải sau khi
     hash xong. `AuthState.PendingSetup` bỏ hẳn field `RecoveryKeyPlaintextUtf8Pinned`/method
     `ZeroSecrets()` (chỉ còn giữ hash — bước Confirm chưa từng cần đọc lại plaintext).
     `GenerateNewRecoveryKeyEntry` đổi trả `byte[] PlaintextUtf8Pinned` thay vì `string Grouped`.
  2. **FAIL 2 — Recovery Key dùng lại được 2 lần** (`HandleRecoveryResetAsync`, thứ tự logic thuần,
     không đụng schema): đảo thứ tự — kiểm tra độ dài `new_password` (>50 ký tự) TRƯỚC KHI verify
     Recovery Key (trước đây verify trước, để lại đường "verify đúng nhưng dừng giữa chừng vì
     new_password quá dài" không ghi `auth.dat` nên Recovery Key không bị đánh dấu `Used`, cũng không
     tính vào rate-limit — key dùng lại được nguyên vẹn). Đã rà `HandleChangePasswordAsync` — KHÔNG
     cùng pattern lỗi (xác thực `old_password` không có khái niệm "used"/tiêu thụ như Recovery Key,
     dừng sớm không để lại trạng thái lấp lửng) nên giữ nguyên thứ tự, có comment giải thích tại chỗ.
  3. **FAIL 3 — Timing oracle lộ trạng thái `auth.dat` corrupt** qua chênh lệch thời gian phản hồi
     (nội dung response đã đồng nhất từ ADR-81/v0.2.0, nhưng thời gian thì chưa): thêm
     `Argon2idHasher.ComputeAndDiscard` (chạy `ComputeHash` thật — cùng hàm dùng cho `Hash`/`Verify` —
     rồi `ZeroMemory` kết quả ngay) + `AuthCoordinator.RunDecoyArgon2idAsync` (gọi hàm trên với
     `_state.DecoySalt`, KHÔNG bọc `Task.Run` vì nhánh verify thật cũng gọi `Argon2idHasher.Verify`
     trực tiếp không offload threadpool — khớp cả pattern lập lịch, không chỉ tổng thời gian) + mới
     `AuthState.DecoySalt` (16 byte sinh 1 lần lúc `AuthCoordinator` ctor, RAM-only, tái dùng suốt vòng
     đời process). Gọi ở đúng vị trí (sau chuẩn hoá input, trước build response) trong cả 3 handler có
     credential-check thật: `HandleAuthVerifyAsync`/`HandleChangePasswordAsync`/`HandleRecoveryResetAsync`,
     nhánh `_dataCorrupt || _cached is null` — KHÔNG áp dụng nhánh `LOCKED_OUT` (tự nó đã công khai
     "đang khoá" có chủ đích, không phải oracle cần che).
  4. **Bug 4 — `AuditLogWriter` hardcode path** (`test-runner` phát hiện, có từ Đợt 0-2, không riêng
     `PWD-xxx`): mọi call site `_auditLog.AppendAsync(...)` trên toàn Service hardcode
     `InstallPaths.AuditLogPath` thay vì dùng path đã `InitializeAsync` — test dùng file tạm cô lập vẫn
     âm thầm ghi đè `%ProgramData%\ParentalGuard\audit.log` production thật, phá hash-chain
     tamper-evident (đã xác nhận thực nghiệm, đã dọn file bị pollute). Không tìm thấy use-case hợp lệ
     nào cần ghi vào path khác path đã init (production luôn dùng đúng `InstallPaths.AuditLogPath` ở cả
     2 đầu) — sửa thẳng API: `AuditLogWriter` lưu `_path` làm state nội bộ lúc `InitializeAsync`,
     `AppendAsync` bỏ tham số `path`, luôn dùng `_path`. Rà + sửa TOÀN BỘ 7 call site (không chỉ
     `AuthCoordinator.cs`/`Worker.cs` như phạm vi ban đầu yêu cầu — đổi API là breaking change nên bắt
     buộc sửa đồng bộ mọi caller để build qua): `AuthCoordinator.cs` (6), `Worker.cs` (2),
     `UiSessionServer.cs` (1), `OverlayDecisionCoordinator.cs` (1), `ChildProcessSupervisor.cs` (2),
     `FailSecureConfigLoader.cs` (1), `AuditLogWriterTests.cs` (4, test project). Dọn `using
     ParentalGuard.Service.Configuration;` không còn dùng ở 3 file (`UiSessionServer`/
     `OverlayDecisionCoordinator`/`ChildProcessSupervisor`) sau khi bỏ `InstallPaths.AuditLogPath`.

- **2026-09-20** — Đợt 5 (Pause/Resume, `PAUSE-001`-`031`, Architecture/02 mục 3a) hoàn thành. 6
  message IPC mới (`ipc.proto` field 92-97), 1 module mới `src/ParentalGuard.Service/Pause/` (5 file:
  `PauseCoordinator`, `PauseDurationCalculator`, `PauseDurationMapper`, `PauseStateRecovery`,
  `PauseFrequencyGuard`), sửa `ChildProcessSupervisor` (cadence heartbeat động, ADR-104),
  `OverlayDecisionCoordinator` (`ClearForPause`, ADR-106), `UiSessionServer` (định tuyến 2 domain),
  `ConfigDb` (`UpdatePauseState`), `Worker.cs` (khôi phục lúc Starting mục 3a.3, wiring
  `PauseCoordinator`). Build 0 Warning/0 Error, test 251/251 (+31 so với Đợt 4: 6 file test mới —
  `PauseCoordinatorTests`/`PauseDurationCalculatorTests`/`PauseStateRecoveryTests`/
  `PauseFrequencyGuardTests` + 1 test mới `ConfigDbTests.UpdatePauseState_ThenReadSnapshot_...` + đã
  tính `Overlay.Tests` không đổi). Không phá vỡ hành vi Đợt 0-4 đã có (220/220 test cũ vẫn pass
  nguyên trước khi thêm test mới). Xác nhận tách biệt `ANTI-060` bằng test chức năng thật (8 chu kỳ
  pause/resume liên tiếp, audit log không có `AttackPatternDetected`/`ProcessRestarted`) thay vì chỉ
  suy luận từ code review — đúng yêu cầu "test xác nhận KHÔNG tăng bộ đếm". 2 khoảng trống ghi nhận ở
  mục "Khoảng trống đã biết — Đợt 5" phía trên (event_type `PauseFrequencyAnomalyDetected` chưa có
  dòng ở Architecture/04, banner Toast chưa verify hiển thị thật).

- **2026-09-20 (audit fix)** — FAIL cứng `TEST-001` (security-privacy-auditor): mọi hàm WRITE trong
  `ConfigDb.cs` (`CreateSchema`, `MigrateFromV1ToV2`, `InsertSchemaMeta`, `InsertMonitoringState`,
  `InsertPauseState`, `UpdatePauseState`, `InsertIpcKey`, `InsertAuditMeta`, `UpsertIconPosition`) gọi
  thẳng `command.ExecuteNonQuery()` KHÔNG bọc try/catch như mọi hàm READ — `SqliteException` (vd. `SQLite
  Error 5: 'database is locked'` do AV/backup/WAL checkpoint khoá file thoáng qua, hoàn toàn tự nhiên,
  không cần tấn công) lọt nguyên bản ra ngoài, phá vỡ fail-secure của `PauseCoordinator.TryPersist` (chỉ
  catch `ConfigLoadException`) — hậu quả: Resume "thành công" về xác thực nhưng `_state = resumedState`
  KHÔNG BAO GIỜ chạy (giám sát ÂM THẦM vẫn Paused, NGƯỢC HƯỚNG fail-secure đã thiết kế), và kết nối UI bị
  hang (exception thoát khỏi `Task.Run` không observe, giết cả vòng lặp `UiSessionServer.RunLoopAsync`,
  không chỉ 1 kết nối). Sửa: bọc TẤT CẢ hàm ghi trên bằng đúng pattern `catch (SqliteException ex) => throw
  new ConfigLoadException(...)` đã dùng nhất quán ở hàm đọc (xem các dòng `ConfigDb.*` phía trên, mỗi dòng
  đã cập nhật ghi chú sửa riêng). Thêm `ConfigLoadException` vào catch của `UiSessionServer.RunLoopAsync`
  làm lưới an toàn tầng ngoài (defense in depth). Thêm overload nội bộ `ConfigDb.Open(string, int?
  busyTimeoutSecondsForTest)` (dùng `SqliteConnectionStringBuilder.DefaultTimeout` — KHÔNG nối chuỗi PRAGMA
  để tránh CA2100 — SQLite PRAGMA không hỗ trợ bind parameter cho vế giá trị) chỉ phục vụ test lock-
  contention nhanh, hành vi production không đổi (caller công khai duy nhất luôn truyền `null`). Test mới:
  `ConfigDbTests.UpdatePauseState_WhileDbLockedByAnotherConnection_ThrowsConfigLoadExceptionNotSqliteException`
  (nhanh, dùng `busyTimeoutSecondsForTest: 1`), `PauseCoordinatorTests.HandlePause_ConfigDbLockedDuringWrite_ReturnsUnspecifiedAndStaysMonitoring`
  + `.HandleResume_ConfigDbLockedDuringWrite_StillAppliesRamStateResumeFailSecure` (đi qua `PauseCoordinator`
  thật với busy timeout mặc định ~30s — chậm nhưng đúng bug thật đã tái hiện, dùng kỹ thuật
  `BEGIN IMMEDIATE TRANSACTION` trên 1 connection SQLite riêng để khoá ghi), và
  `PauseCoordinatorTests.HandlePause_ActionTokenIssuedForDifferentActionContext_ReturnsInvalidToken`
  (permanent hoá kịch bản action_context mismatch security-privacy-auditor đã xác nhận adhoc). Build 0
  Warning/0 Error, không còn file test tạm nào sót lại.

- **2026-09-25 (audit fix)** — FAIL cứng (`security-privacy-auditor`, Đợt 6 giai đoạn 4 `S4` Settings):
  `SettingsViewModel.ChangePasswordAsync` ghi đè `_newRecoveryKeyPlaintextBuffer` bằng Recovery Key mới
  KHÔNG zero buffer CŨ trước — nút "Đổi mật khẩu" không bị khoá lúc Recovery Key mới đang hiển thị chưa
  acknowledge, nên đổi mật khẩu 2 lần liên tiếp (trước khi bấm "Đã lưu" lần đầu) làm plaintext Recovery
  Key #1 trôi nổi không zero trên managed heap vô thời hạn (đọc được qua RAM dump). Sửa 2 lớp: (1)
  `ChangePasswordAsync` — `CryptographicOperations.ZeroMemory` buffer cũ NGAY trước khi ghi đè nếu chưa
  `null`; (2) defense in depth — thêm `SettingsViewModel.CanSubmitChangePassword` (computed
  `IsNotChangingPassword && !HasNewRecoveryKeyDisplay`), đổi `SettingsPage.xaml`
  `ChangePasswordButton.IsEnabled` từ `IsNotChangingPassword` sang `CanSubmitChangePassword` — khoá hẳn
  nút qua UI thật, không chỉ dựa vào lớp 1. Test mới trong `SettingsViewModelTests.cs`:
  `ChangePasswordAsync_CalledTwiceBeforeAcknowledge_ZeroesPreviousRecoveryKeyBuffer` (assert buffer #1
  zero hết sau lần đổi thứ 2, dùng `FakeAuthFacade.EnqueueResult` mới thêm để giả lập 2 lần gọi liên
  tiếp trả 2 kết quả khác nhau), `ChangePasswordAsync_SuccessWithRegenerate_LocksSubmitUntilAcknowledged`
  (assert `CanSubmitChangePassword` `false` lúc hiển thị, `true` lại sau acknowledge). Build 0 Warning/0
  Error, 20/20 `SettingsViewModelTests` pass, 91/91 `ParentalGuard.UI.Tests` pass, không regression (346
  → 348 tổng test toàn solution). **Lưu ý môi trường** (không liên quan bug này): `dotnet test
  ParentalGuard.sln` ở mức solution load nhầm bản build RID-specific
  (`bin\x64\Debug\...\win-x64\ParentalGuard.UI.dll`) bị WDAC/Smart App Control chặn
  (`FileLoadException 0x800711C7`, cùng họ vấn đề đã ghi nhận ở mục `ParentalGuard.Vision.Tests` phía
  trên) — chạy trực tiếp `dotnet test tests/ParentalGuard.UI.Tests/ParentalGuard.UI.Tests.csproj` (build
  không-RID) thì pass sạch; `test-runner` cần chạy theo project riêng cho `ParentalGuard.UI.Tests`, không
  qua `dotnet test` ở mức `.sln`.

- **2026-09-25 (audit fix)** — FAIL cứng (`security-privacy-auditor`, Đợt 6 giai đoạn 5 `S6` Recovery,
  `PWD-032`/`033`): race Cancel/đóng app TRONG LÚC `RecoveryViewModel.SubmitAsync` còn treo IPC (Argon2id
  hash + sinh Recovery Key mới ở Service mất thời gian) khiến `RecoveryViewModel` mồ côi — kịch bản: user
  bấm "Huỷ, quay lại" (`CancelLink`, KHÔNG bị khoá theo `IsBusy` khác `SubmitButton`) → `OnCancelClick`
  điều hướng ngay, không huỷ task đang chạy → `RecoveryPage.OnNavigatedFrom` chạy `AcknowledgeNewRecoveryKeyDisplayed()`
  NGAY LÚC buffer CHƯA tồn tại (no-op) → Service sau đó trả `Success` thật, `SubmitAsync` vẫn set
  `_newRecoveryKeyPlaintextBuffer`/`NewRecoveryKeyDisplay` trên instance đã mồ côi, KHÔNG CÒN ĐƯỜNG NÀO
  zero buffer này nữa (`_activeRecoveryViewModel` đã bị `Unregister` trước đó) — plaintext Recovery Key
  mới tồn tại vô thời hạn trên managed heap tới khi process thoát, vi phạm `PWD-032`/`TEST-001`. Xác nhận
  qua `UiIpcClient.SendRequestAsync`/`DisconnectAsync` dùng chung 1 `SemaphoreSlim _gate`: `DisconnectAsync`
  (gọi từ `App.OnWindowClosed`) BỊ CHẶN chờ request đang treo xong trước khi đóng pipe — nghĩa là đóng app
  giữa chừng (X/Alt+F4) cũng KHÔNG cắt được request, Service vẫn trả `Success` thật vào ViewModel mồ côi
  y hệt kịch bản Cancel (đã kiểm chứng logic thay vì chỉ suy đoán). Rà thêm thấy CÙNG LỚP LỖI đã tồn tại
  từ trước (không do lượt S6 này gây ra) ở `SettingsViewModel.ChangePasswordAsync` qua
  `SettingsPage.ForgotOldPasswordLink` (điều hướng thẳng `S6`, không khoá theo `IsChangingPassword` khác
  `ChangePasswordButton`) — sửa cùng lúc. Sửa theo 2 lớp phòng thủ trên CẢ 2 ViewModel:
  (1) **UI lock** (tránh mất Recovery Key một cách không cần thiết ở đường có thể tránh — Success nghĩa
  là mật khẩu/Recovery Key ĐÃ đổi thật ở Service, huỷ client-side không rollback được, phải hiển thị được
  chứ không được lặng lẽ zero): `RecoveryPage.xaml` `CancelLink.IsEnabled` từ không-bind sang
  `{x:Bind ViewModel.IsNotBusy}` (property mới); `SettingsPage.xaml` `ForgotOldPasswordLink.IsEnabled`
  thêm bind `{x:Bind ViewModel.IsNotChangingPassword}` (property có sẵn). (2) **Fail-secure backstop**
  (bịt kín cả đường UI lock không chặn được — đóng app giữa chừng): thêm field `_discarded`
  (`volatile bool`) + method `MarkDiscarded()` (idempotent) trên CẢ `RecoveryViewModel`/`SettingsViewModel`
  — gọi TRƯỚC mọi điểm rời trang/đóng app hiện có (`RecoveryPage.OnNavigatedFrom`,
  `SettingsPage.OnNavigatedFrom`, `App.OnWindowClosed` cho cả 2 ViewModel); nhánh `Success` trong
  `SubmitAsync`/`ChangePasswordAsync` kiểm tra `_discarded` NGAY khi nhận response — nếu `true`,
  `CryptographicOperations.ZeroMemory` buffer mới nhận ngay lập tức, KHÔNG publish lên
  `NewRecoveryKeyDisplay`. Test mới (cả 2 file, dùng `TaskCompletionSource`/`PendingResult` mới thêm vào
  `FakeAuthFacade` để giữ request "treo" cho tới khi test tự `MarkDiscarded()` rồi mới `SetResult`):
  `RecoveryViewModelTests.SubmitAsync_MarkDiscardedWhilePending_LateSuccessZeroesBufferWithoutPublishing`,
  `SettingsViewModelTests.ChangePasswordAsync_MarkDiscardedWhilePending_LateSuccessZeroesBufferWithoutPublishing`.
  Build 0 Warning/0 Error toàn `.sln`; `dotnet test tests/ParentalGuard.UI.Tests/ParentalGuard.UI.Tests.csproj`
  (per-project, tránh WDAC false-positive đã ghi nhận ở mục trên) 110/110 pass (108 → 110, +2 test mới).

## Đợt 6 (Architecture/10-ui-architecture.md mục 6.6) — `ParentalGuard.UI` giai đoạn 5 (`S6` Recovery)

Phạm vi lượt này: `IAuthFacade`/`AuthFacade` thêm `RecoveryResetAsync` (Service-side `AuthCoordinator`/
proto `RecoveryResetRequest`/`Response` đã có sẵn từ Đợt 3 — chỉ nối dây phía `UI` Facade lần đầu),
`RecoveryViewModel`/`RecoveryPage.xaml(.cs)` mới, `NavigationService.NavigateToRecovery` chuyển từ
`throw NotImplementedException` (stub giai đoạn 1) sang implement thật (`Frame.Navigate` root frame tới
`RecoveryPage`). Không sửa `.proto` (field 88/89 đã đúng khớp Architecture/08 mục 7.5 từ trước, chỉ xác
nhận lại). Cả 2 caller cũ của `NavigateToRecovery` (`AuthPromptDialog` qua `ShowAuthPromptAsync`,
`SettingsPage.OnForgotOldPasswordClick`) không cần sửa vì signature không đổi ngữ nghĩa (`void`→`bool`,
caller đều gọi dạng statement, discard giá trị trả về hợp lệ).

### `src/ParentalGuard.UI/Services/IpcClient/IAuthFacade.cs`, `AuthFacade.cs` (thêm `RecoveryResetAsync`)

| Hàm | Callers | Callees |
|---|---|---|
| `AuthFacade.RecoveryResetAsync` (mới) | `RecoveryViewModel.SubmitAsync` | `UiIpcClient.NewEnvelope`/`.SendRequestAsync`, `MapRecoveryResetResponse`, `CryptographicOperations.ZeroMemory`/`CredentialBytes.Zero` (zero cả 2 buffer pinned gốc + 2 `ByteString` nội bộ trong `finally`, Architecture/08 mục 5.3) |
| `AuthFacade.MapRecoveryResetResponse` (private static, mới) | `RecoveryResetAsync` | `CredentialBytes.UnsafeGetBuffer` (chỉ khi `Success` + `new_recovery_key_plaintext` không rỗng, `PWD-032`/ADR-83) |

Tên POCO record `RecoveryResult`/enum `RecoveryOutcome` (không phải `RecoveryResetResult`/`RecoveryResetOutcome`)
— tránh trùng tên với enum proto `ParentalGuard.Ipc.Protocol.RecoveryResetResult` cùng `using` trong file
(khác namespace nên không lỗi runtime, nhưng cùng tên sẽ khiến type cục bộ che khuất type proto trong các
`switch` cần so khớp giá trị enum proto — đặt tên khác ngay từ đầu để tránh nhầm lẫn khi đọc code).

### `src/ParentalGuard.UI/ViewModels/RecoveryViewModel.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `RecoveryViewModel.NormalizeRecoveryKey` (static, mục 6.2) | `Views/RecoveryPage.OnSubmitClick` (TRƯỚC khi build buffer pinned) | thuần logic bỏ `-`/khoảng trắng + `ToUpperInvariant` — Service vẫn chuẩn hoá lại lần nữa, không tin input UI |
| `RecoveryViewModel.SubmitAsync` | `Views/RecoveryPage.OnSubmitClick` | `IAuthFacade.RecoveryResetAsync` — `Success`: **fix bug security audit Đợt 6 S6** — nếu `_discarded` (trang đã bị rời/app đã đóng trong lúc chờ IPC), `CryptographicOperations.ZeroMemory` buffer mới nhận NGAY, KHÔNG publish; ngược lại zero buffer CŨ trước nếu còn (cùng fix bug security audit đã áp dụng `SettingsViewModel`), decode UTF-8 1 lần → `NewRecoveryKeyDisplay`; `WrongRecoveryKey`/`NewPasswordTooLong`: set `ErrorMessage`; `LockedOut`: khoá `IsSubmitEnabled` + đếm ngược `lockout_until_unix_ms` (đọc trực tiếp field response, không tự tính lại rate-limit, `08` mục 7.7, cùng mẫu hình `AuthPromptViewModel.SubmitAsync`) |
| `RecoveryViewModel.MarkDiscarded` (mới — fix bug security audit Đợt 6 S6, idempotent) | `Views/RecoveryPage.OnNavigatedFrom`, `App.OnWindowClosed` (qua `_activeRecoveryViewModel`) | set field `_discarded = true` (đọc bởi `SubmitAsync` nhánh `Success`) |
| `RecoveryViewModel.AcknowledgeNewRecoveryKeyDisplayed`/`.Dispose` | `Views/RecoveryPage.OnAcknowledgeClick`/`.OnNavigatedFrom`, `App.OnWindowClosed` (qua `RegisterActiveRecoveryViewModel`, safety net BUG B) | `CryptographicOperations.ZeroMemory` (zero buffer Recovery Key mới ngay, idempotent) |
| `RecoveryViewModel.IsFormVisible`/`.HasNewRecoveryKeyDisplay`/`.HasError` (computed) | `Views/RecoveryPage.xaml` (`Visibility` binding) | — |
| `RecoveryViewModel.CanSubmit` (computed, defense in depth chống double-submit — cùng mẫu hình `SettingsViewModel.CanSubmitChangePassword`) | `Views/RecoveryPage.xaml` (`SubmitButton.IsEnabled`, thay `IsSubmitEnabled` đơn thuần) | `IsSubmitEnabled && !IsBusy` |
| `RecoveryViewModel.IsNotBusy` (mới, computed — fix bug security audit Đợt 6 S6) | `Views/RecoveryPage.xaml` (`CancelLink.IsEnabled`) | `!IsBusy` (tránh Cancel huỷ 1 request đã commit thành công server-side, làm mất Recovery Key mới không cần thiết) |

### `src/ParentalGuard.UI/Views/RecoveryPage.xaml(.cs)` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `RecoveryPage` ctor | `NavigationService.NavigateToRecovery` (`Frame.Navigate`) | `new RecoveryViewModel` (Facade lấy qua `App.Services`), set label tĩnh qua `LocalizationService.Get` |
| `RecoveryPage.OnNavigatedTo`/`.OnNavigatedFrom` | Windows App SDK (`Frame` navigation lifecycle) | `App.RegisterActiveRecoveryViewModel`/`.UnregisterActiveRecoveryViewModel`, `RecoveryViewModel.MarkDiscarded` (fix bug security audit Đợt 6 S6 — TRƯỚC Acknowledge, phòng `SubmitAsync` còn treo IPC), `.AcknowledgeNewRecoveryKeyDisplayed` (safety net rời trang khi Recovery Key mới còn hiển thị) |
| `RecoveryPage.OnSubmitClick` | nút "Khôi phục" (XAML event) | đọc `RecoveryKeyInput.Text`/2 `PasswordBox` → biến cục bộ, clear cả 3 input NGAY (Architecture/08 mục 5.3, cùng mẫu hình `SettingsPage.OnChangePasswordClick`) TRƯỚC guard rỗng/khớp mật khẩu, `RecoveryViewModel.NormalizeRecoveryKey`, `RecoveryViewModel.SubmitAsync` (byte[] pinned) |
| `RecoveryPage.OnCancelClick` (mới, không mô tả tường minh ở Architecture mục 6.6 — bổ sung tối thiểu để có lối thoát khỏi `S6` nếu vào nhầm, tránh kẹt màn hình toàn cửa sổ không có back button nào khác; `CancelLink.IsEnabled` bind `ViewModel.IsNotBusy` — thêm ở fix bug security audit Đợt 6 S6) | HyperlinkButton "Huỷ, quay lại" (XAML event) | `NavigationService.NavigateToMainShell` |
| `RecoveryPage.OnAcknowledgeClick` | nút "Đã lưu" trên Recovery Key mới (XAML event) | `RecoveryViewModel.AcknowledgeNewRecoveryKeyDisplayed`, `NavigationService.NavigateToMainShell` (mục 6.6: "SUCCESS → ... → quay lại Main Shell") |
| `RecoveryPage.OnCopyClick` | nút "Sao chép" (XAML event) | `Clipboard.SetContent` (cùng residual risk đã ghi nhận ở `OnboardingRecoveryKeyPage`) |

Không tạo `UserControl RecoveryKeyDisplayControl` riêng — cùng lý do đã ghi nhận ở giai đoạn 4
(`SettingsPage`): không có `UserControl` như vậy trong code thật, chỉ inline `TextBlock`/`Button`, lặp lại
đúng pattern đã dùng ở `OnboardingRecoveryKeyPage`/`SettingsPage` (DEV-026).

### `src/ParentalGuard.UI/Services/NavigationService.cs` (sửa — implement `NavigateToRecovery`)

| Hàm | Callers | Callees |
|---|---|---|
| `NavigationService.NavigateToRecovery` (sửa, trả `bool` khớp các `Navigate*` khác thay vì `void`) | `ShowAuthPromptAsync`, `Views/SettingsPage.OnForgotOldPasswordClick` | `Navigate(typeof(RecoveryPage))` |

### `src/ParentalGuard.UI/App.xaml.cs` (sửa — safety net BUG B cho `RecoveryViewModel`)

| Hàm | Callers | Callees |
|---|---|---|
| `App.RegisterActiveRecoveryViewModel`/`.UnregisterActiveRecoveryViewModel` (mới) | `Views/RecoveryPage.OnNavigatedTo`/`.OnNavigatedFrom` | set/clear field `_activeRecoveryViewModel` |
| `App.OnWindowClosed` (sửa — thêm nhánh Recovery) | Windows App SDK (`Window.Closed`) | `RecoveryViewModel.MarkDiscarded` (fix bug security audit Đợt 6 S6, TRƯỚC `Dispose` — `UiIpcClient.DisconnectAsync` bên dưới bị chặn chờ request `SubmitAsync` đang treo xong nên vẫn có thể nhận `Success` thật sau khi window đã đóng), `.Dispose` (cùng lý do BUG B đã sửa cho `OnboardingViewModel`/`SettingsViewModel` — đóng app giữa chừng khi Recovery Key mới còn hiển thị vẫn phải zero buffer) |

### Test mới (`tests/ParentalGuard.UI.Tests/`)

`AuthFacadeTests.cs` — thêm mapping 4 outcome `RecoveryResetResult` (proto) → `RecoveryOutcome`, verify
request gửi đúng `recovery_key`/`new_password`, `lockout_until_unix_ms` đọc đúng khi `LockedOut`,
`NewRecoveryKeyPlaintextUtf8` giải mã đúng UTF-8 khi `Success`. `RecoveryViewModelTests.cs` (mới) —
`NormalizeRecoveryKey` (bỏ `-`/khoảng trắng, hoa hoá, 3 case); `SubmitAsync` 4 outcome (`Success` ẩn form/
hiện Recovery Key mới, `WrongRecoveryKey` giữ form, `LockedOut` khoá `IsSubmitEnabled`, `NewPasswordTooLong`
set lỗi) + lỗi kết nối (`UiIpcConnectionException`); `AcknowledgeNewRecoveryKeyDisplayed` zero buffer;
`Dispose` (safety net BUG B — có buffer chưa acknowledge phải zero, không buffer thì no-op, cùng mẫu hình
regression test `OnboardingViewModelTests`/`SettingsViewModelTests`); `CanSubmit` `false` khi `IsBusy=true`
(defense in depth chống double-submit). 3 `FakeAuthFacade` sẵn có
(`OnboardingViewModelTests`/`DashboardViewModelTests`/`SettingsViewModelTests`) đều phải thêm implement
`RecoveryResetAsync` (`throw NotSupportedException`) do `IAuthFacade` có thêm method mới — không đổi hành
vi test hiện có, chỉ để interface biên dịch được. Build 0 Warning/0 Error toàn `.sln`; per-project
(tránh WDAC false-positive đã ghi nhận ở mục trên): `ParentalGuard.UI.Tests` 108/108 pass (+17 so với
baseline 91 trước lượt này), `ParentalGuard.Service.Tests` 183/183, `ParentalGuard.Watchdog.Tests` 2/2,
`ParentalGuard.Overlay.Tests` 18/18, `ParentalGuard.Uninstaller.Tests` 9/9 — không regression.
`ParentalGuard.Vision.Tests` vẫn gặp đúng WDAC/Smart App Control false-positive đã ghi nhận (không liên
quan lượt này, không đụng tới `ParentalGuard.Vision`): 3/45 pass qua được trước khi
`FileLoadException 0x800711C7` chặn phần còn lại — môi trường, không phải regression thật. Tổng test
toàn solution: 348 → **365** (+17, đúng số test mới thêm).

## Đợt 7 (Architecture/05-image-pipeline-architecture.md mục 3.6/3.7/3.8, `PERF-010`/`011`, `IMG-011`) — Adaptive Frame Rate + Perceptual Hashing

Phạm vi lượt này đúng 2 phía: **`Vision`** — Window Message Pump (thread thứ 3, mục 3.6), dHash 64-bit
viết tay (mục 3.8.1, ADR-128) + hash-gate skip resize/inference khi nội dung không đổi (`PERF-011`),
field mới `content_changed` (proto field 8, ADR-134); **`Service`** — `AdaptiveFrameRateCoordinator`
mới (mục 3.7, `PERF-010`) — state machine 2 bucket `Vigilant`/`Relaxed` + hysteresis bất đối xứng
(ADR-130) + cờ `Boost` độc lập (mục 3.7.3), thay hẳn cơ chế TĨNH cũ (`state.CaptureIntervalBaselineMs`
gửi thẳng) — không có 2 cơ chế song song. `performance_mode` (Đợt 6, trước đây tồn tại CHỈ ở
`ipc.proto`/`ParentalGuard.UI`, **CHƯA từng có** ở `MonitoringStateData`/`ConfigDb`/`Worker` — gap thật
phát hiện lúc build, xem "Ghi chú gap" cuối mục) nay thêm vào `MonitoringStateData`/`ConfigDb` làm
**trần nới lỏng tối đa** cho bucket `Relaxed` (ADR-131), mặc định `Balanced`.

### Ghi chú gap phát hiện lúc build (không thuộc phạm vi Đợt 7, không tự sửa)

1. **`ipc.proto` `VisionInferenceResult` thiếu hẳn field 7 (`process_name`, `MISC-030`/ADR-111,
   Đợt 6 v0.7.1)** — `Architecture/03-ipc-communication.md` ghi đã "amend" từ Đợt 6 nhưng file
   `.proto` thật (`src/ParentalGuard.Ipc/Protos/ipc.proto`) chưa từng có field này, và không nơi nào
   trong code thật (`Vision`/`Service`) đọc/ghi `ProcessName` của `VisionInferenceResult`. Đã thêm 1
   dòng comment giữ chỗ field 7 trong `.proto` (không implement) — cần `feature-dev` lượt khác xử lý
   đúng `MISC-030`, không lẫn vào Đợt 7.
2. ~~**`ConfigQuery`/`ConfigUpdateRequest`/`RemoveWhitelistEntryRequest` (`S4` Settings, Đợt 6) chưa có
   handler ở `Service`**~~ — **ĐÃ ĐÓNG, xem mục "Đợt 7 gap fix (test-runner)" bên dưới.**

### `src/ParentalGuard.Vision/Capture/PerceptualHash.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `PerceptualHash.ComputeDHash64` (static) | `FrameClassificationPipeline.ProcessFrame` | `Span<byte>.Clear()` (zero `luma[9,8]` trong `finally`, mục 3.8.3 — stack-local, không escape method nên không cần `IFrameBufferAuditor`, khác các buffer heap tái dùng ở bảng mục 6) |

### `src/ParentalGuard.Vision/Pipeline/WindowHashCache.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `WindowHashCache.ResolveContentChanged` | `FrameClassificationPipeline.ProcessFrame` | — |
| `WindowHashCache.UpdateRiskScore` | `FrameClassificationPipeline.ProcessFrame` (chỉ khi `content_changed=true`, sau khi classify xong) | — |
| `WindowHashCache.EndCycle` | `FrameClassificationPipeline.EndCaptureCycle` | — |

### `src/ParentalGuard.Vision/Pipeline/FrameClassificationPipeline.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `FrameClassificationPipeline.ProcessFrame` (sửa — chèn hash-gate giữa readback và resize, mục 3.8) | `Process`, test (`FrameClassificationPipelineZeroOutTests`/`FrameClassificationPipelineHashGateTests`) | `PerceptualHash.ComputeDHash64` (mới, luôn chạy), `WindowHashCache.ResolveContentChanged`/`.UpdateRiskScore` (mới), `FrameResizerNormalizer.Resize`/`_classifier.Classify` (nay CÓ ĐIỀU KIỆN — chỉ khi `content_changed=true`) |
| `FrameClassificationPipeline.EndCaptureCycle` (mới, public) | `CaptureLoopWorker.ProcessCycle` | `WindowHashCache.EndCycle` |

Zero-out `input_tensor`/`pixel_buffer_bgra8` (bảng mục 6) nay **có điều kiện đúng theo v0.3.0**: pixel
buffer luôn zero (hash luôn chạy), tensor CHỈ zero khi `content_changed=true` (Resize/Classify chưa từng
chạm tensor khi skip — tensor giữ nguyên trạng zero từ chu kỳ trước). **Regression test cập nhật**:
`FrameClassificationPipelineZeroOutTests.ProcessFrame_CropperThrowsAfterPartialWrite_...` đổi assertion
`ZeroedCount("input_tensor")` từ `1` → `0` (cropper throw TRƯỚC hash-gate, tensor chưa từng bị chạm) —
đã ghi rõ lý do trong comment tại chỗ, không phải nới lỏng bất biến `IMG-003` (tensor vẫn provably luôn
là 0 giữa 2 lần gọi, chỉ khác chỗ AI/khi nào zero được gọi).

### `src/ParentalGuard.Vision/Capture/WindowMessagePump.cs` (mới, ADR-133)

| Hàm | Callers | Callees |
|---|---|---|
| `WindowMessagePump.Start` | `Program.cs` (top-level) | `Thread` mới (`IsBackground=true`) chạy `Run` |
| `WindowMessagePump.Run` (private, chạy trên Thread thứ 3) | `Start` | `RegisterClassEx`/`CreateWindowEx`/`SetWinEventHook`/`GetMessage`/`TranslateMessage`/`DispatchMessage` (P/Invoke `user32`/`kernel32`) |
| `WindowMessagePump.WndProcImpl` (private, callback native) | `DispatchMessage` (native) | set `_displayChanged` khi `WM_DISPLAYCHANGE` |
| `WindowMessagePump.OnWinEvent` (private, callback native) | `SetWinEventHook` (native, khi `EVENT_SYSTEM_FOREGROUND`) | `Action _onForegroundChanged` (= `CaptureLoopWorker.WakeUp`, truyền từ `Program.cs`) |
| `WindowMessagePump.ConsumeDisplayChanged` | `CaptureLoopWorker.Run` (đầu mỗi vòng lặp) | `Interlocked.Exchange` |

### `src/ParentalGuard.Vision/Pipeline/CaptureLoopWorker.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `CaptureLoopWorker.AttachMessagePump` (mới) | `Program.cs` (sau khi tạo `WindowMessagePump` bằng chính `captureLoop.WakeUp` — phá vòng phụ thuộc constructor 2 chiều) | set field `_messagePump` |
| `CaptureLoopWorker.Run` (sửa — thêm bước tiêu thụ `WM_DISPLAYCHANGE`) | `Start` (Thread mới) | `_messagePump?.ConsumeDisplayChanged()` (mục 3.6 — hiện tại KHÔNG đổi hành vi enumerate vì `MonitorSelector.EnumerateOutputs` đã chạy lại mỗi chu kỳ từ Đợt 2, giữ đúng hợp đồng thiết kế cho lúc enumerate được cache sau này) |
| `CaptureLoopWorker.ProcessCycle` (sửa — thêm `usedWindowHandles`) | `Run` | `FrameClassificationPipeline.EndCaptureCycle` (mới, cuối mỗi chu kỳ) |

### `src/ParentalGuard.Vision/Program.cs` (sửa)

Khởi tạo `WindowMessagePump` NGAY SAU `CaptureLoopWorker` (cần `captureLoop.WakeUp`), gọi
`captureLoop.AttachMessagePump(messagePump)` rồi mới `messagePump.Start()`/`captureLoop.Start()`.

### `src/ParentalGuard.Ipc/Protos/ipc.proto` (sửa)

Thêm `bool content_changed = 8;` vào `VisionInferenceResult` (ADR-134) — additive, không đổi field cũ
(đúng ADR-16). **Lưu ý môi trường phát hiện lúc build**: `obj/`/`bin/` của `ParentalGuard.Ipc` có thể
cache code sinh ra từ `.proto` CŨ nếu chỉ build incremental — phải `rm -rf src/ParentalGuard.Ipc/obj
src/ParentalGuard.Ipc/bin` rồi build lại project này riêng trước khi build toàn `.sln` mỗi khi sửa
`.proto` (đã áp dụng đúng ở lượt này, không phải lỗi mới).

### `src/ParentalGuard.Service/Data/MonitoringStateData.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `MonitoringStateData` (record, thêm field `PerformanceMode`) | `ConfigDb.ReadMonitoringState`/`.InsertMonitoringState`, `Worker.ExecuteAsync` (qua `config.MonitoringState`) | — |
| `MonitoringStateData.CreateFirstRunDefault`/`.CreateFailSecureDefault` (sửa — thêm `PerformanceMode: PerformanceMode.Balanced`) | `ConfigDb.CreateFresh` (test + `FailSecureConfigLoader`) | — |

### `src/ParentalGuard.Service/Data/ConfigDb.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `ConfigDb.ToJsonValue`/`.ParsePerformanceMode` (mới, private static) | `InsertMonitoringState`/`ReadMonitoringState` | — |
| `ConfigDb.InsertMonitoringState`/`.ReadMonitoringState` (sửa — thêm field `performance_mode`) | `CreateFresh`/`ReadSnapshot` | `ToJsonValue`/`ParsePerformanceMode` |

`MonitoringStateJson.PerformanceMode` (string, default `"balanced"`) — backward-compat: `config.db` ghi
TRƯỚC Đợt 7 (thiếu key này trong JSON) đọc lại vẫn ra `Balanced` (default property initializer +
`ParsePerformanceMode` fallback), không throw `ConfigLoadException`.

### `src/ParentalGuard.Service/Performance/AdaptiveFrameRateCoordinator.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `AdaptiveFrameRateCoordinator.HandleVisionResult` | `Worker.HandleVisionResultAsync` (local function, kênh Vision) | `UpdateVigilance`/`UpdateBoost`/`ComputeDesiredIntervalMs`/`Prune` (private) |
| `AdaptiveFrameRateCoordinator.CurrentIntervalMs` (property) | `Worker.BuildControlVisionCommand` (mọi call site — initial push Vision, `PauseCoordinator` build delegate, `HandleVisionResultAsync` khi đổi) | — |

RAM-only, `Dictionary<ulong window_handle, WindowFrameRateState>`, reset rỗng khi `Service` restart
(ADR-132) — không có bảng `config.db` nào cho domain-state này.

### `src/ParentalGuard.Service/Worker.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `Worker.BuildControlVisionCommand` (sửa — thêm tham số `uint captureIntervalMs`, KHÔNG còn đọc `state.CaptureIntervalBaselineMs`) | lambda initial push `_visionSupervisor` (field mới `_adaptiveFrameRateCoordinator!.CurrentIntervalMs`), `PauseCoordinator` build delegate (cùng field), `HandleVisionResultAsync` (giá trị mới từ `AdaptiveFrameRateCoordinator.HandleVisionResult`) | — |
| `Worker.HandleVisionResultAsync` (mới, local function trong `ExecuteAsync` — thay lambda cũ gọi thẳng `_overlayDecisionCoordinator.HandleVisionResultAsync`) | `_visionSupervisor` (`onBusinessMessage`, kênh Vision) | `OverlayDecisionCoordinator.HandleVisionResultAsync` (như cũ) RỒI `AdaptiveFrameRateCoordinator.HandleVisionResult` — nếu trả về interval mới, `_visionSupervisor.TryEnqueueBusinessMessage` gửi `ControlVisionCommand` mới (mục 3.7.4 điểm 5 — chỉ gửi khi thực sự đổi) |

### Test mới/sửa

- `tests/ParentalGuard.Vision.Tests/PerceptualHashTests.cs` (mới, 4 test): 2 ảnh giống hệt → Hamming=0;
  ảnh phẳng đồng nhất (đen/trắng) → Hamming=0 (giới hạn dHash, ghi rõ trong test); caro vs đảo ngược →
  Hamming > ngưỡng; nhiễu nhỏ ±1 trên gradient → Hamming ≤ ngưỡng.
- `tests/ParentalGuard.Vision.Tests/WindowHashCacheTests.cs` (mới, 8 test): lần đầu gặp → `true`; hash
  giống hệt → `false` + tái dùng `CachedRiskScore`; trong/ngoài ngưỡng Hamming; luôn so với frame NGAY
  TRƯỚC (không phải frame gốc); evict đúng sau 5 chu kỳ liên tiếp không dùng, giữ nguyên nếu dùng lại
  hoặc dưới 5 chu kỳ.
- `tests/ParentalGuard.Vision.Tests/FrameClassificationPipelineHashGateTests.cs` (mới, 5 test): pixel
  giống hệt → skip classify + tái dùng risk score; vẫn điền `Bbox`/`CapturedAtUnixMs` khi skip; pixel
  khác hẳn → classify lại; cửa sổ khác `hwnd` độc lập (luôn `true` lần đầu); evict qua
  `EndCaptureCycle` sau 5 chu kỳ → coi như lần đầu gặp lại.
- `tests/ParentalGuard.Vision.Tests/FrameClassificationPipelineZeroOutTests.cs` (sửa 1 assertion, xem
  ghi chú ở mục `FrameClassificationPipeline.cs` trên).
- `tests/ParentalGuard.Service.Tests/AdaptiveFrameRateCoordinatorTests.cs` (mới, 11 test): giữ Vigilant
  dưới `N_RELAX`; relax đúng tại `N_RELAX` ở `balanced`; lên lại Vigilant NGAY (không streak) sau 1
  frame đổi; `maximum_protection` không bao giờ relax dưới 1000ms; `Boost` độc lập đè lên bucket khi
  gần ngưỡng; không boost khi lệch xa hoặc đã ở/trên ngưỡng; MIN(interval) trên nhiều cửa sổ; không gửi
  lại khi interval không đổi (tránh spam IPC); evict cửa sổ idle > 5×interval hiện hành không còn tính
  vào MIN.
- `tests/ParentalGuard.Service.Tests/ConfigDbTests.cs` (thêm 2 test): round-trip `performance_mode`
  `Balanced`/`MaximumProtection` qua `CreateFresh`→`ReadSnapshot`.

Build 0 Warning/0 Error toàn `.sln` (Debug lẫn Release). Test per-project (tránh WDAC false-positive):
`ParentalGuard.Vision.Tests` 45 → **62** (+17), `ParentalGuard.Service.Tests` 183 → **196** (+13),
`ParentalGuard.Overlay.Tests` 18/18, `ParentalGuard.Watchdog.Tests` 2/2,
`ParentalGuard.Uninstaller.Tests` 9/9, `ParentalGuard.UI.Tests` 110/110 — không regression. Tổng test
toàn solution: 367 → **397** (+30, đúng số test mới thêm; baseline 367 = 365 ghi ở lượt trước + 2 lệch
do thay đổi nhỏ ở `ParentalGuard.UI.Tests` giữa 2 lượt không được ghi riêng).

## Đợt 7 gap fix (test-runner, 2026-09-25/26) — 2 gap phát hiện sau verify Đợt 7

### GAP 1 — `CaptureLoopWorker` thiếu nhánh `Wait()` vô hạn khi foreground bị exclude-list (`PERF-010` dòng 1)

`Architecture/05-image-pipeline-architecture.md` mục 3.3 (v0.3.0): khi foreground app bị exclude-list
**và không còn candidate nào khác** (kể cả trên màn hình khác, multi-monitor `BE-073a`), `Vision` phải
park **vô hạn** (0% CPU capture), chỉ đánh thức bởi `WindowMessagePump` (`EVENT_SYSTEM_FOREGROUND`)
hoặc `ControlVisionCommand` mới — KHÔNG polling theo `capture_interval_ms`.

### `src/ParentalGuard.Vision/Pipeline/CaptureLoopWorker.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `CaptureLoopWorker.Run` (sửa — nhánh mới) | `Start` (Thread riêng) | `ProcessCycle` (nay trả `bool`) → nếu `true`, `WaitOnEvent(Timeout.Infinite, ...)` thay vì `WaitOnEvent(config.CaptureIntervalMs, ...)` |
| `CaptureLoopWorker.ProcessCycle` (sửa — trả `bool`, tính `fgInExcludeList` tách riêng khỏi `fgExcluded`) | `Run` | `ShouldParkInfinitely(fgInExcludeList, candidates.Count)` (mới) |
| `CaptureLoopWorker.ShouldParkInfinitely` (mới, `internal static`, hàm thuần) | `ProcessCycle`, test (`CaptureLoopWorkerShouldParkInfinitelyTests`) | — |

`fgInExcludeList` (chỉ `true` khi `fgHwnd != IntPtr.Zero` VÀ nằm trong exclude-list) tách khỏi
`fgExcluded` (đã có từ Đợt 2, dùng cho `CandidateWindowSelector` — bao gồm cả case `fgHwnd == IntPtr.Zero`)
— tránh park vô hạn nhầm khi lý do rỗng candidate KHÔNG phải exclude-list (vd tạm thời không có cửa sổ
foreground nào, có thể tự phục hồi ở interval kế tiếp mà không có `EVENT_SYSTEM_FOREGROUND`). Không đổi
hành vi nhánh Pause (`!config.MonitoringEnabled`, đã park vô hạn từ Đợt 1) hay nhánh multi-monitor còn
candidate khác (`BE-073a`, vẫn tiếp tục polling theo interval bình thường).

`tests/ParentalGuard.Vision.Tests/CaptureLoopWorkerShouldParkInfinitelyTests.cs` (mới, 4 test): foreground
bị exclude + không candidate → `true`; foreground bị exclude nhưng còn candidate màn hình khác → `false`;
foreground không bị exclude + không candidate (lý do khác) → `false`; có candidate bình thường → `false`.

### GAP 2 — `ConfigQuery`/`ConfigUpdateRequest`/`RemoveWhitelistEntryRequest`/`AuditLogQuery`/`MarkFalsePositiveRequest` chưa có handler (`10-ui-architecture.md` mục 5/6.3/6.4)

Trước lượt này: `UiSessionServer.DispatchAsync` route MỌI message ngoài Pause/Resume/Status sang
`AuthCoordinator.HandleAsync` — 5 message trên rơi vào `default` case, throw `InvalidOperationException`
(UI nhận lỗi kết nối, không phải response hợp lệ). Đồng thời phát hiện `MonitoringStateData`/`ConfigDb`
**chưa từng có** field `overlay_message`/`user_whitelisted_process_names` (dù `Architecture/04-data-architecture.md`
mục 3.3 v0.3.0 đã mô tả schema JSON có 2 field này từ Đợt 6) — chỉ `performance_mode` (Đợt 7) đã có sẵn.

### `src/ParentalGuard.Service/Data/MonitoringStateData.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `MonitoringStateData` (record, thêm field `OverlayMessage`/`UserWhitelistedProcessNames`) | `ConfigDb.ReadMonitoringState`/`.InsertMonitoringState`/`.UpdateMonitoringState`, `Worker.ExecuteAsync` (qua `MonitoringStateHolder.Current`), `ConfigCoordinator`, `Worker.BuildControlVisionCommand` | — |
| `MonitoringStateData.CreateFirstRunDefault`/`.CreateFailSecureDefault` (sửa — thêm `OverlayMessage: ""`, `UserWhitelistedProcessNames: []`) | `ConfigDb.CreateFresh` (test + `FailSecureConfigLoader`) | — |

### `src/ParentalGuard.Service/Data/ConfigDb.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `ConfigDb.InsertMonitoringState`/`.ReadMonitoringState` (sửa — thêm 2 field JSON) | `CreateFresh`/`ReadSnapshot` | `MonitoringStateJson.OverlayMessage`/`.UserWhitelistedProcessNames` (mới, default `""`/`[]` — backward-compat `config.db` ghi trước Đợt 7) |
| `ConfigDb.UpdateMonitoringState` (mới, public — cùng mẫu hình `UpdatePauseState`) | `ConfigCoordinator.TryPersist` | `SqliteCommand` (`UPDATE monitoring_state ...`) |

### `src/ParentalGuard.Service/Data/MonitoringStateHolder.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `MonitoringStateHolder.Current`/`.Update` (cùng mẫu hình `VisionRuntimeConfigHolder` bên `Vision`) | `Worker.ExecuteAsync` (mọi closure trước đây đọc `config.MonitoringState` — `OverlayDecisionCoordinator`/`AdaptiveFrameRateCoordinator`/`BuildControlVisionCommand`/`PauseCoordinator` delegate), `ConfigCoordinator` (đọc + `.Update` sau khi persist thành công) | — |

Lý do bắt buộc thêm holder: trước gap fix này, `MonitoringStateData` chỉ đọc 1 lần lúc `Worker.ExecuteAsync`
khởi động (biến local `config`, immutable, không có đường ghi lại lúc runtime) — nay `ConfigUpdateRequest`/
`RemoveWhitelistEntryRequest`/`MarkFalsePositiveRequest` cần ghi lại RAM + `config.db` và có hiệu lực NGAY
cho các closure đã đọc giá trị này trước đó (risk_threshold, performance_mode, exclude list gửi Vision)
mà không cần restart `Service`.

### `src/ParentalGuard.Service/Config/OverlayMessageValidator.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `OverlayMessageValidator.Validate` (hàm thuần) | `ConfigCoordinator.HandleConfigUpdateAsync`, test (`OverlayMessageValidatorTests`) | — |

`FE-012`/`FE-012a`: `TooLong` nếu > 255 ký tự; `InvalidCharacters` nếu có ký tự ngoài chữ cái (kể cả có
dấu tiếng Việt)/chữ số/khoảng trắng/dấu câu cho phép (`. , ! ? : ; - ( ) " '`) — bao gồm cả emoji/symbol
cấm liệt kê tường minh lẫn ký tự điều khiển (`char.IsControl` kiểm tra TRƯỚC `IsWhiteSpace` vì tab/newline
cũng khớp `IsWhiteSpace`).

### `src/ParentalGuard.Service/Config/ConfigCoordinator.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `ConfigCoordinator.HandleAsync` | `UiSessionServer.DispatchAsync` (`ConfigQuery`/`ConfigUpdateReq`/`RemoveWhitelistReq`) | `HandleConfigQueryAsync`/`.HandleConfigUpdateAsync`/`.HandleRemoveWhitelistAsync` (private) |
| `ConfigCoordinator.HandleConfigQueryAsync` | `HandleAsync` | `MonitoringStateHolder.Current` (đọc RAM, không I/O — mục 6.4 "không gate") |
| `ConfigCoordinator.HandleConfigUpdateAsync` | `HandleAsync` | `OverlayMessageValidator.Validate`, `TryPersist`, `MonitoringStateHolder.Update`, `AuditLogWriter.AppendAsync` (`ConfigChanged`, chỉ khi field thực sự đổi), `pushControlVisionCommand` (chỉ khi `performance_mode` đổi) |
| `ConfigCoordinator.HandleRemoveWhitelistAsync` | `HandleAsync` | `AuthCoordinator.TryConsumeActionTokenAsync` (`manage_whitelist`, ADR-122), `TryPersist`, `MonitoringStateHolder.Update`, `pushControlVisionCommand`, `AuditLogWriter.AppendAsync` |
| `ConfigCoordinator.TryAddUserWhitelistEntryAsync` (public — 1 nguồn ghi whitelist duy nhất) | `HandleRemoveWhitelistAsync` (gián tiếp qua logic riêng), `AuditLogCoordinator.HandleMarkFalsePositiveAsync` | `TryPersist`, `MonitoringStateHolder.Update`, `pushControlVisionCommand` |
| `ConfigCoordinator.TryPersist` (private) | `HandleConfigUpdateAsync`/`.HandleRemoveWhitelistAsync`/`.TryAddUserWhitelistEntryAsync` | `ConfigDb.Open`/`.UpdateMonitoringState` |

`pushControlVisionCommand` (`Action`, tiêm từ `Worker.ExecuteAsync`) — gọi lại đúng
`Worker.BuildControlVisionCommand` hiện có (nay UNION `ExcludeProcessNames` ∪ `UserWhitelistedProcessNames`
trước khi gửi Vision, xem bên dưới) qua `_visionSupervisor.TryEnqueueBusinessMessage`, cùng cơ chế đã có
từ Đợt 5/7 (`PauseCoordinator`/`AdaptiveFrameRateCoordinator`) — không thêm message IPC mới.

### `src/ParentalGuard.Service/Audit/AuditLogWriter.cs` (sửa — thêm đọc phân trang)

| Hàm | Callers | Callees |
|---|---|---|
| `AuditLogWriter.ReadPageAsync` (mới, public) | `AuditLogCoordinator.HandleAuditLogQueryAsync` | `TryParseEntry` (private, mới — bỏ qua dòng parse lỗi thay vì ném lỗi cả trang) |

Dùng chung `_writeLock` với `AppendAsync` (tránh đọc trúng dòng đang ghi dở). Trả về mới nhất trước
(quyết định implement, không phải yêu cầu spec tường minh) + `has_more`. `AuditLogEntryRaw` (record mới,
namespace `ParentalGuard.Service.Audit`) — `ProcessName`/`RiskScore` chỉ set khi `event_type="ContentBlocked"`
(đọc đúng field `processName`/`riskScore` camelCase theo schema `04-data-architecture.md` mục 5.1).

### `src/ParentalGuard.Service/Audit/AuditLogCoordinator.cs` (mới)

| Hàm | Callers | Callees |
|---|---|---|
| `AuditLogCoordinator.HandleAsync` | `UiSessionServer.DispatchAsync` (`AuditLogQuery`/`MarkFalsePositiveReq`, truyền thêm `AuditLogViewSession` của đúng kết nối hiện tại) | `HandleAuditLogQueryAsync`/`.HandleMarkFalsePositiveAsync` (private) |
| `AuditLogCoordinator.HandleAuditLogQueryAsync` | `HandleAsync` | `AuthCoordinator.TryConsumeActionTokenAsync` (`view_audit_log`, `PWD-020`, chỉ khi `action_token` không rỗng) hoặc kiểm tra `session.GateOpenUntilUnixMs` (token rỗng — trang kế tiếp cùng phiên xem), `AuditLogWriter.ReadPageAsync` |
| `AuditLogCoordinator.HandleMarkFalsePositiveAsync` | `HandleAsync` | `AuthCoordinator.TryConsumeActionTokenAsync` (`manage_whitelist`), `ConfigCoordinator.TryAddUserWhitelistEntryAsync`, `AuditLogWriter.AppendAsync` (`ConfigChanged`) |

**Quyết định implement (uỷ quyền tường minh ở `10-ui-architecture.md` mục 6.3)**: `action_token` chỉ bắt
buộc hợp lệ ở request đầu tiên của 1 phiên xem `AuditLogQuery` — token rỗng ở các trang kế tiếp được chấp
nhận nếu còn trong cửa sổ tái sử dụng **10 phút** kể từ lần validate hợp lệ gần nhất. Con số 10 phút là UX,
không phải giá trị bảo mật (khác TTL 15 giây của bản thân `action_token`, `AuthState.PendingActionToken.Ttl`).

**2026-09-28 audit fix (FAIL cứng, 2 vòng security-privacy-auditor độc lập xác nhận bằng PoC thật)**: bản
gốc lưu gate ở field `_viewGateOpenUntilUnixMs` **cấp instance của `AuditLogCoordinator`** — mà coordinator
này chỉ tạo 1 lần cho toàn vòng đời `Service` (`Worker.cs`), dùng CHUNG cho mọi kết nối UI kế tiếp. Biện
minh ban đầu ("chấp nhận được vì `UI` single-instance qua named `Mutex`, ADR-117a") KHÔNG hợp lệ — chính
`ADR-117a` ghi rõ đây là "UX polish thuần, KHÔNG PHẢI yêu cầu bảo mật", không thể dùng để nới lỏng gate
`PWD-020`. Hệ quả thật: phụ huynh xác thực + xem trang 0 xong đóng app, trong 10 phút sau đó BẤT KỲ ai tự
mở lại `ParentalGuard.UI.exe` đã cài (kể cả đứa trẻ bị giám sát — `VerifyClientIdentity` chỉ kiểm tra
đường dẫn exe, chưa có code-signing 3b) gửi `AuditLogQuery` token RỖNG đều đọc được toàn bộ audit log
không cần mật khẩu — cả 2 auditor độc lập đã chạy PoC thật xác nhận bypass thành công rồi xoá PoC. Đã sửa:
gate nay sống trong `AuditLogViewSession` — 1 instance MỚI tạo mỗi lần `UiSessionServer.RunConnectionAsync`
bắt đầu (1 kết nối pipe), truyền qua `DispatchAsync` xuống `AuditLogCoordinator.HandleAsync` (thêm tham
số), tự giải phóng khi kết nối đóng, không còn field service-wide nào để rò rỉ qua kết nối khác. Test
regression mới `AuditLogCoordinatorTests.AuditLogQuery_EmptyTokenFromDifferentConnection_ReturnsInvalidToken_EvenWithinGateWindow`
tái hiện đúng kịch bản 2 kết nối (session A pass gate, session B token rỗng phải bị từ chối) — PASS sau
fix, và sẽ FAIL nếu ai đó lỡ revert về field service-wide cũ. Build 0 Warning/0 Error, test toàn solution
439/439 pass (Service.Tests 234, Vision.Tests 66, Overlay 18, Watchdog 2, Uninstaller 9, UI 110) — không
regression.

### `src/ParentalGuard.Service/Ipc/UiSessionServer.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `UiSessionServer` ctor (sửa — thêm tham số `ConfigCoordinator`/`AuditLogCoordinator`) | `Worker.StartUiSessionServer` | — |
| `UiSessionServer.DispatchAsync` (sửa — thêm 2 nhánh mới) | `RunConnectionAsync` | `ConfigCoordinator.HandleAsync` (`ConfigQuery`/`ConfigUpdateReq`/`RemoveWhitelistReq`), `AuditLogCoordinator.HandleAsync` (`AuditLogQuery`/`MarkFalsePositiveReq`) |

### `src/ParentalGuard.Service/Worker.cs` (sửa)

| Hàm | Callers | Callees |
|---|---|---|
| `Worker.ExecuteAsync` (sửa — thêm `monitoringStateHolder`, khởi tạo `_configCoordinator`/`_auditLogCoordinator`) | `BackgroundService` (host) | `MonitoringStateHolder` ctor, `ConfigCoordinator`/`AuditLogCoordinator` ctor |
| `Worker.BuildControlVisionCommand` (sửa — UNION `state.ExcludeProcessNames` ∪ `state.UserWhitelistedProcessNames`) | Mọi lambda build `ControlVisionCommand` (initial push Vision, `PauseCoordinator` delegate, `HandleVisionResultAsync`, `ConfigCoordinator` `pushControlVisionCommand`) | — |
| `Worker.StartUiSessionServer` (sửa — truyền thêm `_configCoordinator!`/`_auditLogCoordinator!`) | `ExecuteAsync` | `UiSessionServer` ctor |

`BuildControlVisionCommand` UNION whitelist — hiện thực hoá đúng nghĩa đen `05-image-pipeline-architecture.md`
mục 3.5 sửa nhỏ v0.3.0 ("`config.ExcludeProcessNames ∪ config.UserWhitelistedProcessNames`") — `Vision`
chỉ có 1 danh sách `ExcludeProcessNames` (`VisionRuntimeConfig`), phép hợp phải làm ở `Service` trước khi
gửi qua `ControlVisionCommand`.

### Test mới/sửa

- `tests/ParentalGuard.Service.Tests/OverlayMessageValidatorTests.cs` (mới, 10 test).
- `tests/ParentalGuard.Service.Tests/MonitoringStateDataTests.cs` (thêm 1 test — fail-secure reset whitelist/overlay_message rỗng).
- `tests/ParentalGuard.Service.Tests/ConfigDbTests.cs` (thêm 2 test — round-trip `overlay_message`/`user_whitelisted_process_names` qua `CreateFresh` và `UpdateMonitoringState`).
- `tests/ParentalGuard.Service.Tests/ConfigCoordinatorTests.cs` (mới, 11 test): `ConfigQuery` đọc RAM; `ConfigUpdateRequest` — `overlay_message` hợp lệ (không push), `performance_mode` đổi (có push), `UNSPECIFIED` giữ nguyên mode, `TooLong`/`InvalidCharacters`; `RemoveWhitelistEntryRequest` — `InvalidToken`/`NotFound`/`Success` (persist + push); `TryAddUserWhitelistEntryAsync` — thêm mới (push) và `AlreadyListed` (case-insensitive, không push).
- `tests/ParentalGuard.Service.Tests/AuditLogCoordinatorTests.cs` (mới, 7 test): `AuditLogQuery` — `InvalidToken`, token rỗng không có gate trước đó → `InvalidToken`, token hợp lệ mở gate cho trang kế tiếp, gate hết hạn sau 10 phút → `InvalidToken`; `MarkFalsePositiveRequest` — `InvalidToken`, thêm mới thành công + ghi `ConfigChanged`, `AlreadyListed`.
- `tests/ParentalGuard.Service.Tests/AuditLogWriterTests.cs` (thêm 3 test): `ReadPageAsync` mới nhất trước + `has_more` đúng qua 2 trang; `ContentBlocked` trích đúng `processName`/`riskScore`; event khác để `ProcessName` rỗng.

Build 0 Warning/0 Error toàn `.sln`. Test per-project: `ParentalGuard.Vision.Tests` 62 → **66** (+4, GAP 1),
`ParentalGuard.Service.Tests` 196 → **233** (+37, GAP 2), `ParentalGuard.Overlay.Tests` 18/18,
`ParentalGuard.Watchdog.Tests` 2/2, `ParentalGuard.Uninstaller.Tests` 9/9, `ParentalGuard.UI.Tests`
110/110 — không regression.

### Gap MỚI phát hiện lúc sửa (không thuộc phạm vi lượt này, không tự sửa)

`DashboardStatusQuery`/`AuditChartQuery`/`AcknowledgePauseAnomalyRequest` (`S2` Dashboard, Đợt 6) **cũng
chưa có handler ở `Service`** — cùng loại gap với GAP 2 (rơi vào `default` case của
`UiSessionServer.DispatchAsync` trước lượt này, nay vẫn vậy vì 3 message này KHÔNG nằm trong 5 message
`test-runner` yêu cầu sửa lượt này). `ParentalGuard.UI` `DashboardViewModel`/health-check/biểu đồ `S2`
polling các message này nhưng chưa từng nhận response thật. Cần `feature-dev` lượt khác nối dây theo đúng
`Architecture/10-ui-architecture.md` mục 6.2 (mẫu hình y hệt `ConfigCoordinator`/`AuditLogCoordinator` đã
làm ở lượt này — có thể 1 `DashboardCoordinator` mới hoặc gộp vào 1 trong 2 coordinator hiện có tuỳ
`feature-dev` quyết định lúc đó).

Đồng thời xác nhận lại (không tự sửa, ngoài phạm vi): event `ContentBlocked` (`04-data-architecture.md`
mục 5.1) **chưa từng được ghi thật** bởi `OverlayDecisionCoordinator.HandleVisionResultAsync` — audit
log hiện không có record `ContentBlocked` nào để `AuditLogQuery`/`MarkFalsePositiveRequest` hiển thị
trong kịch bản thật (`S3` "Đánh dấu sai" cần đúng dòng `ContentBlocked` để lấy `process_name`). Handler
`AuditLogQuery`/`MarkFalsePositiveRequest` viết ở lượt này vẫn đúng/đầy đủ cho MỌI `event_type` sẵn có
trong `audit.log` (không phụ thuộc `ContentBlocked` cụ thể) — khi gap này được đóng ở lượt khác,
`AuditLogQuery` tự động hiển thị đúng mà không cần sửa gì thêm ở đây. Đây CHÍNH LÀ gap đã ghi nhận ở
"Ghi chú gap phát hiện lúc build" mục 1 phía trên (`VisionInferenceResult` thiếu field `process_name`) —
2 gap liên đới trực tiếp: thiếu `process_name` → không thể ghi `ContentBlocked.detail.processName` đúng
→ `MISC-030` chưa hoàn thiện end-to-end dù mọi hạ tầng IPC/whitelist đã sẵn sàng từ lượt này.

## Đợt 8 (`ROADMAP.md` mục 4, Additional mechanisms & hardening) — `MISC-090`, `SEC-020`, `MISC-010` hoàn chỉnh, `process_name`

Đóng 4/5 việc bắt buộc (`MISC-090`, `SEC-020`, `MISC-010` hoàn chỉnh, `process_name`) + việc 6 (tài liệu
`docs/BEHAVIOR-DISCLOSURE.md`). **Việc 5 (`MISC-050`/S2 Dashboard: `DashboardStatusQuery`/`AuditChartQuery`/
`AcknowledgePauseAnomalyRequest`) KHÔNG làm ở lượt này** — đúng theo hướng dẫn "khuyến nghị, không bắt
buộc nếu quá tải, ưu tiên 4 việc trên trước"; cả 3 message vẫn rơi vào nhánh `default` của
`UiSessionServer.DispatchAsync` → `authCoordinator.HandleAsync` → throw, giống trạng thái Đợt 7 (gap còn
mở, xem ghi chú "Gap MỚI phát hiện lúc sửa" ở mục Đợt 7 phía trên) — cần `feature-dev` lượt khác đóng,
sẽ cần thêm state tracking `vision_connected`/`overlay_connected`/`watchdog_alive` (hiện chưa tồn tại ở
bất kỳ đâu trong `ChildProcessSupervisor`/`WatchdogSessionServer`) + đọc `audit_log_free_disk_bytes` +
đếm `blocked_count`/ngày từ `audit.log` cho `AuditChartQuery`.

### 1. `MISC-090` — Verify checksum model AI khi load

| Hàm/File | Callers | Callees |
|---|---|---|
| `ExpectedModelChecksum.Sha256Hex` (mới, `src/ParentalGuard.Vision/ModelIntegrity/ExpectedModelChecksum.cs`) — hằng số SHA-256 hex của `models/nsfw_model.onnx` đóng gói hiện tại (tính bằng `sha256sum`, PHẢI cập nhật thủ công mỗi khi đổi model) | `Program.cs` | — |
| `Program.cs` (top-level, sửa) — gọi `OnnxChecksumVerifier.Verify(modelBytes, Convert.FromHexString(ExpectedModelChecksum.Sha256Hex))` NGAY sau `File.ReadAllBytesAsync(ModelPaths.OnnxModelPath)`, TRƯỚC `OrtEnv.Instance().DisableTelemetryEvents()`/tạo `NsfwClassifier` — fail → `Array.Clear(modelBytes)` (IMG-003) + `return VisionExitCodes.ModelIntegrityCheckFailed` (=18, top-level `return` set exit code, cùng mẫu hình `return VisionExitCodes.CaptureInitAccessDenied` đã có ở dòng dưới cho case `CaptureInitializationException` — KHÔNG dùng `Environment.Exit` trực tiếp vì tại điểm này chưa có Task/Thread nền nào chạy, `return` đủ tương đương) | entry point (OS) | `OnnxChecksumVerifier.Verify` (đã có từ Đợt 1, chưa từng được gọi tới lượt này) |
| `VisionExitCodes.ModelIntegrityCheckFailed` (=18, comment sửa — xác nhận đã enforce từ Đợt 8, không còn "giữ chỗ chưa dùng") | `Program.cs`, `ChildProcessSupervisor` (respawn bình thường, không đổi IL — `Architecture/05` mục 8.2) | — |

Test: `tests/ParentalGuard.Vision.Tests/ExpectedModelChecksumTests.cs` (mới, 1 test — guard định dạng
64 hex chars, KHÔNG đọc lại file thật để tránh brittleness phụ thuộc đường dẫn repo checkout).
`OnnxChecksumVerifierTests.cs`/`VisionExitCodesTests.cs` (đã có từ Đợt 1) vẫn bao phủ đúng logic cốt lõi
(`Verify` constant-time, exit code = 18) — không sửa 2 file test này.

**Gap tự ghi nhận (không giấu)**: không có test tích hợp cho chính `Program.cs` (top-level statements,
không có seam DI cho `File.ReadAllBytesAsync`/exit path — nhất quán với toàn bộ `Program.cs` hiện tại
vốn không có test nào khác, kể cả nhánh `CaptureInitAccessDenied` có sẵn từ Đợt 1). Đã tự kiểm chứng thủ
công: chạy `sha256sum models/nsfw_model.onnx` khớp đúng hằng số nhúng, đọc lại code đảm bảo thứ tự
verify → load đúng ADR-46 (tránh TOCTOU).

### 2. `SEC-020` — WER crash dump policy cho `ParentalGuard.Vision.exe`

| Hàm/File | Callers | Callees |
|---|---|---|
| `WerPolicyProvisioner.Apply(string)` / `.Apply(RegistryKey, string)` (mới, `src/ParentalGuard.Service/Security/WerPolicyProvisioner.cs`) — ghi `ExcludedApplications\<file>=1` + `LocalDumps\<file>\DumpType=1` (Mini, KHÔNG Full), idempotent (đọc trước khi ghi lại) | `Worker.ApplyWerPolicyBestEffort` (public overload); test (`WerPolicyProvisionerTests`, overload `RegistryKey` test-only, cùng mẫu hình `RegistryStartValueWatcher`) | `Microsoft.Win32.Registry.LocalMachine`/`RegistryKey.CreateSubKey` (BCL) |
| `Worker.ApplyWerPolicyBestEffort` (mới, private) — best-effort, không chặn `ExecuteAsync` nếu ghi registry lỗi | `Worker.ExecuteAsync` (cùng nhóm `ApplyAclBestEffort`/`ApplyWfpBestEffort`, gọi ngay sau `ApplyWfpBestEffort()`, trước khi spawn Vision lần đầu) | `WerPolicyProvisioner.Apply(string)` |

Test: `tests/ParentalGuard.Service.Tests/WerPolicyProvisionerTests.cs` (mới, 3 test, registry THẬT dưới
HKCU — không cần quyền SYSTEM): ghi đúng 2 key; gọi 2 lần không lỗi/giữ nguyên giá trị (idempotent); giá
trị sai lệch có sẵn (mô phỏng bị ghi đè) → tự sửa lại đúng.

### 3. `MISC-010` audit log tamper-evident hoàn chỉnh

#### 3a. Ghi `ContentBlocked` edge-triggered (ADR-137)

| Hàm/File | Callers | Callees |
|---|---|---|
| `OverlayDecisionCoordinator.HandleVisionResultAsync` (sửa — nay `async`, thêm biến `isNewViolation` = `violates && !wasActive` TÁCH KHỎI `changed` hiện có) | `ChildProcessSupervisor.ReaderLoopAsync` (kênh Vision) | `OverlayThresholdDecision.Violates`, `PushCurrentList`, `AuditLogWriter.AppendAsync` (`"ContentBlocked"`, chỉ khi `isNewViolation`) |

`detail` = `{ windowHandle, processName, riskScore, bbox: {x,y,width,height} }` (khớp đúng schema
`04-data-architecture.md` mục 5.1) — `result.Bbox` dùng `?.` (message field proto3 C# trả `null` nếu
chưa set, KHÔNG tự có default instance — bug thực tế phát hiện lúc chạy test cũ
`OverlayDecisionCoordinatorMergeModeTests` (không set `Bbox`) → `NullReferenceException`, đã sửa bằng
`result.Bbox?.X ?? 0` v.v.). `result.ProcessName` (string proto3, default `""`, không cần `?.`).

Test: `tests/ParentalGuard.Service.Tests/OverlayDecisionCoordinatorContentBlockedTests.cs` (mới, 4 test):
lần đầu vi phạm → ghi `ContentBlocked` đúng field; cùng cửa sổ tiếp tục vi phạm (bbox đổi) → KHÔNG ghi
lặp; gỡ chặn rồi vi phạm lại → ghi lần 2 (lượt block mới); không set `Bbox` → không throw, ghi bbox=0.

#### 3b. `VerifyAuditChainRequest`/`Response` (field 154/155, ADR-136/138)

| Hàm/File | Callers | Callees |
|---|---|---|
| `.proto` (sửa, `src/ParentalGuard.Ipc/Protos/ipc.proto`) — thêm `oneof` case 154/155 + message `VerifyAuditChainRequest {}`/`VerifyAuditChainResponse{is_intact, total_records_scanned, broken_at_seq, verified_at_unix_ms}`; thêm field 7 `process_name` (string) vào `VisionInferenceResult` (trước là comment giữ chỗ) | Grpc.Tools codegen (build-time) | — |
| `AuditLogWriter.VerifyChain` (private, refactor từ `VerifyTail` — trả `ChainVerifyOutcome{Detail, BrokenAtSeq, BrokenIndex}` thay vì `string?`, dùng chung cho cả cửa sổ đuôi N=50 (boot) lẫn toàn bộ file (on-demand)) | `InitializeAsync`, `VerifyFullChainAsync` | — |
| `AuditLogWriter.VerifyFullChainAsync` (mới, public) — đọc TOÀN BỘ file dưới `_writeLock` (thả ngay sau đọc, mục 5.3a bước 3), `VerifyChain(all)`, nếu không intact VÀ chưa có `AuditChainBrokenDetected` nào SAU điểm đứt (check qua `EventType` trong `Raw` JSON của các record phía sau) → `AppendAsync(..., startNewChain: true)` 1 lần duy nhất (ADR-138) | `AuditLogCoordinator.HandleVerifyAuditChainAsync` | `VerifyChain`, `AppendAsync` (private overload, `startNewChain:true`), `EventType` (private static helper mới) |
| `AuditChainVerifyResult` (record mới, public, cùng file) | `AuditLogWriter.VerifyFullChainAsync` → `AuditLogCoordinator` | — |
| `ConfigDb.UpdateLastFullVerifyAt(long)` / `.ReadLastFullVerifyAtUnixMs()` (mới) — `UPDATE`/`SELECT audit_meta.last_full_verify_at_unix_ms WHERE id=1` | `AuditLogCoordinator.TryPersistLastFullVerifyAt` (Update); test (Read, round-trip) | `SqliteCommand` (Microsoft.Data.Sqlite) |
| `AuditLogCoordinator` ctor (sửa — thêm tham số `string configDbPath`), `.HandleAsync` (sửa — thêm case `VerifyAuditChainReq`), `.HandleVerifyAuditChainAsync`/`.TryPersistLastFullVerifyAt` (mới, private) | `Worker.ExecuteAsync` (ctor); `UiSessionServer.DispatchAsync` (`HandleAsync`) | `AuditLogWriter.VerifyFullChainAsync`, `ConfigDb.Open`/`.UpdateLastFullVerifyAt` (best-effort, nuốt `ConfigLoadException` — mốc thời gian chỉ là UX phụ trợ, không chặn kết quả trả UI) |
| `UiSessionServer.DispatchAsync` (sửa — thêm `VerifyAuditChainReq` vào nhóm route sang `auditLogCoordinator`) | `RunConnectionAsync` | `AuditLogCoordinator.HandleAsync` |
| `Worker.ExecuteAsync` (sửa — `new AuditLogCoordinator(..., InstallPaths.ConfigDbPath)`) | `BackgroundService` (host) | — |

Test: `tests/ParentalGuard.Service.Tests/AuditLogCoordinatorTests.cs` (thêm 2 test): log nguyên vẹn →
`is_intact=true` + `audit_meta.last_full_verify_at_unix_ms` được ghi đúng giá trị trả về; log bị tamper
(sửa trực tiếp dòng đầu, tái dùng đúng kỹ thuật `AuditLogWriterTests.Reinitialize_OnTamperedRecord_...`)
→ `is_intact=false`/`broken_at_seq` đúng + verify 2 lần liên tiếp chỉ ghi `AuditChainBrokenDetected`
ĐÚNG 1 LẦN (ADR-138, không lặp).

#### 3c. `AuditLogWriter.ChainWasBrokenAtStartup` + Toast hoãn phát (ADR-139)

| Hàm/File | Callers | Callees |
|---|---|---|
| `AuditLogWriter.ChainWasBrokenAtStartup` (property mới, public get/private set) — `true` chỉ khi nhánh chain-đứt chạy trong LẦN GỌI `InitializeAsync` hiện tại | `Worker.ExecuteAsync` (đọc sau khi `_auditLog` đã sẵn sàng) | set bởi `InitializeAsync` (object initializer `{ ChainWasBrokenAtStartup = true }` ở nhánh `outcome.Detail is not null`) |
| `Worker.BuildOverlayOneTimeMessages` (sửa — thêm tham số `bool auditChainWasBrokenAtStartup`, trả `List<Action<IpcPayload>>` gộp CẢ 2 điều kiện thay vì chỉ 1 `ShowToast` cố định) | `Worker.ExecuteAsync` (gọi `BuildOverlayOneTimeMessages(config, _auditLog.ChainWasBrokenAtStartup)`) | `BuildFailSecureToast` (đã có), `BuildAuditChainBrokenToast` (mới) |
| `Worker.BuildAuditChainBrokenToast` (mới, private static) — `reason_code="AUDIT_CHAIN_BROKEN_DETECTED"` | `BuildOverlayOneTimeMessages` | — |

Không có test riêng cho sequencing Toast (đã có `AuditLogWriterTests.Reinitialize_OnTamperedRecord_...`
xác nhận `ChainWasBrokenAtStartup`-tương đương qua audit.log; wiring `Worker.cs` thuần lắp ráp, không có
logic nhánh mới cần unit test riêng ngoài 2 hàm build Toast tĩnh — rủi ro thấp, nhất quán mẫu hình
`BuildFailSecureToast` gốc cũng không có test riêng từ Đợt 0).

### 4. `process_name` field 7 (`VisionInferenceResult`)

| Hàm/File | Callers | Callees |
|---|---|---|
| `CaptureLoopWorker.ProcessOneFrame` (sửa — sau khi `pipeline.Process` trả `result` không null, set `result.ProcessName = ForegroundWindowTracker.ResolveProcessName(hwnd) ?? ""` TRƯỚC khi `EnqueueOutbound`) | `ProcessCycle` (vòng lặp candidate) | `ForegroundWindowTracker.ResolveProcessName` (tái dùng đúng hàm đã dùng cho exclude-list, KHÔNG resolve bằng cơ chế khác — `hwnd` ở đây là cửa sổ ĐANG xử lý, không nhất thiết là `fgHwnd`, nên gọi lại đúng 1 lần/frame cho đúng cửa sổ đó thay vì tái dùng biến `fgProcessName` đã tính sẵn cho foreground khác) |

**Gap tự ghi nhận (không giấu)**: không có unit test riêng cho dòng này — `CaptureLoopWorker.ProcessOneFrame`
là `private`, không có seam DI cho `IpcChildClient`/`FrameClassificationPipeline` cụ thể (khác
`IFrameCapture`/`IWindowCropper` đã có seam từ trước) để dựng test cô lập; nhất quán với việc
`ForegroundWindowTracker` (P/Invoke `user32`/`kernel32`) chưa từng có test riêng từ Đợt 1. Đã tự kiểm
chứng bằng đọc lại code + tái dùng đúng API đã tested gián tiếp qua production từ Đợt 1
(`ExcludeProcessMatcherTests` test logic khớp tên, không test `ResolveProcessName` P/Invoke thật).

### Việc 6 — `docs/BEHAVIOR-DISCLOSURE.md` (mới, thuần tài liệu — `MISC-070`)

Song ngữ Việt/Anh: mục đích app, capture màn hình cục bộ/không lưu/không gửi mạng + WFP chặn cứng tầng
OS, watchdog kép + chống gỡ cài đặt (không ransomware, không ẩn Task Manager, có Dashboard + gỡ cài đặt
hợp pháp qua mật khẩu/Recovery Key), zero network/telemetry/auto-update + mã nguồn mở, placeholder liên
hệ/báo cáo false-positive (chủ dự án tự điền). Không phải quyết định kiến trúc — không cập nhật
`Architecture/`.

### Kết quả build/test Đợt 8

Build 0 Warning/0 Error toàn `.sln`. Test per-project (tránh WDAC false-positive `ParentalGuard.Vision.Tests`
khi chạy qua `dotnet test` solution-wide đã ghi nhận từ trước): `ParentalGuard.Service.Tests` 234 → **243**
(+9: 4 `OverlayDecisionCoordinatorContentBlockedTests` + 2 `AuditLogCoordinatorTests` + 3
`WerPolicyProvisionerTests`), `ParentalGuard.Vision.Tests` 66 → **67** (+1), `ParentalGuard.Overlay.Tests`
18/18, `ParentalGuard.Watchdog.Tests` 2/2, `ParentalGuard.Uninstaller.Tests` 9/9, `ParentalGuard.UI.Tests`
110/110 — không regression (449/449 tổng).

### 2026-09-29 audit fix — 2 FAIL cứng ĐỘC LẬP phát hiện ở đúng phần Đợt 8 vừa code, cả 2 đã sửa + re-audit độc lập 2 vòng xác nhận RESOLVED

**FAIL 1 (security-privacy-auditor)** — `VerifyAuditChainRequest` hoàn toàn KHÔNG gate. Root cause:
`AuditLogCoordinator.HandleAsync` định tuyến `VerifyAuditChainReq` sang `HandleVerifyAuditChainAsync(request,
cancellationToken)` — **DROP MẤT tham số `AuditLogViewSession`** dù `UiSessionServer` đã tạo và truyền đúng
xuống mỗi kết nối pipe. Hệ quả: bất kỳ kết nối nào (kể cả không qua `AuthVerifyRequest` bao giờ) gọi
`VerifyAuditChainRequest` đều nhận được kết quả verify đầy đủ — bypass hoàn toàn `PWD-020`. Cả 2 auditor
độc lập đã chạy PoC thật xác nhận bypass thành công (session A pass gate qua `AuditLogQuery`, session B
mới không token vẫn đọc được). Sửa: `HandleVerifyAuditChainAsync` nay nhận `session`, kiểm tra
`session.GateOpenUntilUnixMs` (tái dùng đúng state đã dùng cho `AuditLogQuery`, không tạo state song song
mới) trước khi verify — trả `VerifyAuditChainResult.InvalidToken` nếu chưa/hết gate. Amendment `.proto`:
thêm `enum VerifyAuditChainResult`/field `result` (field 5, additive) vào `VerifyAuditChainResponse` —
`Architecture/03-ipc-communication.md` v0.8.3→v0.8.4 (ADR-142). 2 test regression mới
(`AuditLogCoordinatorTests.cs`): `VerifyAuditChainRequest_SessionNeverPassedViewAuditLogGate_ReturnsInvalidToken`,
`VerifyAuditChainRequest_DifferentConnectionSession_ReturnsInvalidToken_EvenAfterAnotherSessionPassedGate`
(tái hiện đúng kịch bản 2 kết nối, PASS sau fix, FAIL nếu revert).

**FAIL 2 (test-runner, PoC thật)** — `AuditLogWriter.VerifyFullChainAsync` (dùng `VerifyChain` tuyến tính
cũ) `return` NGAY khi gặp bất thường ĐẦU TIÊN của CẢ FILE. Vì 1 record đã tamper mãi mãi fail lại
self-hash-check của chính nó ở MỌI lần gọi sau, việc bail sớm khiến verify KHÔNG BAO GIỜ quét tới các đoạn
`chain_id` phía sau — 1 tamper ĐỘC LẬP thứ 2 xảy ra SAU 1 lần "phục hồi" (đoạn chain mới) hợp lệ trước đó
hoàn toàn không được phát hiện, verify mãi mãi báo lại đúng điểm đứt CŨ. Đây trực tiếp mâu thuẫn với câu
chữ thiết kế mục 5.3a bước 1 ("chia thành các đoạn chain kế tiếp nhau theo `chain_id`... áp dụng đúng
thuật toán cho TOÀN BỘ TỪNG ĐOẠN") — implementation đầu tiên hiểu sai thành 1 vòng quét toàn file duy
nhất. Sửa: `VerifySegments`/`VerifySegment` (mới, `AuditLogWriter.cs`) chia records thành các đoạn liên
tiếp theo `chain_id`, verify MỖI đoạn ĐỘC LẬP (1 đoạn hỏng không cản việc verify đoạn sau) — `VerifyChain`
cũ GIỮ NGUYÊN, chỉ dùng cho boot-time tail-window (N=50) ở `InitializeAsync`, không đổi hành vi đó. Field
`broken_at_seq` mới thêm vào `detail` JSON của `AuditChainBrokenDetected` (CẢ 2 nhánh boot-time + on-demand)
để dedup đúng theo từng điểm đứt cụ thể thay vì "có record nào xuất hiện sau vị trí đứt hay không" (cách
cũ chính là 1 phần nguyên nhân che giấu tamper độc lập). Amendment `Architecture/04-data-architecture.md`
v0.5.0→v0.5.1 (làm rõ mục 5.3a bước 1/6, ADR-143). Test regression mới (`AuditLogWriterTests.cs`):
`VerifyFullChainAsync_SecondIndependentTamperInNewSegment_IsDetectedAndAppendsSecondBrokenRecord` (tamper
chain1 → verify → ghi 2 record hợp lệ vào chain2 → tamper ĐỘC LẬP giữa chain2 → verify lại → PHẢI phát
hiện + ghi thêm 1 `AuditChainBrokenDetected` MỚI, tổng 2 record trong file). Re-audit độc lập (`test-runner`)
tự viết thêm PoC biến thể 3-đoạn-chain (2 đoạn khác nhau bị tamper cùng lúc trùng giá trị `broken_at_seq`
cục bộ) xác nhận dedup theo VỊ TRÍ (`Skip(BrokenIndex+1)`) không thể match nhầm giữa 2 đoạn khác nhau —
thuật toán tổng quát đúng, không chỉ pass đúng 1 kịch bản hẹp.

**1 design-limitation phát hiện qua cùng lượt audit (KHÔNG do 2 fix trên gây ra, tồn tại từ thiết kế gốc
Đợt 0, KHÔNG blocking)**: hash-chain (SHA-256 thuần, không HMAC/chữ ký, công thức công khai) không chống
được kẻ tấn công có ĐỒNG THỜI (a) quyền ghi trực tiếp `audit.log` VÀ (b) khả năng khiến `Service` restart —
họ có thể tự tính lại hash-chain hợp lệ cho nội dung bịa từ bất kỳ điểm cắt nào. Giới hạn cố hữu của mọi
hash-chain không có anchor ngoài, chưa từng được ghi nhận tường minh ở `Specification/`. Lớp phòng thủ
thật cho nhóm này nằm ở ACL file + anti-tamper, không phải hash-chain. Đã giao `spec-maintainer` bổ sung
ghi chú làm rõ phạm vi bảo vệ vào `SEC-041`/`MISC-010` (xem `Specification/04-security-spec.md`/
`10-additional-mechanisms-spec.md` changelog).

Build 0 Warning/0 Error, test toàn solution 449→**452/452** (Service.Tests 243→246: +2 session-scoping
+1 segment-independence, Vision/Overlay/Watchdog/Uninstaller/UI không đổi) — không regression.
