using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParentalGuard.Service.Data;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// Đọc/ghi <c>auth.dat</c> (Architecture/04-data-architecture.md mục 4): file nhị phân
/// <c>{4 byte version LE}{DPAPI(JSON)}</c>, tách biệt vật lý khỏi <c>config.db</c> (`PWD-013`).
/// ACL/thư mục chứa đã do <see cref="ParentalGuard.Service.Security.AclProvisioner.EnsureProgramDataAcl"/>
/// áp (chỉ SYSTEM, kế thừa xuống file mới tạo trong thư mục) — không set ACL riêng ở đây.
/// </summary>
public static class AuthDataStore
{
    private const uint FileVersion = 1;

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Trả <c>null</c> nếu chưa từng setup (file không tồn tại). Ném <see cref="AuthDataCorruptException"/> nếu tồn tại nhưng không đọc được.</summary>
    public static AuthData? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            throw new AuthDataCorruptException($"auth.dat read failed: {ex.Message}", ex);
        }

        if (fileBytes.Length < 4)
        {
            throw new AuthDataCorruptException("auth.dat too small to contain version header.");
        }

        uint version = BitConverter.ToUInt32(fileBytes, 0);
        if (version != FileVersion)
        {
            throw new AuthDataCorruptException($"auth.dat unknown version {version}.");
        }

        byte[] plaintext;
        try
        {
            plaintext = DataProtectionHelper.Unprotect(fileBytes[4..]);
        }
        catch (CryptographicException ex)
        {
            throw new AuthDataCorruptException($"auth.dat DPAPI unprotect failed: {ex.Message}", ex);
        }

        try
        {
            AuthDataJson? json = JsonSerializer.Deserialize<AuthDataJson>(plaintext);
            return json is null ? throw new AuthDataCorruptException("auth.dat JSON is null.") : json.ToDomain();
        }
        catch (JsonException ex)
        {
            throw new AuthDataCorruptException($"auth.dat JSON invalid: {ex.Message}", ex);
        }
        catch (FormatException ex)
        {
            // Argon2idHasher.ParsePhc (gọi gián tiếp qua ToDomain không parse — chỉ giữ string; ném
            // ở đây phòng hờ nếu validate PHC sớm được thêm sau này, giữ đối xứng với JsonException).
            throw new AuthDataCorruptException($"auth.dat PHC hash invalid: {ex.Message}", ex);
        }
    }

    /// <summary>Ghi đè toàn bộ 1 lần (atomic qua tmp-file + <see cref="File.Move"/>) — mục 7.4/7.5: "ghi auth.dat 1 lần (atomic)".</summary>
    public static void Save(string path, AuthData data)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(AuthDataJson.FromDomain(data));
        byte[] ciphertext = DataProtectionHelper.Protect(plaintext);
        byte[] output = new byte[4 + ciphertext.Length];
        BitConverter.GetBytes(FileVersion).CopyTo(output, 0);
        ciphertext.CopyTo(output, 4);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmpPath = path + ".tmp";
        File.WriteAllBytes(tmpPath, output);
        File.Move(tmpPath, path, overwrite: true);
    }

    private sealed class AuthDataJson
    {
        [JsonPropertyName("password")]
        public PasswordEntryJson Password { get; set; } = new();

        [JsonPropertyName("recovery_key")]
        public RecoveryKeyEntryJson RecoveryKey { get; set; } = new();

        [JsonPropertyName("rate_limit")]
        public RateLimitJson RateLimit { get; set; } = new();

        // `security_questions`: luôn null ở Phase 1 (PWD-034 là Phase 2) — không deserialize vào type
        // riêng, chỉ giữ chỗ đúng shape khi ghi (FromDomain) để không phải đổi cấu trúc file sau này.
        [JsonPropertyName("security_questions")]
        public object? SecurityQuestions { get; set; }

        public static AuthDataJson FromDomain(AuthData data) => new()
        {
            Password = new PasswordEntryJson
            {
                Hash = data.Password.HashPhc,
                Argon2Params = new Argon2ParamsJson
                {
                    MemoryKb = data.Password.Argon2Params.MemoryKb,
                    Iterations = data.Password.Argon2Params.Iterations,
                    Parallelism = data.Password.Argon2Params.Parallelism,
                },
                UpdatedAtUnixMs = data.Password.UpdatedAtUnixMs,
            },
            RecoveryKey = new RecoveryKeyEntryJson
            {
                Hash = data.RecoveryKey.HashPhc,
                Argon2Params = new Argon2ParamsJson
                {
                    MemoryKb = data.RecoveryKey.Argon2Params.MemoryKb,
                    Iterations = data.RecoveryKey.Argon2Params.Iterations,
                    Parallelism = data.RecoveryKey.Argon2Params.Parallelism,
                },
                CreatedAtUnixMs = data.RecoveryKey.CreatedAtUnixMs,
                Used = data.RecoveryKey.Used,
            },
            RateLimit = new RateLimitJson
            {
                ConsecutiveFailures = data.RateLimit.ConsecutiveFailures,
                LastFailureAtUnixMs = data.RateLimit.LastFailureAtUnixMs,
                DelayUntilUnixMs = data.RateLimit.DelayUntilUnixMs,
            },
            SecurityQuestions = null,
        };

        public AuthData ToDomain() => new(
            new PasswordEntryData(Password.Hash, new Argon2ParamsData(Password.Argon2Params.MemoryKb, Password.Argon2Params.Iterations, Password.Argon2Params.Parallelism), Password.UpdatedAtUnixMs),
            new RecoveryKeyEntryData(RecoveryKey.Hash, new Argon2ParamsData(RecoveryKey.Argon2Params.MemoryKb, RecoveryKey.Argon2Params.Iterations, RecoveryKey.Argon2Params.Parallelism), RecoveryKey.CreatedAtUnixMs, RecoveryKey.Used),
            new RateLimitData(RateLimit.ConsecutiveFailures, RateLimit.LastFailureAtUnixMs, RateLimit.DelayUntilUnixMs));
    }

    private sealed class PasswordEntryJson
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("argon2_params")]
        public Argon2ParamsJson Argon2Params { get; set; } = new();

        [JsonPropertyName("updated_at_unix_ms")]
        public long UpdatedAtUnixMs { get; set; }
    }

    private sealed class RecoveryKeyEntryJson
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("argon2_params")]
        public Argon2ParamsJson Argon2Params { get; set; } = new();

        [JsonPropertyName("created_at_unix_ms")]
        public long CreatedAtUnixMs { get; set; }

        [JsonPropertyName("used")]
        public bool Used { get; set; }
    }

    private sealed class Argon2ParamsJson
    {
        [JsonPropertyName("memory_kb")]
        public int MemoryKb { get; set; }

        [JsonPropertyName("iterations")]
        public int Iterations { get; set; }

        [JsonPropertyName("parallelism")]
        public int Parallelism { get; set; }
    }

    private sealed class RateLimitJson
    {
        [JsonPropertyName("consecutive_failures")]
        public int ConsecutiveFailures { get; set; }

        [JsonPropertyName("last_failure_at_unix_ms")]
        public long? LastFailureAtUnixMs { get; set; }

        [JsonPropertyName("delay_until_unix_ms")]
        public long? DelayUntilUnixMs { get; set; }
    }
}
