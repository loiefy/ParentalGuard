using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

public class ConfigDbTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pg-config-{Guid.NewGuid():N}.db");

    [Fact]
    public void CreateFresh_ThenReadSnapshot_RoundTripsAllDomainState()
    {
        MonitoringStateData monitoring = MonitoringStateData.CreateFirstRunDefault();
        PauseStateData pause = PauseStateData.CreateDefault();
        byte[] hmacKey = new byte[32];
        Random.Shared.NextBytes(hmacKey);
        AuditCheckpoint checkpoint = AuditCheckpoint.CreateGenesis();

        ConfigDb.CreateFresh(_dbPath, monitoring, pause, hmacKey, checkpoint, lastFallbackEventUnixMs: null);

        using ConfigDb db = ConfigDb.Open(_dbPath);
        ConfigSnapshot snapshot = db.ReadSnapshot();

        Assert.Equal(ConfigDb.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Null(snapshot.LastFallbackEventUnixMs);
        Assert.True(snapshot.MonitoringState.MonitoringEnabled);
        Assert.False(snapshot.MonitoringState.UsingFallbackConfig);
        Assert.Equal(monitoring.ExcludeProcessNames.Count, snapshot.MonitoringState.ExcludeProcessNames.Count);
        Assert.False(snapshot.PauseState.IsPaused);
        Assert.Equal(hmacKey, snapshot.IpcHmacKey);
    }

    [Fact]
    public void Open_OnCorruptFile_ThrowsConfigLoadException()
    {
        File.WriteAllBytes(_dbPath, [0x01, 0x02, 0x03, 0x04]); // không phải file SQLite hợp lệ

        Assert.Throws<ConfigLoadException>(() => ConfigDb.Open(_dbPath));
    }

    [Fact]
    public void CreateFresh_FailSecureDefault_HasEmptyExcludeList()
    {
        MonitoringStateData monitoring = MonitoringStateData.CreateFailSecureDefault();
        PauseStateData pause = PauseStateData.CreateDefault();
        byte[] hmacKey = new byte[32];
        Random.Shared.NextBytes(hmacKey);

        ConfigDb.CreateFresh(_dbPath, monitoring, pause, hmacKey, AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: 1234);

        using ConfigDb db = ConfigDb.Open(_dbPath);
        ConfigSnapshot snapshot = db.ReadSnapshot();

        Assert.Empty(snapshot.MonitoringState.ExcludeProcessNames);
        Assert.Equal(1234, snapshot.LastFallbackEventUnixMs);
    }

    public void Dispose()
    {
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            string path = _dbPath + suffix;
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup — không để lỗi dọn dẹp file tạm che khuất kết quả test thật.
            }
        }
    }
}
