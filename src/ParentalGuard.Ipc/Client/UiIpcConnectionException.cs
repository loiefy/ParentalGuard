namespace ParentalGuard.Ipc.Client;

/// <summary>
/// Lỗi kết nối/giao tiếp qua pipe <c>ParentalGuard.Svc.UI</c> (Architecture/10-ui-architecture.md
/// mục 3.2/9) — Facade/ViewModel bắt exception này để chuyển UI sang trạng thái lỗi (màn hình lỗi
/// kết nối lúc khởi động, hoặc banner "mất kết nối" giữa chừng). <see cref="UiIpcClient"/> KHÔNG tự
/// động retry ngầm khi ném exception này — user luôn có thể tự bấm "Thử lại" (mục 3.2).
/// </summary>
public sealed class UiIpcConnectionException : Exception
{
    public UiIpcConnectionException(string message)
        : base(message)
    {
    }

    public UiIpcConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
