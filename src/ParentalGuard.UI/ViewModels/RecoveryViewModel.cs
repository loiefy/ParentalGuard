using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using ParentalGuard.Ipc.Client;
using ParentalGuard.UI.Services;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.ViewModels;

/// <summary>
/// `S6` Recovery (Architecture/10-ui-architecture.md mục 6.6, `PWD-032`/`033`, Architecture/08 mục
/// 6.2/7.5) — khôi phục mật khẩu qua Recovery Key, tự gate qua <c>recovery_key</c> (không qua `S5`).
/// </summary>
public sealed partial class RecoveryViewModel(IAuthFacade authFacade) : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsSubmitEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    /// <summary>Defense in depth — khoá nút "Khôi phục" trong lúc đang xử lý, tránh double-submit (cùng mẫu hình <c>SettingsViewModel.CanSubmitChangePassword</c>).</summary>
    public bool CanSubmit => IsSubmitEnabled && !IsBusy;

    /// <summary>
    /// Security audit Đợt 6 S6 (bug đã sửa) — khoá <c>CancelLink</c> trong lúc <see cref="SubmitAsync"/>
    /// đang treo IPC: rời trang giữa chừng lúc này khiến Recovery Key mới (nếu Service trả Success sau
    /// đó) không còn ai hiển thị/acknowledge được, phải zero câm lặng (xem <see cref="_discarded"/>) —
    /// khoá nút để tránh mất Recovery Key mới một cách không cần thiết trong đường đi có thể tránh được.
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewRecoveryKeyDisplay))]
    [NotifyPropertyChangedFor(nameof(IsFormVisible))]
    public partial string? NewRecoveryKeyDisplay { get; set; }

    /// <summary>x:Bind hỗ trợ chuyển <c>bool</c> → <c>Visibility</c> trực tiếp, không cần converter riêng.</summary>
    public bool HasNewRecoveryKeyDisplay => !string.IsNullOrEmpty(NewRecoveryKeyDisplay);

    /// <summary>Form nhập (Recovery Key/mật khẩu mới) ẩn đi sau khi hiện Recovery Key mới (mục 6.6).</summary>
    public bool IsFormVisible => !HasNewRecoveryKeyDisplay;

    private byte[]? _newRecoveryKeyPlaintextBuffer;

    /// <summary>
    /// Security audit Đợt 6 S6 (bug đã sửa) — <c>true</c> sau khi trang/app đã bị rời trong lúc
    /// <see cref="SubmitAsync"/> còn treo IPC (<see cref="MarkDiscarded"/>, gọi từ
    /// <c>RecoveryPage.OnNavigatedFrom</c>/<c>App.OnWindowClosed</c>). Nếu Service vẫn trả Success SAU
    /// thời điểm đó, buffer Recovery Key mới bị zero ngay lập tức thay vì publish lên
    /// <see cref="NewRecoveryKeyDisplay"/> của 1 ViewModel mồ côi mà không còn đường zero nào khác.
    /// </summary>
    private volatile bool _discarded;

    /// <summary>
    /// Architecture/08 mục 6.2 — chuẩn hoá client-side TRƯỚC khi gửi: bỏ dấu <c>-</c>/khoảng trắng,
    /// chuyển hoa. Service vẫn chuẩn hoá lại lần nữa, không tin input UI.
    /// </summary>
    public static string NormalizeRecoveryKey(string raw)
    {
        Span<char> buffer = raw.Length <= 128 ? stackalloc char[raw.Length] : new char[raw.Length];
        int len = 0;
        foreach (char c in raw)
        {
            if (c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            buffer[len++] = char.ToUpperInvariant(c);
        }

        return new string(buffer[..len]);
    }

    /// <summary>
    /// Mục 6.6. <paramref name="recoveryKeyUtf8Pinned"/>/<paramref name="newPasswordUtf8Pinned"/> —
    /// buffer pinned caller (code-behind) vừa đọc/chuẩn hoá; <see cref="IAuthFacade.RecoveryResetAsync"/>
    /// tự zero cả 2 (Architecture/08 mục 5.3).
    /// </summary>
    public async Task SubmitAsync(byte[] recoveryKeyUtf8Pinned, byte[] newPasswordUtf8Pinned, CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            RecoveryResult result = await authFacade.RecoveryResetAsync(recoveryKeyUtf8Pinned, newPasswordUtf8Pinned, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case RecoveryOutcome.Success:
                    if (_discarded)
                    {
                        // Security audit Đợt 6 S6 (bug đã sửa) — trang đã bị rời (Cancel/đóng app) trong
                        // lúc chờ IPC. Mật khẩu/Recovery Key mới ĐÃ đổi thật ở Service, nhưng không còn
                        // ai hiển thị/acknowledge được nữa — zero ngay, không publish lên ViewModel mồ
                        // côi (fail-secure: ưu tiên không leak RAM hơn tiện dụng, PWD-032/TEST-001).
                        if (result.NewRecoveryKeyPlaintextUtf8 is not null)
                        {
                            CryptographicOperations.ZeroMemory(result.NewRecoveryKeyPlaintextUtf8);
                        }

                        break;
                    }

                    // Security audit Đợt 6 S4 (bug đã sửa) — nếu vì lý do nào đó vẫn còn buffer trước
                    // đó chưa acknowledge, zero ngay trước khi ghi đè, không để plaintext cũ trôi nổi.
                    if (_newRecoveryKeyPlaintextBuffer is not null)
                    {
                        CryptographicOperations.ZeroMemory(_newRecoveryKeyPlaintextBuffer);
                    }

                    _newRecoveryKeyPlaintextBuffer = result.NewRecoveryKeyPlaintextUtf8;
                    // Mục 6.1 bước 3 — UTF-8 decode ĐÚNG 1 LẦN từ bytes nhận được để hiển thị.
                    NewRecoveryKeyDisplay = _newRecoveryKeyPlaintextBuffer is not null ? Encoding.UTF8.GetString(_newRecoveryKeyPlaintextBuffer) : null;
                    break;
                case RecoveryOutcome.WrongRecoveryKey:
                    ErrorMessage = LocalizationService.Get("RecoveryWrongKey");
                    break;
                case RecoveryOutcome.LockedOut:
                    IsSubmitEnabled = false;
                    TimeSpan remaining = DateTimeOffset.FromUnixTimeMilliseconds(result.LockoutUntilUnixMs) - DateTimeOffset.UtcNow;
                    string countdown = remaining > TimeSpan.Zero ? remaining.ToString(@"mm\:ss") : "00:00";
                    ErrorMessage = LocalizationService.GetFormatted("RecoveryLockedOutFormat", countdown);
                    break;
                case RecoveryOutcome.NewPasswordTooLong:
                    ErrorMessage = LocalizationService.Get("OnboardingPasswordTooLong");
                    break;
            }
        }
        catch (UiIpcConnectionException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Bấm "Đã lưu" trên Recovery Key mới hiển thị — zero buffer plaintext ngay (cùng mẫu hình `S1` bước 3/`S4`, residual risk `string` bất biến đã ghi nhận ở đó).</summary>
    public void AcknowledgeNewRecoveryKeyDisplayed()
    {
        if (_newRecoveryKeyPlaintextBuffer is not null)
        {
            CryptographicOperations.ZeroMemory(_newRecoveryKeyPlaintextBuffer);
            _newRecoveryKeyPlaintextBuffer = null;
        }

        NewRecoveryKeyDisplay = null;
    }

    /// <summary>
    /// Security audit Đợt 6 S6 (bug đã sửa) — gọi TRƯỚC khi rời `S6` (<c>RecoveryPage.OnNavigatedFrom</c>,
    /// <c>App.OnWindowClosed</c>), kể cả khi <see cref="SubmitAsync"/> chưa hoàn tất; idempotent.
    /// </summary>
    public void MarkDiscarded() => _discarded = true;

    /// <summary>Safety net (App.xaml.cs OnWindowClosed, BUG B) — đóng app giữa chừng khi Recovery Key mới còn hiển thị vẫn phải zero buffer; idempotent.</summary>
    public void Dispose() => AcknowledgeNewRecoveryKeyDisplayed();
}
