namespace ParentalGuard.Service.Auth;

/// <summary>
/// <c>auth.dat</c> tồn tại nhưng không đọc/giải mã/parse được (DPAPI lỗi, JSON hỏng, PHC string
/// hỏng...). KHÁC nhánh fail-secure của <c>config.db</c> (Architecture/04 mục 6, chỉ áp dụng cho
/// domain giám sát) — xử lý ở <see cref="AuthCoordinator"/> (xem ghi chú "gap cần xác nhận" ở đó).
/// </summary>
public sealed class AuthDataCorruptException(string message, Exception? inner = null) : Exception(message, inner);
