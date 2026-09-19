using System.Security.Cryptography;
using Google.Protobuf;
using ParentalGuard.Ipc.Protocol;

namespace ParentalGuard.Ipc.Framing;

/// <summary>
/// Length-prefixed framing + HMAC-SHA256 trailer cho <see cref="IpcPayload"/>
/// (Architecture/03-ipc-communication.md mục 2.3, 5). Wire format:
/// [4 byte LE uint32 length][N byte IpcPayload][32 byte HMAC-SHA256(payload, key)].
/// </summary>
public static class IpcFrameTransport
{
    /// <summary>Giới hạn kích thước payload (SEC-012, ADR-21).</summary>
    public const int MaxPayloadBytes = 65536;

    public static async Task WriteFrameAsync(Stream stream, IpcPayload payload, byte[] hmacKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(hmacKey);

        byte[] body = payload.ToByteArray();
        if (body.Length > MaxPayloadBytes)
        {
            // Message do chính process này tạo ra vượt ngưỡng là lỗi lập trình (không phải input
            // không tin cậy), nên fail loud thay vì âm thầm cắt bớt dữ liệu.
            throw new InvalidOperationException(
                $"IpcPayload serialized size {body.Length} exceeds {MaxPayloadBytes} bytes (SEC-012).");
        }

        byte[] lengthPrefix = BitConverter.GetBytes((uint)body.Length);
        byte[] mac = ComputeHmac(hmacKey, body);

        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(mac, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Đọc 1 frame. Ném <see cref="IpcFrameViolationException"/> nếu vi phạm giới hạn kích
    /// thước hoặc HMAC sai/payload hỏng — caller phải đóng kết nối ngay khi bắt exception này,
    /// không gửi phản hồi lỗi (mục 5.4/6, ADR-22).
    /// </summary>
    public static async Task<IpcPayload> ReadFrameAsync(Stream stream, byte[] hmacKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(hmacKey);

        byte[] lengthPrefix = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        uint length = BitConverter.ToUInt32(lengthPrefix);
        if (length > MaxPayloadBytes)
        {
            // Không đọc tiếp phần payload theo đúng yêu cầu mục 2.3 — tránh cấp phát buffer
            // theo kích thước bên gửi tự khai.
            throw new IpcFrameViolationException(
                $"Declared payload length {length} exceeds {MaxPayloadBytes} bytes limit (SEC-012).");
        }

        byte[] body = await ReadExactAsync(stream, (int)length, cancellationToken).ConfigureAwait(false);
        byte[] receivedMac = await ReadExactAsync(stream, 32, cancellationToken).ConfigureAwait(false);
        byte[] expectedMac = ComputeHmac(hmacKey, body);

        if (!CryptographicOperations.FixedTimeEquals(receivedMac, expectedMac))
        {
            throw new IpcFrameViolationException("HMAC verification failed.");
        }

        try
        {
            return IpcPayload.Parser.ParseFrom(body);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new IpcFrameViolationException("Malformed IpcPayload.", ex);
        }
    }

    private static byte[] ComputeHmac(byte[] key, byte[] body)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(body);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                // Đầu bên kia đóng pipe giữa chừng — coi như "pipe broken" (03 mục 6),
                // không phải HMAC/framing fail.
                throw new IOException("Pipe has been ended.");
            }

            offset += read;
        }

        return buffer;
    }
}
