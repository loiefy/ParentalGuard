using Microsoft.UI.Xaml;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.Views;

namespace ParentalGuard.UI.Services;

/// <inheritdoc cref="IParentChallengePromptService"/>
public sealed class ParentChallengePromptService(IParentProtectionFacade facade) : IParentChallengePromptService
{
    public async Task<bool> ShowChallengeAsync(XamlRoot xamlRoot)
    {
        var dialog = new ParentChallengeDialog(facade) { XamlRoot = xamlRoot };
        return await dialog.RunAsync().ConfigureAwait(true);
    }
}
