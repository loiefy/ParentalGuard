using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `PAUSE-041` (2026-10-07) — thử thách "Bảo vệ cả phụ huynh": 5 phép tính, 60 giây, phải đúng 5/5. Sai/hết giờ → Service cấp
/// bộ câu hỏi mới; thất bại 3 lần liên tiếp → khoá 5 phút. Hết giờ thì tự nộp bài (Service chấm "hết giờ").
/// </summary>
public sealed partial class ParentChallengeDialog : ContentDialog
{
    private readonly IParentProtectionFacade _facade;
    private readonly StackPanel _questionsPanel = new() { Spacing = 8 };
    private readonly TextBlock _countdownText = new() { Opacity = 0.8 };
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
    private readonly List<TextBox> _answerInputs = [];
    private DispatcherQueueTimer? _timer;
    private long _expiresAtUnixMs;
    private bool _passed;
    private bool _submitting;

    public ParentChallengeDialog(IParentProtectionFacade facade)
    {
        _facade = facade;
        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out object style))
        {
            Style = (Style)style;
        }

        Title = LocalizationService.Get("ChallengeTitle");
        PrimaryButtonText = LocalizationService.Get("ChallengeSubmitButton");
        CloseButtonText = LocalizationService.Get("ChallengeCancelButton");
        DefaultButton = ContentDialogButton.Primary;

        var root = new StackPanel { Spacing = 12, MinWidth = 340 };
        root.Children.Add(new TextBlock { Text = LocalizationService.Get("ChallengeIntro"), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_countdownText);
        root.Children.Add(_questionsPanel);
        root.Children.Add(_statusText);
        Content = root;

        PrimaryButtonClick += OnPrimaryButtonClick;
        Closed += (_, _) => _timer?.Stop();
    }

    /// <summary>Hiện hộp thoại; <c>true</c> nếu Service đã chấm "đã vượt qua".</summary>
    public async Task<bool> RunAsync()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += async (_, _) => await OnTickAsync().ConfigureAwait(true);

        await LoadNewChallengeAsync(null).ConfigureAwait(true);
        await ShowAsync();
        return _passed;
    }

    private async Task LoadNewChallengeAsync(string? status)
    {
        _timer?.Stop();
        _questionsPanel.Children.Clear();
        _answerInputs.Clear();
        _statusText.Text = status ?? string.Empty;
        try
        {
            ChallengeStart start = await _facade.StartChallengeAsync(CancellationToken.None).ConfigureAwait(true);
            if (start.Outcome == ChallengeOutcome.LockedOut)
            {
                ShowLockedOut(start.LockedUntilUnixMs);
                return;
            }

            _expiresAtUnixMs = start.ExpiresAtUnixMs;
            foreach (string question in start.Questions)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                row.Children.Add(new TextBlock { Text = $"{question} =", MinWidth = 90, VerticalAlignment = VerticalAlignment.Center, FontSize = 16 });
                var input = new TextBox { Width = 120, InputScope = NumericInputScope(), MaxLength = 6 };
                _answerInputs.Add(input);
                row.Children.Add(input);
                _questionsPanel.Children.Add(row);
            }

            IsPrimaryButtonEnabled = true;
            UpdateCountdown();
            _timer?.Start();
            if (_answerInputs.Count > 0)
            {
                _answerInputs[0].Focus(FocusState.Programmatic);
            }
        }
        catch (UiIpcConnectionException ex)
        {
            _statusText.Text = ex.Message;
            IsPrimaryButtonEnabled = false;
        }
    }

    private static InputScope NumericInputScope()
    {
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        return scope;
    }

    private void ShowLockedOut(long lockedUntilUnixMs)
    {
        _timer?.Stop();
        _questionsPanel.Children.Clear();
        _answerInputs.Clear();
        _countdownText.Text = string.Empty;
        string until = DateTimeOffset.FromUnixTimeMilliseconds(lockedUntilUnixMs).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        _statusText.Text = LocalizationService.GetFormatted("ChallengeLockedOut", until);
        IsPrimaryButtonEnabled = false;
    }

    private long RemainingSeconds => Math.Max(0, (_expiresAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 999) / 1000);

    private void UpdateCountdown() => _countdownText.Text = LocalizationService.GetFormatted("ChallengeCountdown", RemainingSeconds);

    private async Task OnTickAsync()
    {
        UpdateCountdown();
        if (RemainingSeconds == 0 && !_submitting)
        {
            await SubmitAsync().ConfigureAwait(true);
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            args.Cancel = true; // tự Hide() khi vượt qua — sai thì giữ hộp thoại với bộ câu hỏi mới
            await SubmitAsync().ConfigureAwait(true);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task SubmitAsync()
    {
        if (_submitting || _answerInputs.Count == 0)
        {
            return;
        }

        _submitting = true;
        _timer?.Stop();
        try
        {
            // Ô trống/không phải số → gửi giá trị chắc chắn sai: Service vẫn tính 1 lần thất bại (không cho dò đáp án miễn phí).
            int[] answers = [.. _answerInputs.Select(i => int.TryParse(i.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) ? v : int.MinValue)];
            ChallengeSubmitResult result = await _facade.SubmitChallengeAsync(answers, CancellationToken.None).ConfigureAwait(true);
            switch (result.Outcome)
            {
                case ChallengeOutcome.Passed:
                    _passed = true;
                    Hide();
                    break;
                case ChallengeOutcome.LockedOut:
                    ShowLockedOut(result.LockedUntilUnixMs);
                    break;
                case ChallengeOutcome.Expired:
                    await LoadNewChallengeAsync(LocalizationService.GetFormatted("ChallengeExpired", result.FailuresBeforeLockout)).ConfigureAwait(true);
                    break;
                default:
                    await LoadNewChallengeAsync(LocalizationService.GetFormatted("ChallengeWrong", result.FailuresBeforeLockout)).ConfigureAwait(true);
                    break;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            _statusText.Text = ex.Message;
        }
        finally
        {
            _submitting = false;
        }
    }
}
