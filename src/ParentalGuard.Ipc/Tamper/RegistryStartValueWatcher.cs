namespace ParentalGuard.Ipc.Tamper;

/// <summary>
/// Giám sát registry value <c>Start</c> của 1 service key qua <c>RegNotifyChangeKeyValue</c> (không
/// polling — Architecture/09-anti-tamper-architecture.md mục 4.1/4.2, ADR-90): tự phục hồi ngay khi
/// lệch khỏi <see cref="ExpectedStartValue"/>, gọi <paramref name="onTamperDetectedAndRestored"/> mỗi
/// lần phục hồi. 1 instance = 1 key — <c>Service</c>/<c>Watchdog</c> mỗi bên tạo 2 instance (key của
/// chính mình + key của bên kia, mục 4.1 "chấp nhận trùng lặp phát hiện").
/// </summary>
public sealed class RegistryStartValueWatcher : IDisposable
{
    /// <summary>SERVICE_AUTO_START (mục 4.2).</summary>
    public const uint ExpectedStartValue = 0x00000002;

    private const string _startValueName = "Start";

    private readonly IntPtr _rootHive;
    private readonly string _keyPath;

    /// <summary>Tham số: (keyPath, oldValue, newValue) — gọi ngay sau khi đã tự phục hồi <c>Start</c> value.</summary>
    private readonly Action<string, uint, uint> _onTamperDetectedAndRestored;
    private readonly Action<Exception>? _onError;
    private readonly Thread _thread;
    private volatile bool _stopping;
    private IntPtr _hKey = IntPtr.Zero;

    public RegistryStartValueWatcher(string keyPath, Action<string, uint, uint> onTamperDetectedAndRestored, Action<Exception>? onError = null)
        : this(RegistryTamperInterop.HKeyLocalMachine, keyPath, onTamperDetectedAndRestored, onError)
    {
    }

    /// <summary>Overload chỉ dùng cho unit test (root hive tuỳ ý, vd HKCU — không cần quyền SYSTEM).</summary>
    internal RegistryStartValueWatcher(IntPtr rootHive, string keyPath, Action<string, uint, uint> onTamperDetectedAndRestored, Action<Exception>? onError = null)
    {
        _rootHive = rootHive;
        _keyPath = keyPath;
        _onTamperDetectedAndRestored = onTamperDetectedAndRestored;
        _onError = onError;
        _thread = new Thread(Run) { IsBackground = true, Name = $"RegWatch:{keyPath}" };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stopping = true;
        // Đóng handle đang chờ RegNotifyChangeKeyValue — cách chính thức để huỷ 1 lời gọi đang pending
        // (đọc trong Run() và Dispose() có thể race benign: worst case đóng 2 lần, RegCloseKey idempotent-safe ở đây vì chỉ gọi từ đúng luồng sở hữu handle).
        IntPtr handle = Interlocked.Exchange(ref _hKey, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            RegistryTamperInterop.RegCloseKey(handle);
        }

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        try
        {
            int openStatus = RegistryTamperInterop.RegOpenKeyEx(
                _rootHive,
                _keyPath,
                0,
                RegistryTamperInterop.KeyNotify | RegistryTamperInterop.KeyQueryValue | RegistryTamperInterop.KeySetValue,
                out IntPtr hKey);
            if (openStatus != RegistryTamperInterop.ErrorSuccess)
            {
                throw new InvalidOperationException($"RegOpenKeyEx('{_keyPath}') failed: {openStatus}");
            }

            _hKey = hKey;

            while (!_stopping)
            {
                int notifyStatus = RegistryTamperInterop.RegNotifyChangeKeyValue(
                    hKey, watchSubtree: false, RegistryTamperInterop.RegNotifyChangeLastSet, IntPtr.Zero, asynchronous: false);
                if (_stopping)
                {
                    break;
                }

                if (notifyStatus != RegistryTamperInterop.ErrorSuccess)
                {
                    throw new InvalidOperationException($"RegNotifyChangeKeyValue('{_keyPath}') failed: {notifyStatus}");
                }

                CheckAndSelfHeal(hKey);
            }
        }
        catch (Exception ex) when (!_stopping)
        {
            _onError?.Invoke(ex);
        }
        catch (Exception)
        {
            // Đang Dispose() — lỗi do handle vừa bị đóng chủ động, không phải bất thường cần báo cáo.
        }
    }

    private void CheckAndSelfHeal(IntPtr hKey)
    {
        int dataSize = sizeof(uint);
        int queryStatus = RegistryTamperInterop.RegQueryValueEx(hKey, _startValueName, IntPtr.Zero, out uint type, out uint currentValue, ref dataSize);
        if (queryStatus != RegistryTamperInterop.ErrorSuccess || type != RegistryTamperInterop.RegDwordType)
        {
            return; // value bị xoá hẳn hoặc đổi kiểu — ngoài phạm vi "self-heal giá trị lệch" của mục 4.2, không đoán ý định.
        }

        if (currentValue == ExpectedStartValue)
        {
            return;
        }

        uint restoreValue = ExpectedStartValue;
        RegistryTamperInterop.RegSetValueEx(hKey, _startValueName, 0, RegistryTamperInterop.RegDwordType, in restoreValue, sizeof(uint));
        _onTamperDetectedAndRestored(_keyPath, currentValue, ExpectedStartValue);
    }
}
