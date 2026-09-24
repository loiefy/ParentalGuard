using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `PauseFacade` (Architecture/10-ui-architecture.md mục 5/6.2.2, `02-process-architecture.md` mục
/// 3a.1/3a.2) — cùng mẫu hình loopback pipe với <see cref="DashboardFacadeTests"/>.
/// </summary>
public sealed class PauseFacadeTests
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    [Theory]
    [InlineData(PauseResult.Success, PauseOutcome.Success)]
    [InlineData(PauseResult.InvalidToken, PauseOutcome.InvalidToken)]
    [InlineData(PauseResult.AlreadyPaused, PauseOutcome.AlreadyPaused)]
    public async Task PauseMonitoringAsync_MapsResultCorrectly(PauseResult protoResult, PauseOutcome expectedOutcome)
    {
        string pipeName = "test-pause-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.PauseMonitoringReq, req.BodyCase);
            Assert.Equal(PauseDuration.OneHour, req.PauseMonitoringReq.Duration);
            Assert.Equal(new byte[] { 1, 2, 3 }, req.PauseMonitoringReq.ActionToken.ToByteArray());
            await RespondAsync(serverPipe, sessionKey, req.MessageId, resp => resp.PauseMonitoringResp = new PauseMonitoringResponse
            {
                Result = protoResult,
                PauseExpiresAtUnixMs = protoResult == PauseResult.Success ? 12345 : 0,
            });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new PauseFacade(client);

        PauseMonitoringResult result = await facade.PauseMonitoringAsync(new byte[] { 1, 2, 3 }, PauseDurationOption.OneHour, CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Outcome);
        await serverTask;
        await client.DisconnectAsync();
    }

    [Theory]
    [InlineData(ResumeResult.Success, ResumeOutcome.Success)]
    [InlineData(ResumeResult.InvalidToken, ResumeOutcome.InvalidToken)]
    [InlineData(ResumeResult.NotPaused, ResumeOutcome.NotPaused)]
    public async Task ResumeMonitoringAsync_MapsResultCorrectly(ResumeResult protoResult, ResumeOutcome expectedOutcome)
    {
        string pipeName = "test-pause-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.ResumeMonitoringReq, req.BodyCase);
            await RespondAsync(serverPipe, sessionKey, req.MessageId, resp => resp.ResumeMonitoringResp = new ResumeMonitoringResponse { Result = protoResult });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new PauseFacade(client);

        ResumeMonitoringResult result = await facade.ResumeMonitoringAsync(new byte[] { 4, 5, 6 }, CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Outcome);
        await serverTask;
        await client.DisconnectAsync();
    }

    private static async Task RunServerAsync(NamedPipeServerStream serverPipe, Func<byte[], Func<Task<IpcPayload>>, Task> handle)
    {
        await serverPipe.WaitForConnectionAsync();

        IpcPayload hello = await IpcFrameTransport.ReadFrameAsync(serverPipe, _unsignedHelloKey, CancellationToken.None);
        Assert.Equal(IpcPayload.BodyOneofCase.Hello, hello.BodyCase);

        byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
        IpcPayload ack = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: 1, correlationId: hello.MessageId);
        ack.HelloAck = new HelloAck { Accepted = true, SessionKey = ByteString.CopyFrom(sessionKey), ServerTimeUnixMs = 0 };
        await IpcFrameTransport.WriteFrameAsync(serverPipe, ack, _unsignedHelloKey, CancellationToken.None);

        await handle(sessionKey, () => IpcFrameTransport.ReadFrameAsync(serverPipe, sessionKey, CancellationToken.None));
    }

    private static ulong _nextServerMessageId = 2;

    private static Task RespondAsync(NamedPipeServerStream serverPipe, byte[] sessionKey, ulong correlationId, Action<IpcPayload> setBody)
    {
        IpcPayload resp = IpcEnvelope.NewEnvelope(ProcessType.Service, messageId: _nextServerMessageId++, correlationId: correlationId);
        setBody(resp);
        return IpcFrameTransport.WriteFrameAsync(serverPipe, resp, sessionKey, CancellationToken.None);
    }
}
