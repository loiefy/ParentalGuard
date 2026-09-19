using Microsoft.Data.Sqlite;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Tests;

public class ConfigDbIconPositionsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pg-config-icons-{Guid.NewGuid():N}.db");

    [Fact]
    public void CreateFresh_IncludesEmptyIconPositionsTable()
    {
        ConfigDb.CreateFresh(_dbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), RandomKey(), AuditCheckpoint.CreateGenesis(), null);

        using ConfigDb db = ConfigDb.Open(_dbPath);
        Assert.Empty(db.ReadAllIconPositions());
    }

    [Fact]
    public void UpsertIconPosition_ThenReadAll_RoundTrips()
    {
        ConfigDb.CreateFresh(_dbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), RandomKey(), AuditCheckpoint.CreateGenesis(), null);

        using ConfigDb db = ConfigDb.Open(_dbPath);
        db.UpsertIconPosition(new IconPositionData(@"\\.\DISPLAY1", 100, 200));
        db.UpsertIconPosition(new IconPositionData(@"\\.\DISPLAY2", 300, 400));

        IReadOnlyList<IconPositionData> all = db.ReadAllIconPositions();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.DeviceName == @"\\.\DISPLAY1" && p.X == 100 && p.Y == 200);
    }

    [Fact]
    public void UpsertIconPosition_SameDeviceTwice_OverwritesPreviousValue()
    {
        ConfigDb.CreateFresh(_dbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), RandomKey(), AuditCheckpoint.CreateGenesis(), null);

        using ConfigDb db = ConfigDb.Open(_dbPath);
        db.UpsertIconPosition(new IconPositionData(@"\\.\DISPLAY1", 1, 1));
        db.UpsertIconPosition(new IconPositionData(@"\\.\DISPLAY1", 42, 84));

        IReadOnlyList<IconPositionData> all = db.ReadAllIconPositions();
        IconPositionData single = Assert.Single(all);
        Assert.Equal(42, single.X);
        Assert.Equal(84, single.Y);
    }

    /// <summary>Architecture/04-data-architecture.md mục 3.6a/3.7: config.db tạo bởi code Đợt 0/1 (schema_version=1, chưa có icon_positions) phải tự migrate khi mở bằng code Đợt 2.</summary>
    [Fact]
    public void ReadSnapshot_OnV1Database_MigratesToV2AndIconPositionsBecomesUsable()
    {
        byte[] hmacKey = RandomKey();
        ConfigDb.CreateFresh(_dbPath, MonitoringStateData.CreateFirstRunDefault(), PauseStateData.CreateDefault(), hmacKey, AuditCheckpoint.CreateGenesis(), null);
        DowngradeToLegacyV1Shape();

        using ConfigDb db = ConfigDb.Open(_dbPath);
        ConfigSnapshot snapshot = db.ReadSnapshot();

        Assert.Equal(2, snapshot.SchemaVersion);
        Assert.Equal(hmacKey, snapshot.IpcHmacKey); // domain-state hiện có không bị đụng tới bởi migration
        Assert.Empty(db.ReadAllIconPositions());

        db.UpsertIconPosition(new IconPositionData(@"\\.\DISPLAY1", 5, 6));
        Assert.Single(db.ReadAllIconPositions());
    }

    /// <summary>Giả lập 1 file config.db tạo bởi code Đợt 0/1 (trước khi bảng <c>icon_positions</c> tồn tại) từ 1 file v2 hợp lệ — chỉ đụng phần cấu trúc plaintext, không đụng domain-state đã mã hoá.</summary>
    private void DowngradeToLegacyV1Shape()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DROP TABLE icon_positions; UPDATE schema_meta SET schema_version = 1 WHERE id = 1;";
        command.ExecuteNonQuery();
    }

    private static byte[] RandomKey()
    {
        byte[] key = new byte[32];
        Random.Shared.NextBytes(key);
        return key;
    }

    public void Dispose()
    {
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_dbPath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }
}
