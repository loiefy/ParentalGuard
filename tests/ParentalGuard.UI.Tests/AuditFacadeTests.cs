using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `AuditFacade` (Architecture/10-ui-architecture.md mục 5/6.3, `03-ipc-communication.md` mục 3.7) —
/// cùng mẫu hình loopback pipe với <see cref="PauseFacadeTests"/>/<see cref="DashboardFacadeTests"/>.
/// </summary>
public sealed class AuditFacadeTests
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    [Theory]
    [InlineData(AuditLogQueryResult.Success, AuditLogQueryOutcome.Success)]
    [InlineData(AuditLogQueryResult.InvalidToken, AuditLogQueryOutcome.InvalidToken)]
    public async Task GetAuditLogAsync_MapsResultCorrectly(AuditLogQueryResult protoResult, AuditLogQueryOutcome expectedOutcome)
    {
        string pipeName = "test-audit-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.AuditLogQuery, req.BodyCase);
            Assert.Equal(new byte[] { 1, 2, 3 }, req.AuditLogQuery.ActionToken.ToByteArray());
            Assert.Equal(0u, req.AuditLogQuery.Page);
            Assert.Equal(50u, req.AuditLogQuery.PageSize);

            var resp = new AuditLogResponse { Result = protoResult, HasMore = protoResult == AuditLogQueryResult.Success };
            if (protoResult == AuditLogQueryResult.Success)
            {
                resp.Entries.Add(new ParentalGuard.Ipc.Protocol.AuditLogEntry
                {
                    Seq = 1,
                    TsUnixMs = 1758000000000,
                    EventType = "ContentBlocked",
                    ProcessName = "chrome.exe",
                    RiskScore = 0.87f,
                });
            }

            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.AuditLogResp = resp);
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new AuditFacade(client);

        AuditLogFetchResult result = await facade.GetAuditLogAsync([1, 2, 3], page: 0, pageSize: 50, CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Outcome);
        if (expectedOutcome == AuditLogQueryOutcome.Success)
        {
            ParentalGuard.UI.Services.IpcClient.AuditLogEntry entry = Assert.Single(result.Entries);
            Assert.Equal(1uL, entry.Seq);
            Assert.Equal("ContentBlocked", entry.EventType);
            Assert.Equal("chrome.exe", entry.ProcessName);
            Assert.Equal(0.87f, entry.RiskScore);
            Assert.True(result.HasMore);
        }
        else
        {
            Assert.Empty(result.Entries);
        }

        await serverTask;
        await client.DisconnectAsync();
    }

    [Theory]
    [InlineData(MarkFalsePositiveResult.Success, MarkFalsePositiveOutcome.Success)]
    [InlineData(MarkFalsePositiveResult.InvalidToken, MarkFalsePositiveOutcome.InvalidToken)]
    [InlineData(MarkFalsePositiveResult.AlreadyListed, MarkFalsePositiveOutcome.AlreadyListed)]
    public async Task MarkFalsePositiveAsync_MapsResultCorrectly(MarkFalsePositiveResult protoResult, MarkFalsePositiveOutcome expectedOutcome)
    {
        string pipeName = "test-audit-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.MarkFalsePositiveReq, req.BodyCase);
            Assert.Equal("chrome.exe", req.MarkFalsePositiveReq.ProcessName);
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.MarkFalsePositiveResp = new MarkFalsePositiveResponse { Result = protoResult });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new AuditFacade(client);

        MarkFalsePositiveOutcome outcome = await facade.MarkFalsePositiveAsync([7, 8, 9], "chrome.exe", CancellationToken.None);

        Assert.Equal(expectedOutcome, outcome);
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
