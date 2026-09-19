using Microsoft.Extensions.Logging;
using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Configuration;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Ipc;

/// <summary>
/// Lưu bền vững vị trí icon sau kéo-thả (`FE-020a`, Architecture/07-overlay-architecture.md mục
/// 4.1.4, ADR-67): nhận <c>IconPositionUpdate</c> (Overlay → Service), ghi bảng <c>icon_positions</c>
/// (Architecture/04 mục 3.6a — plaintext, không DPAPI), đẩy lại toàn bộ layout qua
/// <c>IconLayoutSync</c> mỗi khi Overlay (re)connect. Best-effort: lỗi I/O không được làm crash
/// `Service` hay chặn giám sát (đây là dữ liệu tiện ích, không phải cấu hình an toàn — ADR-70).
/// </summary>
public sealed class IconPositionCoordinator(ILogger logger)
{
    public Task HandleIconPositionUpdateAsync(IpcPayload message, CancellationToken cancellationToken)
    {
        IconPositionUpdate update = message.IconPositionUpdate;
        try
        {
            using ConfigDb db = ConfigDb.Open(InstallPaths.ConfigDbPath);
            db.UpsertIconPosition(new IconPositionData(update.DeviceName, update.X, update.Y));
        }
        catch (Exception ex) when (ex is ConfigLoadException or IOException)
        {
            logger.LogWarning(ex, "Failed to persist icon position for {DeviceName}.", update.DeviceName);
        }

        return Task.CompletedTask;
    }

    /// <summary>Dùng làm 1 trong các <c>initialPushBuilders</c> khi Overlay (re)connect (Architecture/03 mục 4.3).</summary>
    public void ConfigureInitialPush(IpcPayload payload)
    {
        var sync = new IconLayoutSync();
        try
        {
            using ConfigDb db = ConfigDb.Open(InstallPaths.ConfigDbPath);
            foreach (IconPositionData position in db.ReadAllIconPositions())
            {
                sync.Positions.Add(new IconPositionUpdate { DeviceName = position.DeviceName, X = position.X, Y = position.Y });
            }
        }
        catch (Exception ex) when (ex is ConfigLoadException or IOException)
        {
            logger.LogWarning(ex, "Failed to read icon_positions — pushing empty layout sync.");
        }

        payload.IconLayoutSync = sync;
    }
}
