using Microsoft.ML.OnnxRuntime;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Configuration;
using ParentalGuard.Vision.Inference;
using ParentalGuard.Vision.Pipeline;

// Đợt 1 (ROADMAP.md): pipeline 7 bước đầy đủ (Architecture/05-image-pipeline-architecture.md).
// Tiến trình này chỉ được Service spawn qua ChildProcessLauncher (bootstrap handle truyền qua
// STD_INPUT_HANDLE, không qua command-line — Architecture/03 mục 5.2, ADR-18).

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

ChildIpcBootstrap bootstrap;
try
{
    bootstrap = ChildIpcBootstrap.ReadFromInheritedStdHandle();
}
catch (Exception ex) when (ex is IOException or ArgumentException or EndOfStreamException)
{
    // Không có Service để báo lỗi ở bước này — tự thoát; Service phát hiện qua ngân sách
    // 2 giây (Architecture/03 mục 4.1) và spawn lại.
    Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
    return 1;
}

var client = new IpcChildClient(ProcessType.Vision, bootstrap);

byte[] modelBytes = await File.ReadAllBytesAsync(ModelPaths.OnnxModelPath, cts.Token).ConfigureAwait(false);

OrtEnv.Instance().DisableTelemetryEvents(); // IMG-015 — hardening bắt buộc dù ONNX Runtime mặc định không cần network.
using var sessionOptions = new SessionOptions();
try
{
    sessionOptions.AppendExecutionProvider_DML(deviceId: 0);
    client.DiagnosticState = "ep=directml";
}
catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
{
    // PERF-031/ADR-45: fallback CPU EP nếu driver/GPU không hỗ trợ DirectML — không log (Vision
    // không ghi file, SEC-017/BE-022), chỉ báo qua HeartbeatAck.DiagnosticState (chẩn đoán thuần).
    client.DiagnosticState = "ep=cpu-fallback";
}

using INsfwClassifier classifier = new NsfwClassifier(modelBytes, sessionOptions);
Array.Clear(modelBytes);

DesktopDuplicationCapture capture;
try
{
    capture = new DesktopDuplicationCapture();
    // Architecture/05 mục 8.1 bước 2-3: probe khởi tạo Desktop Duplication TRƯỚC khi bắt đầu
    // CaptureLoopWorker — bất kỳ lỗi nào ở đây bị coi là "có thể do Integrity Level" (ADR-49).
    capture.AcquireNextFrame(adapterIndex: 0, outputIndex: 0, timeoutMs: 100);
}
catch (CaptureInitializationException)
{
    return VisionExitCodes.CaptureInitAccessDenied;
}

// Architecture/05 mục 3.5 (ADR-65): context của output (adapter 0, output 0) đã tạo sẵn ở bước probe
// trên — tái dùng làm OutputCaptureContext đầu tiên thay vì tạo trùng 1 ID3D11Device thứ 2.
var initialOutputContext = new OutputCaptureContext(capture, new GpuWindowCropper(capture.Device));
var pipeline = new FrameClassificationPipeline(classifier);

var configHolder = new VisionRuntimeConfigHolder();
var captureLoop = new CaptureLoopWorker(configHolder, pipeline, client, initialOutputContext);
captureLoop.Start(cts.Token);

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
    return 0;
}
catch (GracefulStopRequestedException)
{
    return 0;
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
            command.ExcludeProcessNames));
        captureLoop.WakeUp();
    }

    return Task.CompletedTask;
}
