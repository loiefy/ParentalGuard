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
| `UiIpcClient.SendRequestAsync<TResp>` (**mới Đợt 6**, ADR-119 — `SemaphoreSlim(1)`, không Reader/Writer loop song song khác `IpcChildClient`) | `Client/UiIpcClient.cs` | `ParentalGuard.UI/Services/IpcClient/AuthFacade.*` | `IpcFrameTransport.WriteFrameAsync/ReadFrameAsync` (session_key), `DisconnectCoreAsync` (khi lỗi pipe/framing) |
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
| `PauseFacade`/`DashboardFacade`/`AuditFacade`/`ConfigFacade` (stub rỗng, **CHƯA có method** — placeholder DI cho giai đoạn 2-4) | `PauseFacade.cs`/`DashboardFacade.cs`/`AuditFacade.cs`/`ConfigFacade.cs` | đăng ký DI ở `App.BuildServiceProvider`, chưa ai gọi | — |

### `src/ParentalGuard.UI/Services/` (NavigationService, LocalizationService, SingleInstanceGuard)

| Hàm | File | Callers | Callees |
|---|---|---|---|
| `NavigationService.Initialize` | `NavigationService.cs` | `App.OnLaunched` | — (lưu `Frame` root) |
| `NavigationService.NavigateToConnectionError`/`.NavigateToOnboarding`/`.NavigateToMainShell` | `NavigationService.cs` | `App.ConnectAndRouteAsync`, `Views/OnboardingPage.*` | `Frame.Navigate` (BCL) |
| `NavigationService.NavigateToRecovery` (**CHƯA implement — throw NotImplementedException có chủ đích, `S6` giai đoạn sau**) | `NavigationService.cs` | `ShowAuthPromptAsync` (khi `AuthPromptDialog.ForgotPasswordRequested`), `Views/SettingsPage` (giai đoạn 4, chưa gọi) | — |
| `NavigationService.ShowAuthPromptAsync` (`S5`, mục 6.5) | `NavigationService.cs` | (chưa có caller thật ở giai đoạn 1 — `S3`/`S2`/`S4` gọi ở giai đoạn 2-4) | `Views/AuthPromptDialog` ctor + `.RequestActionTokenAsync`, `NavigateToRecovery` (nếu bấm "Quên mật khẩu?") |
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
