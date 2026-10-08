using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Game;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace ParentalGuard.UI.Views;

/// <summary>Các hành động cửa sổ trò chơi cần từ bên ngoài (Service + điều hướng app).</summary>
/// <param name="Report">Báo kết quả ván (completed, mét đã chạy) cho Service — Service chấm và quyết định.</param>
/// <param name="ShouldSuggestStop">`PAUSE-049`: gọi mỗi lần thua; <c>true</c> = hiện "Hay là không tạm dừng nữa?" + nút Donate.</param>
/// <param name="OpenDonate">Bấm "Donate us": mở tab Ủng hộ dự án ở cửa sổ chính.</param>
public sealed record HurdleGameCallbacks(Func<bool, uint, Task<ParentGameFinish>> Report, Func<bool> ShouldSuggestStop, Action OpenDonate);

/// <summary>
/// `PAUSE-046`/`PAUSE-046b`/`PAUSE-049` (2026-10-08) — cửa sổ trò chơi nhảy vượt rào. Space hoặc chuột trái: bắt đầu / nhảy. Về đích,
/// vấp rào, rơi xuống hố hoặc đóng cửa sổ (= thoát trò chơi) đều báo kết quả cho Service NGAY lúc đó; Service mới là bên quyết định
/// tạm dừng. Dựng hoàn toàn bằng code (không XAML) — vài hình đơn giản cập nhật mỗi khung hình qua <see cref="CompositionTarget.Rendering"/>.
/// </summary>
public sealed partial class HurdleGameWindow : Window
{
    private const double ViewWidth = 960;
    private const double ViewHeight = 440;
    private const double GroundY = 330;
    private const double RunnerX = 120;

    /// <summary>35 px/m: nhìn trước ~24 m — ở tốc độ tối đa (pace 2, 8,3 m/giây) vẫn thấy trước ~3 giây.</summary>
    private const double PixelsPerMeter = 35;
    private const int HurdlePool = 24;
    private const int PitPool = 6;
    private const int LaneMarks = 14;

    private static readonly Color _trackColor = Color.FromArgb(255, 0xB5, 0x4A, 0x2E);
    private static readonly Color _runnerColor = Color.FromArgb(255, 0x2B, 0x7D, 0xE9);

