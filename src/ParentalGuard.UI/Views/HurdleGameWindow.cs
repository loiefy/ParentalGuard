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

/// <summary>
/// `PAUSE-046` (2026-10-08) — cửa sổ trò chơi nhảy vượt rào. Space hoặc chuột trái: bắt đầu / nhảy. Về đích, vấp rào hoặc đóng
/// cửa sổ (= thoát trò chơi) đều báo kết quả cho Service NGAY lúc đó qua <c>report</c>; Service mới là bên quyết định tạm dừng.
/// Dựng hoàn toàn bằng code (không XAML) — vài hình đơn giản cập nhật mỗi khung hình qua <see cref="CompositionTarget.Rendering"/>.
/// </summary>
public sealed partial class HurdleGameWindow : Window
{
    private const double ViewWidth = 960;
    private const double ViewHeight = 440;
    private const double GroundY = 330;
    private const double RunnerX = 170;
    private const double PixelsPerMeter = 55;
    private const int HurdlePool = 6;
    private const int LaneMarks = 14;

    private static readonly Color _trackColor = Color.FromArgb(255, 0xB5, 0x4A, 0x2E);
    private static readonly Color _runnerColor = Color.FromArgb(255, 0x2B, 0x7D, 0xE9);

    private readonly HurdleGameEngine _engine;
    private readonly Func<bool, uint, Task<ParentGameFinish>> _report;
    private readonly TaskCompletionSource<ParentGameFinish> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = new();
    private readonly Canvas _scene = new() { Width = ViewWidth, Height = ViewHeight };
    private readonly List<(Rectangle Bar, Rectangle LeftPost, Rectangle RightPost)> _hurdles = [];
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
    private readonly TextBlock _messageText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 18, Foreground = new SolidColorBrush(Colors.White) };
    private readonly Button _closeButton = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ContentControl _focusRoot = new() { IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private TimeSpan _lastTick;
    private bool _finished;
    private bool _reported;

    private HurdleGameWindow(uint targetMeters, ulong seed, string purposeText, Func<bool, uint, Task<ParentGameFinish>> report)
    {
        _engine = new HurdleGameEngine(targetMeters, seed);
        _report = report;
        Title = LocalizationService.Get("GameWindowTitle");

        BuildScene(purposeText);
        Closed += OnClosed;
        Activated += (_, _) => _focusRoot.Focus(FocusState.Programmatic);
        CompositionTarget.Rendering += OnRendering;
        SizeAndCenter();
        Render();
    }

    /// <summary>Mở cửa sổ trò chơi; kết thúc khi cửa sổ đóng, trả kết quả Service đã chấm (đóng giữa chừng = thua).</summary>
    public static Task<ParentGameFinish> PlayAsync(uint targetMeters, ulong seed, string purposeText, Func<bool, uint, Task<ParentGameFinish>> report)
    {
        var window = new HurdleGameWindow(targetMeters, seed, purposeText, report);
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

        for (int i = 0; i < HurdlePool; i++)
        {
            double heightPx = HurdleGameEngine.HurdleHeight * PixelsPerMeter;
            var bar = new Rectangle { Width = 30, Height = 8, Fill = new SolidColorBrush(Colors.White), Stroke = new SolidColorBrush(Color.FromArgb(255, 0xD0, 0x30, 0x30)), StrokeThickness = 2, RadiusX = 2, RadiusY = 2 };
            var left = new Rectangle { Width = 4, Height = heightPx, Fill = new SolidColorBrush(Color.FromArgb(255, 0xEE, 0xEE, 0xEE)) };
            var right = new Rectangle { Width = 4, Height = heightPx, Fill = new SolidColorBrush(Color.FromArgb(255, 0xEE, 0xEE, 0xEE)) };
            Canvas.SetTop(bar, GroundY - heightPx);
            Canvas.SetTop(left, GroundY - heightPx);
            Canvas.SetTop(right, GroundY - heightPx);
            _scene.Children.Add(left);
            _scene.Children.Add(right);
            _scene.Children.Add(bar);
            _hurdles.Add((bar, left, right));
        }

        // Nhân vật: đầu tròn, thân, tay chân vung theo bước chạy. Gốc toạ độ (0,0) = chân, trục y hướng lên dùng toạ độ âm.
        var head = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xC2, 0x9B)) };
        Canvas.SetLeft(head, -9);
        Canvas.SetTop(head, -78);
        var body = new Rectangle { Width = 12, Height = 30, Fill = new SolidColorBrush(_runnerColor), RadiusX = 4, RadiusY = 4 };
        Canvas.SetLeft(body, -6);
        Canvas.SetTop(body, -60);
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
        _closeButton.Visibility = Visibility.Collapsed;
        _closeButton.Click += (_, _) => Close();
        var messageStack = new StackPanel { Spacing = 14 };
        messageStack.Children.Add(_messageText);
        messageStack.Children.Add(_closeButton);
        _messagePanel.Child = messageStack;
        _messagePanel.Width = 560;
        _messagePanel.Padding = new Thickness(24, 18, 24, 18);
        _messagePanel.CornerRadius = new CornerRadius(10);
        _messagePanel.Background = new SolidColorBrush(Color.FromArgb(215, 0x10, 0x18, 0x28));
        Canvas.SetLeft(_messagePanel, (ViewWidth - 560) / 2);
        Canvas.SetTop(_messagePanel, 120);
        _scene.Children.Add(_messagePanel);
        ShowMessage(LocalizationService.GetFormatted("GameStartPrompt", _engine.TargetMeters, purposeText), showClose: false);

        var viewbox = new Viewbox { Child = _scene, Stretch = Stretch.Uniform };
        var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 0x0E, 0x16, 0x24)) };
        root.Children.Add(viewbox);
        root.PointerPressed += OnPointerPressed;
        _focusRoot.Content = root;
        _focusRoot.KeyDown += OnKeyDown;
        Content = _focusRoot;
    }

    private static Line NewLimb() => new() { Stroke = new SolidColorBrush(Color.FromArgb(255, 0x1E, 0x2E, 0x48)), StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

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

    private void Render()
    {
        double distance = _engine.Distance;
        _distanceText.Text = LocalizationService.GetFormatted("GameDistanceFormat", ((int)distance).ToString("N0", LocalizationService.Culture), _engine.TargetMeters.ToString("N0", LocalizationService.Culture));
        _progress.Value = distance / _engine.TargetMeters;
        TimeSpan elapsed = TimeSpan.FromSeconds(_engine.ElapsedSeconds);
        _timeText.Text = LocalizationService.GetFormatted("GameTimeFormat", elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture));

        double laneSpacing = ViewWidth / LaneMarks;
        double laneOffset = (distance * PixelsPerMeter) % laneSpacing;
        for (int i = 0; i < _laneMarks.Count; i++)
        {
            Canvas.SetLeft(_laneMarks[i], (i * laneSpacing) - laneOffset);
        }

        int slot = 0;
        foreach (double h in _engine.Hurdles)
        {
            double x = RunnerX + ((h - distance) * PixelsPerMeter);
            if (x < -40)
            {
                continue;
            }

            if (x > ViewWidth + 40 || slot >= _hurdles.Count)
            {
                break;
            }

            (Rectangle bar, Rectangle left, Rectangle right) = _hurdles[slot++];
            Canvas.SetLeft(bar, x - 15);
            Canvas.SetLeft(left, x - 13);
            Canvas.SetLeft(right, x + 9);
            bar.Visibility = left.Visibility = right.Visibility = Visibility.Visible;
        }

        for (; slot < _hurdles.Count; slot++)
        {
            (Rectangle bar, Rectangle left, Rectangle right) = _hurdles[slot];
            bar.Visibility = left.Visibility = right.Visibility = Visibility.Collapsed;
        }

        Canvas.SetLeft(_runner, RunnerX);
        Canvas.SetTop(_runner, GroundY - (_engine.RunnerHeight * PixelsPerMeter));

        // Tay chân: vung theo quãng đường khi chạy, co lại khi đang nhảy.
        double swing = _engine.IsAirborne ? 0.9 : Math.Sin(distance * 3.2) * 0.7;
        SetLimb(_legFront, 0, -30, swing, 30);
        SetLimb(_legBack, 0, -30, -swing, 30);
        SetLimb(_armFront, 0, -54, -swing * 0.8, 22);
        SetLimb(_armBack, 0, -54, swing * 0.8, 22);
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
        ShowMessage(LocalizationService.Get(won ? "GameWonChecking" : "GameLostChecking"), showClose: false);
        ParentGameFinish finish = await ReportAsync(won).ConfigureAwait(true);
        string text = finish.Outcome switch
        {
            ParentGameOutcome.Paused => LocalizationService.Get("GameResultPaused"),
            ParentGameOutcome.Applied => LocalizationService.Get("GameResultApplied"),
            ParentGameOutcome.Lost => LocalizationService.GetFormatted("GameResultLost", ((int)_engine.Distance).ToString("N0", LocalizationService.Culture)),
            ParentGameOutcome.TooFast => LocalizationService.Get("GameResultTooFast"),
            _ => LocalizationService.Get("GameResultFailed"),
        };
        ShowMessage(text, showClose: true);
        if (finish.Outcome is ParentGameOutcome.Paused or ParentGameOutcome.Applied)
        {
            await Task.Delay(1500).ConfigureAwait(true);
            Close();
        }
    }

    /// <summary>Báo kết quả đúng 1 lần (về đích, vấp rào, hoặc đóng cửa sổ giữa chừng).</summary>
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
            finish = await _report(completed, (uint)_engine.Distance).ConfigureAwait(true);
        }
        catch (UiIpcConnectionException)
        {
            finish = new ParentGameFinish(ParentGameOutcome.Failed, 0);
        }

        _result.TrySetResult(finish);
        return finish;
    }

    private void ShowMessage(string text, bool showClose)
    {
        _messageText.Text = text;
        _closeButton.Visibility = showClose ? Visibility.Visible : Visibility.Collapsed;
        _messagePanel.Visibility = Visibility.Visible;
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
