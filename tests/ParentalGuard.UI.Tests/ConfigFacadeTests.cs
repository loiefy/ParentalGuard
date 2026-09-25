using System.IO.Pipes;
using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Client;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// `ConfigFacade` (Architecture/10-ui-architecture.md mục 5/6.4, `03-ipc-communication.md` mục 3.7) —
/// cùng mẫu hình loopback pipe với <see cref="PauseFacadeTests"/>/<see cref="DashboardFacadeTests"/>.
/// </summary>
public sealed class ConfigFacadeTests
{
    private static readonly byte[] _unsignedHelloKey = new byte[32];

    [Fact]
    public async Task GetConfigAsync_MapsResponseToSnapshot()
    {
        string pipeName = "test-config-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.ConfigQuery, req.BodyCase);
            var resp = new ConfigResponse { OverlayMessage = "Xin chao.", PerformanceMode = PerformanceMode.MaximumProtection };
            resp.UserWhitelistedProcessNames.Add("chrome.exe");
            resp.UserWhitelistedProcessNames.Add("steam.exe");
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.ConfigResp = resp);
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new ConfigFacade(client);

        ConfigSnapshot snapshot = await facade.GetConfigAsync(CancellationToken.None);

        Assert.Equal("Xin chao.", snapshot.OverlayMessage);
        Assert.Equal(["chrome.exe", "steam.exe"], snapshot.WhitelistedProcessNames);
        Assert.Equal(PerformanceModeOption.MaximumProtection, snapshot.PerformanceMode);

        await serverTask;
        await client.DisconnectAsync();
    }

    [Theory]
    [InlineData(ConfigUpdateResult.Success, ConfigUpdateOutcome.Success)]
    [InlineData(ConfigUpdateResult.InvalidCharacters, ConfigUpdateOutcome.InvalidCharacters)]
    [InlineData(ConfigUpdateResult.TooLong, ConfigUpdateOutcome.TooLong)]
    public async Task UpdateOverlayMessageAsync_SendsFullUpdate_MapsResultCorrectly(ConfigUpdateResult protoResult, ConfigUpdateOutcome expectedOutcome)
    {
        string pipeName = "test-config-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.ConfigUpdateReq, req.BodyCase);
            Assert.Equal("Thong diep moi.", req.ConfigUpdateReq.OverlayMessage);
            // "full update" — mục 6.4: request PHẢI kèm performance_mode hiện hành, không chỉ overlay_message.
            Assert.Equal(PerformanceMode.MaximumProtection, req.ConfigUpdateReq.PerformanceMode);
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.ConfigUpdateResp = new ConfigUpdateResponse { Result = protoResult });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new ConfigFacade(client);

        ConfigUpdateOutcome outcome = await facade.UpdateOverlayMessageAsync("Thong diep moi.", PerformanceModeOption.MaximumProtection, CancellationToken.None);

        Assert.Equal(expectedOutcome, outcome);
        await serverTask;
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task UpdatePerformanceModeAsync_SendsFullUpdate_IncludesOverlayMessage()
    {
        string pipeName = "test-config-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.ConfigUpdateReq, req.BodyCase);
            Assert.Equal("Da luu truoc do.", req.ConfigUpdateReq.OverlayMessage);
            Assert.Equal(PerformanceMode.Balanced, req.ConfigUpdateReq.PerformanceMode);
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.ConfigUpdateResp = new ConfigUpdateResponse { Result = ConfigUpdateResult.Success });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new ConfigFacade(client);

        ConfigUpdateOutcome outcome = await facade.UpdatePerformanceModeAsync("Da luu truoc do.", PerformanceModeOption.Balanced, CancellationToken.None);

        Assert.Equal(ConfigUpdateOutcome.Success, outcome);
        await serverTask;
        await client.DisconnectAsync();
    }

    [Theory]
    [InlineData(RemoveWhitelistEntryResult.Success, RemoveWhitelistOutcome.Success)]
    [InlineData(RemoveWhitelistEntryResult.InvalidToken, RemoveWhitelistOutcome.InvalidToken)]
    [InlineData(RemoveWhitelistEntryResult.NotFound, RemoveWhitelistOutcome.NotFound)]
    public async Task RemoveWhitelistEntryAsync_MapsResultCorrectly(RemoveWhitelistEntryResult protoResult, RemoveWhitelistOutcome expectedOutcome)
    {
        string pipeName = "test-config-facade-" + Guid.NewGuid().ToString("N");
        using var serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task serverTask = RunServerAsync(serverPipe, async (sessionKey, next) =>
        {
            IpcPayload req = await next();
            Assert.Equal(IpcPayload.BodyOneofCase.RemoveWhitelistReq, req.BodyCase);
            Assert.Equal("chrome.exe", req.RemoveWhitelistReq.ProcessName);
            Assert.Equal(new byte[] { 7, 8, 9 }, req.RemoveWhitelistReq.ActionToken.ToByteArray());
            await RespondAsync(serverPipe, sessionKey, req.MessageId, r => r.RemoveWhitelistResp = new RemoveWhitelistEntryResponse { Result = protoResult });
        });

        var client = new UiIpcClient(pipeName);
        await client.ConnectAsync(CancellationToken.None);
        var facade = new ConfigFacade(client);

        RemoveWhitelistOutcome outcome = await facade.RemoveWhitelistEntryAsync([7, 8, 9], "chrome.exe", CancellationToken.None);

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
