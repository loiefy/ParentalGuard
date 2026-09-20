using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ParentalGuard.Service.Audit;
using ParentalGuard.Service.Configuration;

namespace ParentalGuard.Service.Data;

/// <summary>Snapshot toàn bộ dữ liệu domain-state đọc được từ <c>config.db</c> (mục 3).</summary>
public sealed record ConfigSnapshot(
    int SchemaVersion,
    long? LastFallbackEventUnixMs,
    MonitoringStateData MonitoringState,
    PauseStateData PauseState,
    byte[] IpcHmacKey);

/// <summary>
/// Truy cập <c>config.db</c> (SQLite) — Architecture/04-data-architecture.md mục 3.
/// Mỗi lỗi đọc/giải mã ném <see cref="ConfigLoadException"/> để caller (FailSecureConfigLoader)
/// điều hướng sang nhánh fail-secure (mục 6.1) thay vì để exception nguyên bản lan ra ngoài.
/// </summary>
public sealed class ConfigDb : IDisposable
{
    /// <summary>v2 (Đợt 2): thêm bảng <c>icon_positions</c> (Architecture/04 mục 3.6a/3.7, ADR-70).</summary>
    public const int CurrentSchemaVersion = 2;
    private const string _ipcKeyChannel = "vision_overlay";

    private readonly SqliteConnection _connection;

    private ConfigDb(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static ConfigDb Open(string path) => Open(path, busyTimeoutSecondsForTest: null);

    /// <summary>
    /// Overload nội bộ chỉ dùng để test fail-secure write-path dưới lock contention (regression test
    /// cho bug ConfigDb write không bắt <see cref="SqliteException"/>) mà không phải chờ đủ busy
    /// timeout mặc định (~30s — "Default Timeout" của Microsoft.Data.Sqlite) — KHÔNG đổi hành vi
    /// production vì caller công khai duy nhất (<see cref="Open(string)"/>) luôn truyền null. Dùng
    /// connection-string keyword thay vì PRAGMA nối chuỗi để tránh CA2100 (SQLite PRAGMA vốn không hỗ
    /// trợ bind parameter cho vế giá trị).
    /// </summary>
    internal static ConfigDb Open(string path, int? busyTimeoutSecondsForTest)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        if (busyTimeoutSecondsForTest is int busyTimeoutSeconds)
        {
            connectionStringBuilder.DefaultTimeout = busyTimeoutSeconds;
        }

        var connection = new SqliteConnection(connectionStringBuilder.ToString());
        try
        {
            connection.Open();
            using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;"; // ADR-23
            pragma.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            // Không để connection rò rỉ (giữ file lock) khi file tồn tại nhưng không phải SQLite
            // hợp lệ — phát hiện ngay ở bước PRAGMA đầu tiên thay vì đợi tới truy vấn đầu tiên (mục 6.1).
            connection.Dispose();
            throw new ConfigLoadException($"config.db not a valid SQLite database: {ex.Message}");
        }

        return new ConfigDb(connection);
    }

    /// <summary>Đọc toàn bộ domain-state hiện có — ném <see cref="ConfigLoadException"/> nếu bất kỳ bước nào lỗi (mục 6.1).</summary>
    public ConfigSnapshot ReadSnapshot()
    {
        (int schemaVersion, long? lastFallbackEventUnixMs) = ReadSchemaMeta();
        if (schemaVersion > CurrentSchemaVersion)
        {
            // Downgrade (cài lại bản cũ hơn) — không đoán cách đọc, coi như không đọc được (mục 3.7).
            throw new ConfigLoadException($"schema_version {schemaVersion} newer than supported {CurrentSchemaVersion}.");
        }

        if (schemaVersion < CurrentSchemaVersion)
        {
            // Migration tuần tự v1→v2 (mục 3.7) — chỉ thao tác plaintext (thêm bảng), không đổi
            // ngữ nghĩa field cũ. schemaVersion hiện tại chỉ có thể là 1 (chưa có schema_version nào khác).
            MigrateFromV1ToV2();
            schemaVersion = CurrentSchemaVersion;
        }

        MonitoringStateData monitoringState = ReadMonitoringState();
        PauseStateData pauseState = ReadPauseState();
        byte[] ipcHmacKey = ReadIpcKey();
        return new ConfigSnapshot(schemaVersion, lastFallbackEventUnixMs, monitoringState, pauseState, ipcHmacKey);
    }

