using System.Numerics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S1` bước 1 — Giới thiệu (`FE-031`, `FE-032`), thuần UI, không có IPC. Chỉ xuất hiện khi máy chưa từng đặt mật khẩu
/// (<c>App</c> chỉ điều hướng `S1` khi <c>AuthStatusQuery.password_configured=false</c>).
/// </summary>
public sealed partial class OnboardingWelcomePage : Page
{
    /// <summary>Thẻ "Bảo vệ cả phụ huynh" chỉ hiện khi tính năng đó đã phát hành (`FE-032`, mục 9 TODO).</summary>
    private const bool ParentProtectionFeatureAvailable = true;

    private static readonly (string Glyph, string Key, bool Visible)[] _cards =
    [
        ("", "OnboardingCard1", true), // Shield
        ("", "OnboardingCard2", true), // Lock
        ("", "OnboardingCard3", true), // Laptop
        ("", "OnboardingCard4", ParentProtectionFeatureAvailable), // People
        ("", "OnboardingCard5", true), // Code
    ];

    public OnboardingWelcomePage()
    {
        InitializeComponent();
        TitleText.Text = LocalizationService.Get("OnboardingWelcomeTitle");
        BodyText.Text = LocalizationService.Get("OnboardingWelcomeBody");
        ContinueButton.Content = LocalizationService.Get("ContinueButton");

        foreach ((string glyph, string key, bool visible) in _cards)
        {
            if (visible)
            {
                ValueCards.Children.Add(BuildCard(glyph, LocalizationService.Get(key + "Title"), LocalizationService.Get(key + "Body")));
            }
        }
    }

    public OnboardingViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (OnboardingViewModel)e.Parameter;
        ViewModel.IntroAcknowledged = true; // FE-031a: không còn ô tick xác nhận — Tiếp tục luôn bấm được.
        Bindings.Update();
    }

    /// <summary>
    /// FE-032a: thẻ không nền riêng (trong suốt — vẫn nhận chuột), biểu tượng tròn màu nhấn + tiêu đề đậm + mô tả chữ nhỏ;
    /// rê chuột → nền mờ nhẹ + viền màu nhấn, nổi lên 2px.
    /// </summary>
    private static Border BuildCard(string glyph, string title, string body)
    {
        var icon = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top };
        icon.Children.Add(new Ellipse { Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] });
        icon.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
        });

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = body, Opacity = 0.7, FontSize = 13, TextWrapping = TextWrapping.Wrap });

        var layout = new Grid { ColumnSpacing = 14 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        layout.Children.Add(icon);
        layout.Children.Add(text);

        var normal = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var normalBorder = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var hover = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var card = new Border
        {
            Child = layout,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = normalBorder,
            Background = normal,
            TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) },
        };
        card.PointerEntered += (_, _) =>
        {
            card.Background = hover;
            card.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            card.Translation = new Vector3(0, -2, 0);
        };
        card.PointerExited += (_, _) =>
        {
            card.Background = normal;
            card.BorderBrush = normalBorder;
            card.Translation = Vector3.Zero;
        };
        return card;
    }
}
