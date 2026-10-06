using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Configuration;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.ModelIntegrity;
using ParentalGuard.Vision.Pipeline;

// Đợt 1 (ROADMAP.md): pipeline 7 bước đầy đủ (Architecture/05-image-pipeline-architecture.md).
// Tiến trình này chỉ được Service spawn qua ChildProcessLauncher (bootstrap handle truyền qua
// STD_INPUT_HANDLE, không qua command-line — Architecture/03 mục 5.2, ADR-18).

// Log chẩn đoán runtime (Đợt 9) — Console.Error không đi đâu cả khi Service spawn (không redirect
// StdOutput/StdError, CreateNoWindow), nên ghi ra file để tự chẩn đoán sự cố thật không cần
// WER/debugger. TẮT theo mặc định (build production không trả overhead/không tạo file gì — dùng
// [Conditional], lời gọi bị trình biên dịch loại bỏ hoàn toàn nếu thiếu define
// PARENTALGUARD_DIAGNOSTIC_LOG, xem src/Directory.Build.props). Bật bằng
// `dotnet publish ... -p:ParentalGuardDiagnosticLog=true` khi cần điều tra máy thật.
[Conditional("PARENTALGUARD_DIAGNOSTIC_LOG")]
static void DebugLog(string message)
{
    try
    {
        File.AppendAllText(@"C:\PGDebugLog\parentalguard-vision-debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
    }
    catch
    {
    }
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    DebugLog($"UNHANDLED EXCEPTION (IsTerminating={e.IsTerminating}) trên thread bất kỳ:\n{e.ExceptionObject}");
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    DebugLog("UnobservedTaskException:\n" + e.Exception);
};

DebugLog("Vision Main() bắt đầu.");

// Bug 2026-10-06: toạ độ màn hình/cửa sổ phải cùng hệ pixel vật lý — xem DpiAwareness.
bool dpiAware = DpiAwareness.EnablePerMonitorV2();
DebugLog($"SetProcessDpiAwarenessContext(PerMonitorV2) = {dpiAware}.");

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

ChildIpcBootstrap bootstrap;
try
{
    DebugLog("Trước ReadFromInheritedStdHandle.");
    bootstrap = ChildIpcBootstrap.ReadFromInheritedStdHandle();
    DebugLog($"Bootstrap OK — pipeName={bootstrap.PipeName}, protocolVersion={bootstrap.ProtocolVersion}.");
}
catch (Exception ex)
{
    // Không có Service để báo lỗi ở bước này — tự thoát; Service phát hiện qua ngân sách
    // connect sau spawn (Architecture/03 mục 4.1) và spawn lại.
    DebugLog("Bootstrap FAILED:\n" + ex);
    Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
    return 1;
}

var client = new IpcChildClient(ProcessType.Vision, bootstrap);

// Real-hardware fix (Đợt 9): connect pipe NGAY trước khi làm các bước khởi tạo tốn thời gian
// (đọc model, DirectML, Desktop Duplication) — tránh chi phí cold-start ăn hết ngân sách connect
// sau spawn của Service (Architecture/03-ipc-communication.md mục 4.1, ADR-147/148).
DebugLog("Trước client.ConnectAsync (connect pipe sớm, trước khi tải model).");
await client.ConnectAsync(cts.Token).ConfigureAwait(false);
DebugLog("client.ConnectAsync xong — đã connect + handshake thành công.");

DebugLog("Trước đọc model file: " + ModelPaths.OnnxModelPath);
byte[] modelBytes = await File.ReadAllBytesAsync(ModelPaths.OnnxModelPath, cts.Token).ConfigureAwait(false);
DebugLog($"Đọc model xong, {modelBytes.Length} bytes.");

// Architecture/05 mục 7 (ADR-46, MISC-090): verify checksum TRƯỚC khi load InferenceSession — tránh
// TOCTOU (đọc byte[] 1 lần, load thẳng từ buffer đã verify) và không chạy pipeline ở trạng thái model
// không tin cậy.
if (!OnnxChecksumVerifier.Verify(modelBytes, Convert.FromHexString(ExpectedModelChecksum.Sha256Hex)))
{
    DebugLog("Checksum FAILED — thoát ModelIntegrityCheckFailed.");
    Array.Clear(modelBytes); // IMG-003 — zero-out ngay cả khi phát hiện tamper, không giữ lại trong RAM
    return VisionExitCodes.ModelIntegrityCheckFailed;
}
DebugLog("Checksum OK.");

OrtEnv.Instance().DisableTelemetryEvents(); // IMG-015 — hardening bắt buộc dù ONNX Runtime mặc định không cần network.
using var sessionOptions = new SessionOptions();
try
{
    DebugLog("Trước AppendExecutionProvider_DML.");
    sessionOptions.AppendExecutionProvider_DML(deviceId: 0);
    client.DiagnosticState = "ep=directml";
    DebugLog("DirectML EP OK.");
}
catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
{
    // PERF-031/ADR-45: fallback CPU EP nếu driver/GPU không hỗ trợ DirectML — không log (Vision
    // không ghi file, SEC-017/BE-022), chỉ báo qua HeartbeatAck.DiagnosticState (chẩn đoán thuần).
    DebugLog("DirectML EP FAILED, fallback CPU:\n" + ex);
    client.DiagnosticState = "ep=cpu-fallback";
}

DebugLog("Trước tạo NsfwClassifier (load InferenceSession).");
using INsfwClassifier classifier = new NsfwClassifier(modelBytes, sessionOptions);
Array.Clear(modelBytes);
DebugLog("NsfwClassifier OK.");

DesktopDuplicationCapture capture;
try
{
    DebugLog("Trước new DesktopDuplicationCapture.");
    capture = new DesktopDuplicationCapture();
    // Architecture/05 mục 8.1 bước 2-3: probe khởi tạo Desktop Duplication TRƯỚC khi bắt đầu
    // CaptureLoopWorker — bất kỳ lỗi nào ở đây bị coi là "có thể do Integrity Level" (ADR-49).
    DebugLog("Trước AcquireNextFrame probe.");
    // BUG FIX (Đợt 9, real-hardware crash): thiếu ReleaseFrame() sau probe — vi phạm đúng invariant
    // ADR-42 mà DesktopDuplicationCapture.AcquireNextFrame tự ghi ("Caller phải gọi ReleaseFrame
    // ngay sau khi copy xong"). Frame probe bị treo vĩnh viễn, tích luỹ qua nhiều chu kỳ capture
    // thật (CaptureLoopWorker) cho tới khi Desktop Duplication API từ chối hẳn với
    // DXGI_ERROR_INVALID_CALL sau vài giây — root cause của "Pipe has been ended" lặp lại sau khi
    // đã sửa xong bug ACL + bug GpuWindowCropper.
    using (IDisposable? probeFrame = capture.AcquireNextFrame(adapterIndex: 0, outputIndex: 0, timeoutMs: 100))
    {
        capture.ReleaseFrame();
    }

    DebugLog("Capture probe OK.");
}
catch (CaptureInitializationException ex)
{
    DebugLog("CaptureInitializationException — thoát CaptureInitAccessDenied:\n" + ex);
    return VisionExitCodes.CaptureInitAccessDenied;
}

// Architecture/05 mục 3.5 (ADR-65): context của output (adapter 0, output 0) đã tạo sẵn ở bước probe
// trên — tái dùng làm OutputCaptureContext đầu tiên thay vì tạo trùng 1 ID3D11Device thứ 2.
var initialOutputContext = new OutputCaptureContext(capture, new GpuWindowCropper(capture.Device));
var pipeline = new FrameClassificationPipeline(classifier);

var configHolder = new VisionRuntimeConfigHolder();
var captureLoop = new CaptureLoopWorker(configHolder, pipeline, client, initialOutputContext);

// Architecture/05 mục 3.6 (ADR-133): Thread thứ 3, đánh thức captureLoop ngay khi đổi cửa sổ
// foreground (PERF-020) — tái dùng đúng wakeEvent hiện có qua WakeUp(), không thêm cơ chế mới.
var messagePump = new WindowMessagePump(captureLoop.WakeUp);
captureLoop.AttachMessagePump(messagePump);
messagePump.Start();

captureLoop.Start(cts.Token);

DebugLog("Trước client.RunForeverAsync (bắt đầu connect pipe nghiệp vụ).");
try
{
    await client.RunForeverAsync(
        onBusinessMessage: (message, _) => HandleBusinessMessageAsync(message, configHolder, captureLoop),
        cts.Token,
        // Architecture/03 mục 6: mất kết nối = không còn nguồn cấu hình đáng tin cậy (SEC-017) —
        // tự park (giống monitoring_enabled=false) tới khi Service resend ControlVisionCommand
        // ngay sau khi reconnect thành công (03 mục 4.3).
        onDisconnected: () => configHolder.Update(VisionRuntimeConfig.CreateDefault()))
        .ConfigureAwait(false);
    DebugLog("RunForeverAsync trả về bình thường.");
    return 0;
}
catch (GracefulStopRequestedException)
{
    DebugLog("GracefulStopRequestedException — thoát 0.");
    return 0;
}
catch (Exception ex)
{
    DebugLog("RunForeverAsync ném exception KHÔNG lường trước:\n" + ex);
    throw;
}

static Task HandleBusinessMessageAsync(IpcPayload message, VisionRuntimeConfigHolder configHolder, CaptureLoopWorker captureLoop)
{
    if (message.BodyCase == IpcPayload.BodyOneofCase.ControlVision)
    {
        ControlVisionCommand command = message.ControlVision;
        configHolder.Update(new VisionRuntimeConfig(
            command.MonitoringEnabled,
            (int)command.CaptureIntervalMs,
            command.RiskThreshold,
            command.ExcludeProcessNames)
        {
            CoveredWindowHandles = command.CoveredWindowHandles.ToHashSet(),
        });
        captureLoop.WakeUp();
    }

    return Task.CompletedTask;
}
