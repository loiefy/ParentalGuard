using System.Diagnostics.Eventing.Reader;

namespace ParentalGuard.Service.Security;

public sealed class VisionNetworkBlockedEventArgs(string detailXml) : EventArgs
{
    public string DetailXml { get; } = detailXml;
}

/// <summary>
/// Phát hiện <c>Vision</c> cố network (dù bị chặn bởi <see cref="WfpVisionBlocker"/>) qua
/// Windows Security Event 5157 (Architecture/06-security-architecture.md mục 3.3, ADR-33) —
/// chỉ kích hoạt nếu có bug/dependency compromise (defense in depth, SEC-004).
/// </summary>
public sealed class VisionNetworkWatcher(string visionExecutablePath) : IDisposable
{
    private const int _blockedConnectionEventId = 5157;

    private EventLogWatcher? _watcher;

    public event EventHandler<VisionNetworkBlockedEventArgs>? NetworkBlocked;

    public void Start()
    {
        EnableFilteringPlatformConnectionAuditing();

        var query = new EventLogQuery("Security", PathType.LogName, $"*[System[EventID={_blockedConnectionEventId}]]");
        _watcher = new EventLogWatcher(query);
        _watcher.EventRecordWritten += OnEventRecordWritten;
        _watcher.Enabled = true;
    }

    private static void EnableFilteringPlatformConnectionAuditing()
    {
        var policy = new AuditPolicyInformation
        {
            AuditSubCategoryGuid = AuditPolicyInterop.FilteringPlatformConnectionSubCategory,
            AuditingInformation = AuditPolicyInterop.PolicyAuditEventFailure,
        };

        // Không throw nếu fail — đây chỉ là lớp phát hiện bổ sung (defense in depth, SEC-004),
        // WFP block filter (WfpVisionBlocker) vẫn chặn network dù bật audit thất bại vì lý do nào đó.
        AuditPolicyInterop.AuditSetSystemPolicy([policy], 1);
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        using EventRecord? record = e.EventRecord;
        if (record is null)
        {
            return;
        }

        string xml;
        try
        {
            xml = record.ToXml();
        }
        catch (EventLogException)
        {
            return;
        }

        if (!xml.Contains(visionExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return; // chỉ quan tâm sự kiện đúng ParentalGuard.Vision.exe (mục 3.3 bước 2).
        }

        NetworkBlocked?.Invoke(this, new VisionNetworkBlockedEventArgs(xml));
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.Enabled = false;
            _watcher.EventRecordWritten -= OnEventRecordWritten;
            _watcher.Dispose();
        }
    }
}
