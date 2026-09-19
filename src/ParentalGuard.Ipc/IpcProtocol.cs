namespace ParentalGuard.Ipc;

/// <summary>Hằng số giao thức dùng chung 2 phía (Architecture/03 mục 3.2 <c>Hello.protocol_version</c>).</summary>
public static class IpcProtocol
{
    public const uint CurrentVersion = 1;

    /// <summary>
    /// Khoá HMAC dùng cho TOÀN BỘ kết nối pipe <c>ParentalGuard.Svc.Watchdog</c> (Architecture/09-anti-tamper-architecture.md
    /// mục 3.2): "không cần session_key... phòng chống giả mạo đã đủ bằng ACL SYSTEM-only + code-signing,
    /// không cần thêm lớp HMAC ephemeral". Cả 2 phía (Service/Watchdog) đều LocalSystem — không có bí mật
    /// nào cần bảo vệ qua khoá này, HMAC ở đây chỉ còn ý nghĩa toàn vẹn khung (framing), không phải xác
    /// thực danh tính (danh tính đã do ACL pipe + <c>Hello.process_type</c> đảm nhiệm). Cùng khuôn mẫu
    /// khoá hằng số 32-byte-zero đã dùng cho <c>Hello</c> chưa ký của pipe UI (<c>UiSessionServer._unsignedHelloKey</c>).
    /// </summary>
    public static readonly byte[] WatchdogPipeKey = new byte[32];
}
