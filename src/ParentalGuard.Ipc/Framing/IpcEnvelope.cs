using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Ipc.Framing;

/// <summary>Tạo <see cref="IpcPayload"/> envelope đúng field bắt buộc (Architecture/03 mục 3.1).</summary>
public static class IpcEnvelope
{
    public const uint SchemaVersion = 1;

    public static IpcPayload NewEnvelope(ProcessType sender, ulong messageId, ulong correlationId = 0)
    {
        return new IpcPayload
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Sender = sender,
            SchemaVersion = SchemaVersion,
        };
    }
}

/// <summary>Sinh <c>message_id</c> tăng dần theo từng kết nối (mục 3.1).</summary>
public sealed class IpcMessageIdGenerator
{
    private long _lastId;

    public ulong Next() => (ulong)Interlocked.Increment(ref _lastId);
}