    /// <summary>
    /// Xoá <c>config.db</c> hiện có (nếu có) và tạo mới hoàn toàn (Architecture/04 mục 6.2 bước 5) —
    /// dùng cả cho lần cài đặt đầu tiên lẫn nhánh fail-secure.
    /// </summary>
    public static void CreateFresh(
        string path,
        MonitoringStateData monitoringState,
        PauseStateData pauseState,
        byte[] ipcHmacKey,
        AuditCheckpoint auditCheckpoint,
        long? lastFallbackEventUnixMs)
    {
        DeleteFileIfExists(path);
        DeleteFileIfExists(path + "-wal");
        DeleteFileIfExists(path + "-shm");

        using ConfigDb db = Open(path);
        db.CreateSchema();
        db.InsertSchemaMeta(lastFallbackEventUnixMs);
        db.InsertMonitoringState(monitoringState with { UsingFallbackConfig = false });
        db.InsertPauseState(pauseState);
        db.InsertIpcKey(ipcHmacKey);
        db.InsertAuditMeta(auditCheckpoint);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void CreateSchema()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE schema_meta (
              id                          INTEGER PRIMARY KEY CHECK (id = 1),
              schema_version               INTEGER NOT NULL,
              db_created_at_unix_ms        INTEGER NOT NULL,
              app_version_at_creation      TEXT NOT NULL,
              last_fallback_event_unix_ms  INTEGER NULL
            );

            CREATE TABLE monitoring_state (
              id                 INTEGER PRIMARY KEY CHECK (id = 1),
              row_schema_version INTEGER NOT NULL,
              data_encrypted     BLOB NOT NULL,
              updated_at_unix_ms INTEGER NOT NULL
            );

            CREATE TABLE pause_state (
              id                 INTEGER PRIMARY KEY CHECK (id = 1),
              row_schema_version INTEGER NOT NULL,
              data_encrypted     BLOB NOT NULL,
              updated_at_unix_ms INTEGER NOT NULL
            );

            CREATE TABLE ipc_keys (
              channel            TEXT PRIMARY KEY,
              row_schema_version INTEGER NOT NULL,
              key_encrypted      BLOB NOT NULL,
              created_at_unix_ms INTEGER NOT NULL
            );

            CREATE TABLE audit_meta (
              id                           INTEGER PRIMARY KEY CHECK (id = 1),
              chain_id                     TEXT NOT NULL,
              last_seq                     INTEGER NOT NULL,
              last_hash                    TEXT NOT NULL,
              last_full_verify_at_unix_ms  INTEGER NULL
            );

            CREATE TABLE icon_positions (
              device_name         TEXT PRIMARY KEY,
              x                   INTEGER NOT NULL,
              y                   INTEGER NOT NULL,
              updated_at_unix_ms  INTEGER NOT NULL
            );
            """;
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"schema creation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// v1→v2 (mục 3.6a/3.7, ADR-70): thêm bảng <c>icon_positions</c> + bump
    /// <c>schema_meta.schema_version</c>. Idempotent (<c>IF NOT EXISTS</c>) — an toàn nếu Service
    /// crash giữa chừng lần migrate trước.
    /// </summary>
    private void MigrateFromV1ToV2()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS icon_positions (
              device_name         TEXT PRIMARY KEY,
              x                   INTEGER NOT NULL,
              y                   INTEGER NOT NULL,
              updated_at_unix_ms  INTEGER NOT NULL
            );
            UPDATE schema_meta SET schema_version = 2 WHERE id = 1;
            """;
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"schema migration v1->v2 failed: {ex.Message}");
        }
    }

    /// <summary>`FE-020a`: gọi mỗi khi Overlay báo kết quả kéo-thả (<c>IconPositionUpdate</c>) — plaintext, không DPAPI (ADR-70).</summary>
    public void UpsertIconPosition(IconPositionData position)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO icon_positions (device_name, x, y, updated_at_unix_ms)
            VALUES ($deviceName, $x, $y, $updatedAt)
            ON CONFLICT(device_name) DO UPDATE SET x = excluded.x, y = excluded.y, updated_at_unix_ms = excluded.updated_at_unix_ms;
            """;
        command.Parameters.AddWithValue("$deviceName", position.DeviceName);
        command.Parameters.AddWithValue("$x", position.X);
        command.Parameters.AddWithValue("$y", position.Y);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"icon_positions write failed: {ex.Message}");
        }
    }

    /// <summary>`IconLayoutSync` (Architecture/07 mục 4.1.4): đẩy lại toàn bộ vị trí đã lưu lúc Overlay connect.</summary>
    public IReadOnlyList<IconPositionData> ReadAllIconPositions()
    {
        try
        {
            var results = new List<IconPositionData>();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT device_name, x, y FROM icon_positions;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new IconPositionData(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
            }

            return results;
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"icon_positions read failed: {ex.Message}");
        }
    }

    private void InsertSchemaMeta(long? lastFallbackEventUnixMs)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schema_meta (id, schema_version, db_created_at_unix_ms, app_version_at_creation, last_fallback_event_unix_ms)
            VALUES (1, $schemaVersion, $createdAt, $appVersion, $lastFallback);
            """;
        command.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$appVersion", ServiceVersion.Current);
        command.Parameters.AddWithValue("$lastFallback", (object?)lastFallbackEventUnixMs ?? DBNull.Value);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"schema_meta write failed: {ex.Message}");
        }
    }

    private (int SchemaVersion, long? LastFallbackEventUnixMs) ReadSchemaMeta()
    {
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT schema_version, last_fallback_event_unix_ms FROM schema_meta WHERE id = 1;";
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new ConfigLoadException("schema_meta row missing.");
            }

            int schemaVersion = reader.GetInt32(0);
            long? lastFallback = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            return (schemaVersion, lastFallback);
        }
        catch (SqliteException ex)
        {
            // SQLITE_CORRUPT/SQLITE_NOTADB và tương tự chỉ lộ ra khi thực sự truy vấn (mục 6.1).
            throw new ConfigLoadException($"config.db not a valid SQLite database: {ex.Message}");
        }
    }

    private void InsertMonitoringState(MonitoringStateData state)
    {
        var json = new MonitoringStateJson
        {
            MonitoringEnabled = state.MonitoringEnabled,
            RiskThreshold = state.RiskThreshold,
            CaptureIntervalBaselineMs = state.CaptureIntervalBaselineMs,
            ExcludeProcessNames = [.. state.ExcludeProcessNames],
            UsingFallbackConfig = state.UsingFallbackConfig,
        };
        byte[] encrypted = DataProtectionHelper.Protect(JsonSerializer.SerializeToUtf8Bytes(json));

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO monitoring_state (id, row_schema_version, data_encrypted, updated_at_unix_ms)
            VALUES (1, $rowSchemaVersion, $data, $updatedAt);
            """;
        command.Parameters.AddWithValue("$rowSchemaVersion", 1);
        command.Parameters.AddWithValue("$data", encrypted);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"monitoring_state write failed: {ex.Message}");
        }
    }

    private MonitoringStateData ReadMonitoringState()
    {
        byte[] encrypted;
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT data_encrypted FROM monitoring_state WHERE id = 1;";
            encrypted = command.ExecuteScalar() as byte[] ?? throw new ConfigLoadException("monitoring_state row missing.");
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"monitoring_state read failed: {ex.Message}");
        }

        MonitoringStateJson json = DecryptAndParse<MonitoringStateJson>(encrypted, "monitoring_state");
        return new MonitoringStateData(
            json.MonitoringEnabled,
            json.RiskThreshold,
            json.CaptureIntervalBaselineMs,
            json.ExcludeProcessNames,
            json.UsingFallbackConfig);
    }

    private void InsertPauseState(PauseStateData state)
    {
        var json = new PauseStateJson
        {
            IsPaused = state.IsPaused,
            PauseStartedAtUnixMs = state.PauseStartedAtUnixMs,
            PauseExpiresAtUnixMs = state.PauseExpiresAtUnixMs,
        };
        byte[] encrypted = DataProtectionHelper.Protect(JsonSerializer.SerializeToUtf8Bytes(json));

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pause_state (id, row_schema_version, data_encrypted, updated_at_unix_ms)
            VALUES (1, $rowSchemaVersion, $data, $updatedAt);
            """;
        command.Parameters.AddWithValue("$rowSchemaVersion", 1);
        command.Parameters.AddWithValue("$data", encrypted);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"pause_state write failed: {ex.Message}");
        }
    }

    /// <summary>`PAUSE-030` (Architecture/02 mục 3a) — ghi đè dòng <c>pause_state</c> hiện có khi Pause kích hoạt/resume (khác <see cref="InsertPauseState"/>, chỉ dùng lúc <see cref="CreateFresh"/>).</summary>
    public void UpdatePauseState(PauseStateData state)
    {
        var json = new PauseStateJson
        {
            IsPaused = state.IsPaused,
            PauseStartedAtUnixMs = state.PauseStartedAtUnixMs,
            PauseExpiresAtUnixMs = state.PauseExpiresAtUnixMs,
        };
        byte[] encrypted = DataProtectionHelper.Protect(JsonSerializer.SerializeToUtf8Bytes(json));

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE pause_state SET data_encrypted = $data, updated_at_unix_ms = $updatedAt WHERE id = 1;
            """;
        command.Parameters.AddWithValue("$data", encrypted);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"pause_state write failed: {ex.Message}");
        }
    }

    private PauseStateData ReadPauseState()
    {
        byte[] encrypted;
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT data_encrypted FROM pause_state WHERE id = 1;";
            encrypted = command.ExecuteScalar() as byte[] ?? throw new ConfigLoadException("pause_state row missing.");
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"pause_state read failed: {ex.Message}");
        }

        PauseStateJson json = DecryptAndParse<PauseStateJson>(encrypted, "pause_state");
        return new PauseStateData(json.IsPaused, json.PauseStartedAtUnixMs, json.PauseExpiresAtUnixMs);
    }

    private void InsertIpcKey(byte[] hmacKey)
    {
        byte[] encrypted = DataProtectionHelper.Protect(hmacKey);

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ipc_keys (channel, row_schema_version, key_encrypted, created_at_unix_ms)
            VALUES ($channel, $rowSchemaVersion, $key, $createdAt);
            """;
        command.Parameters.AddWithValue("$channel", _ipcKeyChannel);
        command.Parameters.AddWithValue("$rowSchemaVersion", 1);
        command.Parameters.AddWithValue("$key", encrypted);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"ipc_keys write failed: {ex.Message}");
        }
    }

    private byte[] ReadIpcKey()
    {
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT key_encrypted FROM ipc_keys WHERE channel = $channel;";
            command.Parameters.AddWithValue("$channel", _ipcKeyChannel);
            object? result = command.ExecuteScalar();
            if (result is not byte[] encrypted)
            {
                throw new ConfigLoadException("ipc_keys row missing for channel 'vision_overlay'.");
            }

            return DataProtectionHelper.Unprotect(encrypted);
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"ipc_keys read failed: {ex.Message}");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new ConfigLoadException($"ipc_keys DPAPI unprotect failed: {ex.Message}");
        }
    }

    private void InsertAuditMeta(AuditCheckpoint checkpoint)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_meta (id, chain_id, last_seq, last_hash, last_full_verify_at_unix_ms)
            VALUES (1, $chainId, $lastSeq, $lastHash, NULL);
            """;
        command.Parameters.AddWithValue("$chainId", checkpoint.ChainId);
        command.Parameters.AddWithValue("$lastSeq", checkpoint.LastSeq);
        command.Parameters.AddWithValue("$lastHash", checkpoint.LastHash);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new ConfigLoadException($"audit_meta write failed: {ex.Message}");
        }
    }

    private static TJson DecryptAndParse<TJson>(byte[] encrypted, string tableName)
    {
        byte[] plaintext;
        try
        {
            plaintext = DataProtectionHelper.Unprotect(encrypted);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new ConfigLoadException($"{tableName} DPAPI unprotect failed: {ex.Message}");
        }

        try
        {
            return JsonSerializer.Deserialize<TJson>(plaintext) ?? throw new ConfigLoadException($"{tableName} JSON is null.");
        }
        catch (JsonException ex)
        {
            throw new ConfigLoadException($"{tableName} JSON invalid: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private sealed class MonitoringStateJson
    {
        [JsonPropertyName("monitoring_enabled")]
        public bool MonitoringEnabled { get; set; }

        [JsonPropertyName("risk_threshold")]
        public float RiskThreshold { get; set; }

        [JsonPropertyName("capture_interval_baseline_ms")]
        public uint CaptureIntervalBaselineMs { get; set; }

        [JsonPropertyName("exclude_process_names")]
        public List<string> ExcludeProcessNames { get; set; } = [];

        [JsonPropertyName("using_fallback_config")]
        public bool UsingFallbackConfig { get; set; }
    }

    private sealed class PauseStateJson
    {
        [JsonPropertyName("is_paused")]
        public bool IsPaused { get; set; }

        [JsonPropertyName("pause_started_at_unix_ms")]
        public long? PauseStartedAtUnixMs { get; set; }

        [JsonPropertyName("pause_expires_at_unix_ms")]
        public long? PauseExpiresAtUnixMs { get; set; }
    }
}