    private readonly HurdleGameEngine _engine;
    private readonly HurdleGameCallbacks _callbacks;
    private readonly TaskCompletionSource<ParentGameFinish> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = new();
    private readonly Canvas _scene = new() { Width = ViewWidth, Height = ViewHeight };
    private readonly List<(Rectangle Bar, Rectangle LeftPost, Rectangle RightPost)> _hurdles = [];
    private readonly List<Rectangle> _pits = [];
    private readonly List<Rectangle> _laneMarks = [];
    private readonly Canvas _runner = new();
    private readonly Line _legFront = NewLimb();
    private readonly Line _legBack = NewLimb();
    private readonly Line _armFront = NewLimb();
    private readonly Line _armBack = NewLimb();
    private readonly TextBlock _distanceText = NewHudText(22, FontWeights.SemiBold);
    private readonly TextBlock _timeText = NewHudText(16, FontWeights.Normal);
    private readonly ProgressBar _progress = new() { Width = 300, Minimum = 0, Maximum = 1 };
    private readonly Border _messagePanel = new();
    private readonly TextBlock _messageTitle = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) };
    private readonly TextBlock _messageText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 16, Foreground = new SolidColorBrush(Colors.White) };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _closeButton = new();
    private readonly Button _laterButton = new();
    private readonly Button _donateButton = new() { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    private readonly ContentControl _focusRoot = new() { IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private TimeSpan _lastTick;
    private bool _finished;
    private bool _reported;
    private bool _donateRequested;

    private HurdleGameWindow(uint targetMeters, ulong seed, string purposeText, HurdleGameCallbacks callbacks)
    {
        _engine = new HurdleGameEngine(targetMeters, seed);
        _callbacks = callbacks;
        Title = LocalizationService.Get("GameWindowTitle");

        BuildScene(purposeText);
        Closed += OnClosed;
        Activated += (_, _) => _focusRoot.Focus(FocusState.Programmatic);
        CompositionTarget.Rendering += OnRendering;
        SizeAndCenter();
        Render();
    }

    /// <summary>Mở cửa sổ trò chơi; kết thúc khi cửa sổ đóng, trả kết quả Service đã chấm (đóng giữa chừng = thua).</summary>
    public static Task<ParentGameFinish> PlayAsync(uint targetMeters, ulong seed, string purposeText, HurdleGameCallbacks callbacks)
    {
        var window = new HurdleGameWindow(targetMeters, seed, purposeText, callbacks);
        window.Activate();
        return window._result.Task;
    }

    private void BuildScene(string purposeText)
    {
        var sky = new Rectangle { Width = ViewWidth, Height = GroundY };
        sky.Fill = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(255, 0x14, 0x2A, 0x4A), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(255, 0x3A, 0x6E, 0xA8), Offset = 1 },
            },
        };
        _scene.Children.Add(sky);

        var grass = new Rectangle { Width = ViewWidth, Height = ViewHeight - GroundY, Fill = new SolidColorBrush(Color.FromArgb(255, 0x2E, 0x5E, 0x2E)) };
        Canvas.SetTop(grass, GroundY);
        _scene.Children.Add(grass);

        var track = new Rectangle { Width = ViewWidth, Height = 46, Fill = new SolidColorBrush(_trackColor) };
        Canvas.SetTop(track, GroundY);
        _scene.Children.Add(track);

        for (int i = 0; i < LaneMarks; i++)
        {
            var mark = new Rectangle { Width = 34, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) };
            Canvas.SetTop(mark, GroundY + 21);
            _laneMarks.Add(mark);
            _scene.Children.Add(mark);
        }

        // Hố: khoảng tối cắt ngang đường chạy (vẽ SAU vạch kẻ để che vạch).
        for (int i = 0; i < PitPool; i++)
        {
            var pit = new Rectangle
            {
                Height = ViewHeight - GroundY,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(255, 0x24, 0x18, 0x10), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 0x05, 0x05, 0x05), Offset = 1 },
                    },
                },
                Visibility = Visibility.Collapsed,
            };
            Canvas.SetTop(pit, GroundY);
            _pits.Add(pit);
            _scene.Children.Add(pit);
        }

        double hurdlePx = HurdleGameEngine.HurdleHeight * PixelsPerMeter;
        for (int i = 0; i < HurdlePool; i++)
        {
            var bar = new Rectangle { Width = 12, Height = 6, Fill = new SolidColorBrush(Colors.White), Stroke = new SolidColorBrush(Color.FromArgb(255, 0xD0, 0x30, 0x30)), StrokeThickness = 2, RadiusX = 2, RadiusY = 2 };
            var left = new Rectangle { Width = 3, Height = hurdlePx, Fill = new SolidColorBrush(Color.FromArgb(255, 0xEE, 0xEE, 0xEE)) };
            var right = new Rectangle { Width = 3, Height = hurdlePx, Fill = new SolidColorBrush(Color.FromArgb(255, 0xEE, 0xEE, 0xEE)) };
            Canvas.SetTop(bar, GroundY - hurdlePx);
            Canvas.SetTop(left, GroundY - hurdlePx);
            Canvas.SetTop(right, GroundY - hurdlePx);
            _scene.Children.Add(left);
            _scene.Children.Add(right);
            _scene.Children.Add(bar);
            _hurdles.Add((bar, left, right));
        }

        // Nhân vật (~1,7 m ở tỉ lệ 35 px/m): đầu tròn, thân, tay chân vung theo bước chạy. Gốc (0,0) = chân, trục y hướng lên dùng toạ độ âm.
        var head = new Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xC2, 0x9B)) };
        Canvas.SetLeft(head, -6.5);
        Canvas.SetTop(head, -60);
        var body = new Rectangle { Width = 9, Height = 23, Fill = new SolidColorBrush(_runnerColor), RadiusX = 3, RadiusY = 3 };
        Canvas.SetLeft(body, -4.5);
        Canvas.SetTop(body, -46);
        foreach (UIElement part in new UIElement[] { _legBack, _armBack, body, head, _legFront, _armFront })
        {
            _runner.Children.Add(part);
        }

        _scene.Children.Add(_runner);

        var hud = new StackPanel { Spacing = 6, Margin = new Thickness(20, 14, 0, 0) };
        hud.Children.Add(_distanceText);
        hud.Children.Add(_progress);
        hud.Children.Add(_timeText);
        _scene.Children.Add(hud);

        var hint = NewHudText(14, FontWeights.Normal);
        hint.Text = LocalizationService.GetFormatted("GameControlsHint", purposeText);
        hint.Opacity = 0.85;
        hint.Width = 420;
        hint.TextWrapping = TextWrapping.Wrap;
        hint.TextAlignment = TextAlignment.Right;
        Canvas.SetLeft(hint, ViewWidth - 440);
        Canvas.SetTop(hint, 16);
        _scene.Children.Add(hint);

        _closeButton.Content = LocalizationService.Get("GameCloseButton");
        _closeButton.Click += (_, _) => Close();
        _laterButton.Content = LocalizationService.Get("GameLaterButton");
        _laterButton.Click += (_, _) => Close();
        _donateButton.Content = LocalizationService.Get("GameDonateButton");
        _donateButton.Click += (_, _) =>
        {
            _donateRequested = true;
            Close();
        };
        var messageStack = new StackPanel { Spacing = 12 };
        messageStack.Children.Add(_messageTitle);
        messageStack.Children.Add(_messageText);
        messageStack.Children.Add(_buttons);
        _messagePanel.Child = messageStack;
        _messagePanel.Width = 600;
        _messagePanel.Padding = new Thickness(24, 18, 24, 18);
        _messagePanel.CornerRadius = new CornerRadius(10);
        _messagePanel.Background = new SolidColorBrush(Color.FromArgb(225, 0x10, 0x18, 0x28));
        Canvas.SetLeft(_messagePanel, (ViewWidth - 600) / 2);
        Canvas.SetTop(_messagePanel, 110);
        _scene.Children.Add(_messagePanel);
        ShowMessage(null, LocalizationService.GetFormatted("GameStartPrompt", _engine.TargetMeters, purposeText));

        var viewbox = new Viewbox { Child = _scene, Stretch = Stretch.Uniform };
        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 0x0E, 0x16, 0x24)) };
        root.Children.Add(viewbox);
        root.PointerPressed += OnPointerPressed;
        _focusRoot.Content = root;
        _focusRoot.KeyDown += OnKeyDown;
        Content = _focusRoot;
    }

    private static Line NewLimb() => new() { Stroke = new SolidColorBrush(Color.FromArgb(255, 0x1E, 0x2E, 0x48)), StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    private static TextBlock NewHudText(double size, Windows.UI.Text.FontWeight weight) => new() { FontSize = size, FontWeight = weight, Foreground = new SolidColorBrush(Colors.White) };

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Space or VirtualKey.Up)
        {
            e.Handled = true;
            Press();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse || e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            _focusRoot.Focus(FocusState.Programmatic);
            Press();
        }
    }

    private void Press()
    {
        if (_finished)
        {
            return;
        }

        bool wasReady = _engine.State == HurdleGameState.Ready;
        _engine.Press();
        if (wasReady && _engine.State == HurdleGameState.Running)
        {
            _messagePanel.Visibility = Visibility.Collapsed;
            _clock.Start();
            _lastTick = _clock.Elapsed;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        if (_engine.State == HurdleGameState.Running)
        {
            TimeSpan now = _clock.Elapsed;
            _engine.Advance((now - _lastTick).TotalSeconds);
            _lastTick = now;
        }

        Render();
        if (!_finished && _engine.State is HurdleGameState.Won or HurdleGameState.Lost)
        {
            _finished = true;
            _ = OnGameOverAsync(_engine.State == HurdleGameState.Won);
        }
    }

    private double ScreenX(double meters) => RunnerX + ((meters - _engine.Distance) * PixelsPerMeter);

    private void Render()
    {
        double distance = _engine.Distance;
        _distanceText.Text = LocalizationService.GetFormatted("GameDistanceFormat", ((int)distance).ToString("N0", LocalizationService.Culture), _engine.TargetMeters.ToString("N0", LocalizationService.Culture));
        _progress.Value = distance / _engine.TargetMeters;
        TimeSpan elapsed = TimeSpan.FromSeconds(_engine.ElapsedSeconds);
        TimeSpan pace = TimeSpan.FromSeconds(_engine.PaceSecondsPerKm);
        _timeText.Text = LocalizationService.GetFormatted("GameTimeFormat", elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture))
            + "    " + LocalizationService.GetFormatted("GamePaceFormat", pace.ToString(@"m\:ss", CultureInfo.InvariantCulture));

        double laneSpacing = ViewWidth / LaneMarks;
        double laneOffset = (distance * PixelsPerMeter) % laneSpacing;
        for (int i = 0; i < _laneMarks.Count; i++)
        {
            Canvas.SetLeft(_laneMarks[i], (i * laneSpacing) - laneOffset);
        }

        int hurdleSlot = 0;
        int pitSlot = 0;
        foreach (Obstacle o in _engine.Obstacles)
        {
            double startX = ScreenX(o.Start);
            double endX = ScreenX(o.End);
            if (endX < -60)
            {
                continue;
            }

            if (startX > ViewWidth + 60)
            {
                break;
            }

            if (o.Kind == ObstacleKind.Pit && pitSlot < _pits.Count)
            {
                Rectangle pit = _pits[pitSlot++];
                pit.Width = Math.Max(1, endX - startX);
                Canvas.SetLeft(pit, startX);
                pit.Visibility = Visibility.Visible;
            }
            else if (o.Kind == ObstacleKind.Hurdle && hurdleSlot < _hurdles.Count)
            {
                (Rectangle bar, Rectangle left, Rectangle right) = _hurdles[hurdleSlot++];
                Canvas.SetLeft(bar, startX - 6);
                Canvas.SetLeft(left, startX - 5);
                Canvas.SetLeft(right, startX + 2);
                bar.Visibility = left.Visibility = right.Visibility = Visibility.Visible;
            }
        }

        for (; pitSlot < _pits.Count; pitSlot++)
        {
            _pits[pitSlot].Visibility = Visibility.Collapsed;
        }

        for (; hurdleSlot < _hurdles.Count; hurdleSlot++)
        {
            (Rectangle bar, Rectangle left, Rectangle right) = _hurdles[hurdleSlot];
            bar.Visibility = left.Visibility = right.Visibility = Visibility.Collapsed;
        }

        // Rơi xuống hố: nhân vật chìm xuống lòng hố.
        double sink = _engine.State == HurdleGameState.Lost && _engine.LostTo == ObstacleKind.Pit ? 40 : 0;
        Canvas.SetLeft(_runner, RunnerX);
        Canvas.SetTop(_runner, GroundY - (_engine.RunnerHeight * PixelsPerMeter) + sink);

        // Tay chân: vung theo quãng đường khi chạy, co lại khi đang nhảy.
        double swing = _engine.IsAirborne ? 0.9 : Math.Sin(distance * 2.4) * 0.7;
        SetLimb(_legFront, 0, -23, swing, 23);
        SetLimb(_legBack, 0, -23, -swing, 23);
        SetLimb(_armFront, 0, -42, -swing * 0.8, 17);
        SetLimb(_armBack, 0, -42, swing * 0.8, 17);
    }

    private static void SetLimb(Line limb, double x, double y, double angle, double length)
    {
        limb.X1 = x;
        limb.Y1 = y;
        limb.X2 = x + (Math.Sin(angle) * length);
        limb.Y2 = y + (Math.Cos(angle) * length);
    }

    private async Task OnGameOverAsync(bool won)
    {
        _clock.Stop();
        ShowMessage(null, LocalizationService.Get(won ? "GameWonChecking" : "GameLostChecking"));
        ParentGameFinish finish = await ReportAsync(won).ConfigureAwait(true);
        switch (finish.Outcome)
        {
            case ParentGameOutcome.Paused:
            case ParentGameOutcome.Applied:
                ShowMessage(null, LocalizationService.Get(finish.Outcome == ParentGameOutcome.Paused ? "GameResultPaused" : "GameResultApplied"), close: true);
                await Task.Delay(1500).ConfigureAwait(true);
                Close();
                break;
            case ParentGameOutcome.Lost:
                // `PAUSE-049`: mỗi lần thua nhắc rằng máy vẫn đang được bảo vệ; thua liên tiếp 5–10 lần (ngẫu nhiên) thì gợi ý thôi tạm dừng.
                string meters = ((int)_engine.Distance).ToString("N0", LocalizationService.Culture);
                string detail = LocalizationService.GetFormatted(_engine.LostTo == ObstacleKind.Pit ? "GameLostDetailPit" : "GameLostDetailHurdle", meters);
                if (_callbacks.ShouldSuggestStop())
                {
                    ShowMessage(LocalizationService.Get("GameSuggestStop"), detail, close: true, donate: true);
                }
                else
                {
                    ShowMessage(LocalizationService.Get("GameLostProtected"), detail, close: true);
                }

                break;
            case ParentGameOutcome.TooFast:
                ShowMessage(null, LocalizationService.Get("GameResultTooFast"), close: true);
                break;
            default:
                ShowMessage(null, LocalizationService.Get("GameResultFailed"), close: true);
                break;
        }
    }

    /// <summary>Báo kết quả đúng 1 lần (về đích, thua, hoặc đóng cửa sổ giữa chừng).</summary>
    private async Task<ParentGameFinish> ReportAsync(bool completed)
    {
        if (_reported)
        {
            return await _result.Task.ConfigureAwait(true);
        }

        _reported = true;
        ParentGameFinish finish;
        try
        {
            finish = await _callbacks.Report(completed, (uint)_engine.Distance).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException)
        {
            finish = new ParentGameFinish(ParentGameOutcome.Failed, 0);
        }

        _result.TrySetResult(finish);
        return finish;
    }

    private void ShowMessage(string? title, string text, bool close = false, bool donate = false)
    {
        _messageTitle.Text = title ?? string.Empty;
        _messageTitle.Visibility = title is null ? Visibility.Collapsed : Visibility.Visible;
        _messageText.Text = text;
        _buttons.Children.Clear();
        if (donate)
        {
            // Chủ dự án yêu cầu (2026-10-08): kèm nút "Để sau" thay cho "Đóng".
            _buttons.Children.Add(_donateButton);
            _buttons.Children.Add(_laterButton);
        }
        else if (close)
        {
            _buttons.Children.Add(_closeButton);
        }

        _buttons.Visibility = _buttons.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _messagePanel.Visibility = Visibility.Visible;
        (donate ? _donateButton : close ? _closeButton : null)?.Focus(FocusState.Programmatic);
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        CompositionTarget.Rendering -= OnRendering;
        _clock.Stop();
        if (!_reported)
        {
            // `PAUSE-046`: thoát trò chơi = không tạm dừng — vẫn báo Service để ván kết thúc và ghi nhật ký.
            await ReportAsync(completed: false).ConfigureAwait(true);
        }

        if (_donateRequested)
        {
            _callbacks.OpenDonate();
        }
    }

    private void SizeAndCenter()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var size = new SizeInt32((int)(ViewWidth * scale), (int)((ViewHeight + 40) * scale));
        AppWindow.Resize(size);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(area.X + ((area.Width - size.Width) / 2), area.Y + ((area.Height - size.Height) / 2)));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);
}
