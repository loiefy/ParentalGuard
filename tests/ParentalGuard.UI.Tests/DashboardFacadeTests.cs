using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `DashboardFacade` (Architecture/10-ui-architecture.md mục 5/6.2, `03-ipc-communication.md` mục
/// 3.7) — tự viết server-side handshake/dispatch tối giản trên 1 named pipe loopback thật (cùng mẫu
/// hình <c>UiIpcClientTests</c>, `tests/ParentalGuard.Service.Tests/UiIpcClientTests.cs</c>), không
/// cần dựng <c>ParentalGuard.Service</c> thật.
/// </summary>
public sealed class DashboardFacadeTests
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    [Fact]
    public async Task GetStatusAsync_CombinesDashboardAndPauseQueries_OnSameConnection()
    {
        string pipeName = "test-dashboard-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload dashboardReq = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.DashboardStatusQuery, dashboardReq.BodyCase);
            await RespondAsync(serverPipe, sessionKey, dashboardReq.MessageId, resp => resp.DashboardStatusResp = new DashboardStatusResponse
            {
                WatchdogAlive = true,
                VisionConnected = true,
                VisionDiagnosticState = "ep=cpu-fallback",
                OverlayConnected = false,
                UsingFallbackConfig = true,
                AuditLogFreeDiskBytes = 123,
                PauseAnomalyPendingAck = true,
            });

            IpcPayload pauseReq = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.PauseStatusQuery, pauseReq.BodyCase);
            await RespondAsync(serverPipe, sessionKey, pauseReq.MessageId, resp => resp.PauseStatusResp = new PauseStatusResponse
            {
                IsPaused = true,
                PauseExpiresAtUnixMs = 999,
            });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new DashboardFacade(client);

        DashboardStatus status = await facade.GetStatusAsync(CancellationToken.None);

        Assert.True(status.WatchdogAlive);
        Assert.True(status.VisionConnected);
        Assert.Equal("ep=cpu-fallback", status.VisionDiagnosticState);
        Assert.False(status.OverlayConnected);
        Assert.True(status.UsingFallbackConfig);
        Assert.Equal(123, status.AuditLogFreeDiskBytes);
        Assert.True(status.PauseAnomalyPendingAck);
        Assert.True(status.IsPaused);
        Assert.Equal(999, status.PauseExpiresAtUnixMs);

        await serverTask;
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task AcknowledgePauseAnomalyAsync_SendsRequest_AndCompletes()
    {
        string pipeName = "test-dashboard-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.AckPauseAnomalyReq, req.BodyCase);
            await RespondAsync(serverPipe, sessionKey, req.MessageId, resp => resp.AckPauseAnomalyResp = new AcknowledgePauseAnomalyResponse { Acknowledged = true });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new DashboardFacade(client);

        await facade.AcknowledgePauseAnomalyAsync(CancellationToken.None);

        await serverTask;
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task GetAuditChartAsync_MapsDaysToPocoList()
    {
        string pipeName = "test-dashboard-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.AuditChartQuery, req.BodyCase);
            Assert.Equal(7u, req.AuditChartQuery.RangeDays);
            var resp = new AuditChartResponse();
            resp.Days.Add(new ParentalGuard.Ipc.Protocol.DailyBlockCount { DateUtc = "2026-09-20", BlockedCount = 2 });
            resp.Days.Add(new ParentalGuard.Ipc.Protocol.DailyBlockCount { DateUtc = "2026-09-21", BlockedCount = 0 });
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.AuditChartResp = resp);
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new DashboardFacade(client);

        IReadOnlyList<ParentalGuard.UI.Services.IpcClient.DailyBlockCount> days = await facade.GetAuditChartAsync(7, CancellationToken.None);

        Assert.Equal(2, days.Count);
        Assert.Equal("2026-09-20", days[0].DateUtc);
        Assert.Equal(2u, days[0].BlockedCount);
        Assert.Equal(0u, days[1].BlockedCount);

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
