using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ParentalGuard.Ipc.Client;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;
using ParentalGuard.UI.ViewModels;

namespace ParentalGuard.UI.Views;

/// <summary>
/// `S3` Audit log (Architecture/10-ui-architecture.md mục 6.3/6.8) — 2026-10-05 (`PWD-024`, `FE-080`/`FE-081`):
/// không còn mở `S5` khi vào tab; chưa đăng nhập phụ huynh thì chỉ hiện khung đăng nhập, đăng nhập xong (ở tab này
/// hoặc tab Cài đặt) mới tải lịch sử bằng phiên. Hết phiên/đăng xuất → xoá nội dung đang hiển thị.
/// </summary>
public sealed partial class AuditLogPage : Page
{
    private readonly ParentSessionService _parentSession;

    public AuditLogPage()
    {
        InitializeComponent();

        IServiceProvider services = ((App)Application.Current).Services;
        _parentSession = services.GetRequiredService<ParentSessionService>();
        ViewModel = new AuditLogViewModel(
            services.GetRequiredService<IAuditFacade>(),
            _parentSession,
            _parentSession.MarkLoggedOut);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) => _parentSession.SessionChanged -= OnParentSessionChanged;

        EmptyText.Text = LocalizationService.Get("AuditLogEmptyText");
        LoadMoreButton.Content = LocalizationService.Get("AuditLoadMoreButton");
        ExportPdfButton.Content = LocalizationService.Get("AuditExportPdfButton");
        foreach ((uint? days, string key) in _exportRanges)
        {
            ExportRangeCombo.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(key), Tag = days });
        }

        ExportRangeCombo.SelectedIndex = 1;
    }

    /// <summary>`MISC-011`: 7 ngày / 30 ngày / 3 tháng / 6 tháng / toàn bộ.</summary>
    private static readonly (uint? Days, string Key)[] _exportRanges =
    [
        (7, "AuditExportRange7Days"),
        (30, "AuditExportRange30Days"),
        (90, "AuditExportRange3Months"),
        (180, "AuditExportRange6Months"),
        (null, "AuditExportRangeAll"),
    ];

    public AuditLogViewModel ViewModel { get; }

    /// <summary>
    /// Bug real-hardware (2026-10-01, tab Lịch sử trống): lúc <c>OnNavigatedTo</c> Page CHƯA vào visual tree
    /// nên <see cref="UIElement.XamlRoot"/> còn null → <c>ContentDialog</c> `S5` ném exception, bị
    /// <c>_ =</c> nuốt mất → trang trắng, không dialog, không lỗi. Chờ <c>Loaded</c> (đã có XamlRoot).
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _parentSession.SessionChanged += OnParentSessionChanged;
        if (XamlRoot is not null)
        {
            StartGate();
            return;
        }

        Loaded += OnFirstLoaded;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _parentSession.SessionChanged -= OnParentSessionChanged;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        StartGate();
    }

    private void OnParentSessionChanged(object? sender, EventArgs e)
    {
        ViewModel.Reset();
        StartGate();
    }

    private async void StartGate()
    {
        if (!_parentSession.IsLoggedIn)
        {
            return; // FE-081: chỉ hiện khung đăng nhập.
        }

        try
        {
            await ViewModel.InitializeAsync(XamlRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Không để lỗi dựng dialog biến thành trang trắng câm lặng — hiện rõ cho phụ huynh.
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Trước 2026-10-05: huỷ `S5` → quay về `S2`. Nay ở lại tab, khung đăng nhập hiện ra (FE-081).
    }

    private async void OnLoadMoreClick(object sender, RoutedEventArgs e) => await ViewModel.LoadMoreAsync();

    /// <summary>`MISC-011`: dựng PDF trong bộ nhớ rồi lưu qua hộp thoại Lưu của Windows (không ghi file tạm nào).</summary>
    private async void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        if (ExportRangeCombo.SelectedItem is not ComboBoxItem { Content: string rangeText } item)
        {
            return;
        }

        ExportPdfButton.IsEnabled = false;
        try
        {
            ViewModel.ErrorMessage = null;
            ViewModel.StatusMessage = null;
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"ParentalGuard-{DateTime.Now:yyyy-MM-dd}",
            };
            picker.FileTypeChoices.Add("PDF", [".pdf"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, ((App)Application.Current).MainWindowHandle);
            Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            byte[]? pdf = await ViewModel.BuildPdfAsync((uint?)item.Tag, rangeText, LocalizationService.CurrentLanguageCode);
            if (pdf is null)
            {
                return;
            }

            await Windows.Storage.FileIO.WriteBytesAsync(file, pdf);
            ViewModel.StatusMessage = LocalizationService.GetFormatted("AuditExportPdfDone", file.Path);
        }
        catch (Exception ex) when (ex is UiIpcConnectionException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            ViewModel.ErrorMessage = LocalizationService.GetFormatted("AuditExportPdfFailed", ex.Message);
        }
        finally
        {
            ExportPdfButton.IsEnabled = true;
        }
    }

}
