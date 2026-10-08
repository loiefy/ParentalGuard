using ParentalGuard.UI.Game;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.Views;

namespace ParentalGuard.UI.Services;

/// <inheritdoc cref="IParentGameService"/>
public sealed class ParentGameService(IParentProtectionFacade facade, NavigationService navigationService) : IParentGameService
{
    private readonly LossStreakTracker _streak = new();

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

        // Cửa sổ trò chơi tự báo kết quả về Service NGAY khi ván kết thúc (về đích / thua / đóng cửa sổ), rồi mới đóng.
        var callbacks = new HurdleGameCallbacks(
            (completed, meters) => facade.FinishGameAsync(completed, meters, CancellationToken.None),
            _streak.RecordLoss,
            () => navigationService.ShowInMainShell(typeof(DonatePage)));
        ParentGameFinish finish = await HurdleGameWindow.PlayAsync(start.TargetMeters, start.Seed, purposeText, callbacks).ConfigureAwait(true);
        if (finish.Outcome is ParentGameOutcome.Paused or ParentGameOutcome.Applied)
        {
            _streak.RecordWin();
        }

        return finish;
    }
}
