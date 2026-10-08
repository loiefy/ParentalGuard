using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.Views;

namespace ParentalGuard.UI.Services;

/// <inheritdoc cref="IParentGameService"/>
public sealed class ParentGameService(IParentProtectionFacade facade) : IParentGameService
{
    public async Task<ParentGameFinish> PauseWithGameAsync(byte[] actionToken, PauseDurationOption duration)
    {
        ParentGameStart start = await facade.StartPauseGameAsync(actionToken, duration, CancellationToken.None).ConfigureAwait(true);
        return await PlayAsync(start, LocalizationService.Get("GamePurposePause")).ConfigureAwait(true);
    }

    public async Task<ParentGameFinish> ChangeSettingsWithGameAsync(bool enabled, uint gameMeters)
    {
        ParentGameStart start = await facade.StartSettingsGameAsync(enabled, gameMeters, CancellationToken.None).ConfigureAwait(true);
        return await PlayAsync(start, LocalizationService.Get("GamePurposeSettings")).ConfigureAwait(true);
    }

    private async Task<ParentGameFinish> PlayAsync(ParentGameStart start, string purposeText)
    {
        if (start.Outcome != ParentGameOutcome.Started)
        {
            return new ParentGameFinish(start.Outcome, 0);
        }

        // Cửa sổ trò chơi tự báo kết quả về Service NGAY khi ván kết thúc (về đích / vấp rào / đóng cửa sổ), rồi mới đóng.
        return await HurdleGameWindow.PlayAsync(start.TargetMeters, start.Seed, purposeText, (completed, meters) =>
            facade.FinishGameAsync(completed, meters, CancellationToken.None)).ConfigureAwait(true);
    }
}
