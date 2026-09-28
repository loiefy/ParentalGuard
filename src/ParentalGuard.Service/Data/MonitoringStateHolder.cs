namespace ParentalGuard.Service.Data;

/// <summary>
/// Giữ tham chiếu <see cref="MonitoringStateData"/> hiện hành, thread-safe qua <c>volatile</c> — cùng
/// mẫu hình <c>VisionRuntimeConfigHolder</c> (`ParentalGuard.Vision`). Cần thiết từ Đợt 7 (gap fix
/// `ConfigUpdateRequest`/`MarkFalsePositiveRequest`/`RemoveWhitelistEntryRequest`, `10-ui-architecture.md`
/// mục 6.4): trước đó <c>MonitoringStateData</c> chỉ đọc 1 lần lúc <c>Worker.ExecuteAsync</c> khởi
/// động (không có đường ghi lại lúc runtime) — nay cần 1 nguồn sự thật RAM cập nhật được, để các
/// closure đã đọc <c>() =&gt; monitoringStateHolder.Current.X</c> (risk_threshold, performance_mode...)
/// thấy giá trị mới ngay sau khi <c>ConfigCoordinator</c> ghi thành công vào <c>config.db</c>.
/// </summary>
public sealed class MonitoringStateHolder(MonitoringStateData initial)
{
    private volatile MonitoringStateData _current = initial;

    public MonitoringStateData Current => _current;

    public void Update(MonitoringStateData state) => _current = state;
}
