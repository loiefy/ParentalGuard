using Microsoft.UI.Xaml;

namespace ParentalGuard.UI.Services;

/// <summary>
/// Seam cho `S5` Auth Modal (Architecture/10-ui-architecture.md mục 6.5) — tách khỏi
/// <see cref="NavigationService"/> để <c>DashboardViewModel</c> test được nhánh
/// <c>InvalidToken</c>/tự mở lại `S5` bằng fake, không cần <see cref="XamlRoot"/>/<c>ContentDialog</c> thật.
/// </summary>
public interface IAuthPromptService
{
    /// <summary>Hiện `S5`, trả <c>action_token</c> (null nếu người dùng huỷ).</summary>
    Task<byte[]?> ShowAuthPromptAsync(string actionContext, XamlRoot xamlRoot);
}
