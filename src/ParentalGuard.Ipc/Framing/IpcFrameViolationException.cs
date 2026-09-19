namespace ParentalGuard.Ipc.Framing;

/// <summary>
/// Vi phạm framing/HMAC ở tầng IPC (Architecture/03-ipc-communication.md mục 2.3/5.4/6):
/// kích thước vượt giới hạn, HMAC sai, hoặc payload không parse được. Bên nhận phải đóng
/// kết nối ngay, không phản hồi lỗi (ADR-22) — không phải lỗi lập trình, không cho lan ra
/// làm crash Service (SEC mục 2 Elevation of Privilege).
/// </summary>
public sealed class IpcFrameViolationException : Exception
{
    public IpcFrameViolationException(string message)
        : base(message)
    {
    }

    public IpcFrameViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
