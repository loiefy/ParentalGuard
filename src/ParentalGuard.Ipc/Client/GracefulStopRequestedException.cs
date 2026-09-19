namespace ParentalGuard.Ipc.Client;

/// <summary>
/// Server gửi <c>GracefulStopCommand</c> (Architecture/03 mục 3.2) — tiến trình con phải tự
/// thoát trong <see cref="DeadlineMs"/>, nếu không <c>Service</c> sẽ force-kill.
/// </summary>
public sealed class GracefulStopRequestedException(uint deadlineMs, string reason) : Exception($"Graceful stop requested: {reason}")
{
    public uint DeadlineMs { get; } = deadlineMs;

    public string Reason { get; } = reason;
}
