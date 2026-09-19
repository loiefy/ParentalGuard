using System.Security.Cryptography;
using ParentalGuard.Ipc.Framing;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Service.Tests;

public class IpcFrameTransportTests
{
    private static byte[] TestKey => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task WriteThenRead_RoundTripsSamePayload()
    {
        byte[] key = TestKey;
        var original = new IpcPayload
        {
            MessageId = 42,
            Sender = ProcessType.Vision,
            SchemaVersion = 1,
            Hello = new Hello { ProcessType = ProcessType.Vision, Pid = 1234, ProtocolVersion = 1 },
        };

        using var stream = new MemoryStream();
        await IpcFrameTransport.WriteFrameAsync(stream, original, key, CancellationToken.None);
        stream.Position = 0;

        IpcPayload result = await IpcFrameTransport.ReadFrameAsync(stream, key, CancellationToken.None);

        Assert.Equal(original.MessageId, result.MessageId);
        Assert.Equal(original.Hello.Pid, result.Hello.Pid);
    }

    [Fact]
    public async Task ReadFrame_WithWrongKey_ThrowsFrameViolation()
    {
        var payload = new IpcPayload { MessageId = 1, HeartbeatPing = new HeartbeatPing { Sequence = 1 } };
        using var stream = new MemoryStream();
        await IpcFrameTransport.WriteFrameAsync(stream, payload, TestKey, CancellationToken.None);
        stream.Position = 0;

        await Assert.ThrowsAsync<IpcFrameViolationException>(
            () => IpcFrameTransport.ReadFrameAsync(stream, TestKey, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_WithTamperedPayload_ThrowsFrameViolation()
    {
        byte[] key = TestKey;
        var payload = new IpcPayload { MessageId = 1, HeartbeatPing = new HeartbeatPing { Sequence = 7 } };
        using var stream = new MemoryStream();
        await IpcFrameTransport.WriteFrameAsync(stream, payload, key, CancellationToken.None);

        byte[] bytes = stream.ToArray();
        bytes[10] ^= 0xFF; // lật 1 byte trong phần payload đã serialize
        using var tamperedStream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<IpcFrameViolationException>(
            () => IpcFrameTransport.ReadFrameAsync(tamperedStream, key, CancellationToken.None));
    }

    [Fact]
    public async Task ReadFrame_WithDeclaredLengthOverLimit_ThrowsWithoutReadingPayload()
    {
        using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes((uint)(IpcFrameTransport.MaxPayloadBytes + 1)));
        stream.Position = 0;

        await Assert.ThrowsAsync<IpcFrameViolationException>(
            () => IpcFrameTransport.ReadFrameAsync(stream, TestKey, CancellationToken.None));
    }
}
