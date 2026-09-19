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
| `ConfigDb.UpsertIconPosition`/`.ReadAllIconPositions` (public, **mới**) | `ConfigDb.cs` | `IconPositionCoordinator.HandleIconPositionUpdateAsync`/`.ConfigureInitialPush` | — |
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
