using Microsoft.UI.Xaml;

namespace ParentalGuard.UI.Services;

/// <summary>
/// `PAUSE-041` (2026-10-07) — seam hiện hộp thoại thử thách "Bảo vệ cả phụ huynh"; tách ra để ViewModel test được bằng fake
/// (cùng mẫu hình <see cref="IAuthPromptService"/>).
/// </summary>
public interface IParentChallengePromptService
{
    /// <summary><c>true</c> nếu đã vượt thử thách (Service đã ghi nhận), <c>false</c> nếu huỷ hoặc đang bị khoá.</summary>
    Task<bool> ShowChallengeAsync(XamlRoot xamlRoot);
}
