using Microsoft.Data.Sqlite;
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
    public void UpdatePauseState_ThenReadSnapshot_RoundTripsNewValue()
    {
        ConfigDb.CreateFresh(_dbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);
        var paused = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1_000L, PauseExpiresAtUnixMs: 2_000L);

        using (ConfigDb db = ConfigDb.Open(_dbPath))
        {
            db.UpdatePauseState(paused);
        }

        using ConfigDb reopened = ConfigDb.Open(_dbPath);
        ConfigSnapshot snapshot = reopened.ReadSnapshot();

        Assert.True(snapshot.PauseState.IsPaused);
        Assert.Equal(1_000L, snapshot.PauseState.PauseStartedAtUnixMs);
        Assert.Equal(2_000L, snapshot.PauseState.PauseExpiresAtUnixMs);
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

    /// <summary>
    /// Regression cho FAIL cứng audit 2026-09-20: hàm ghi (`UpdatePauseState`) phải bọc
    /// <c>SqliteException</c> thành <see cref="ConfigLoadException"/> giống mọi hàm đọc — trước fix,
    /// exception nguyên bản lọt thẳng ra ngoài, phá vỡ fail-secure của <c>PauseCoordinator.TryPersist</c>.
    /// Dùng overload nội bộ <c>busyTimeoutSecondsForTest</c> để lấy lỗi lock nhanh, không chờ 30s mặc định.
    /// </summary>
    [Fact]
    public void UpdatePauseState_WhileDbLockedByAnotherConnection_ThrowsConfigLoadExceptionNotSqliteException()
    {
        ConfigDb.CreateFresh(_dbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), new byte[32], AuditCheckpoint.CreateGenesis(), lastFallbackEventUnixMs: null);

        using var locker = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        locker.Open();
        using (SqliteCommand beginImmediate = locker.CreateCommand())
        {
            beginImmediate.CommandText = "BEGIN IMMEDIATE TRANSACTION;";
            beginImmediate.ExecuteNonQuery();
        }

        try
        {
            using ConfigDb db = ConfigDb.Open(_dbPath, busyTimeoutSecondsForTest: 1);
            var newState = new PauseStateData(IsPaused: true, PauseStartedAtUnixMs: 1, PauseExpiresAtUnixMs: 2);

            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(() => db.UpdatePauseState(newState));
            Assert.Contains("pause_state write failed", ex.Reason);
        }
        finally
        {
            using SqliteCommand rollback = locker.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }
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
