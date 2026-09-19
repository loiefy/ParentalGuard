using System.Globalization;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace ParentalGuard.Service.Auth;

/// <summary>
/// Argon2id hash/verify qua <c>Konscious.Security.Cryptography.Argon2</c> (Architecture/08 mục 4,
/// ADR-71) — PHC string tự dựng theo chuẩn cộng đồng (mục 4.3):
/// <c>$argon2id$v=19$m=&lt;m&gt;,t=&lt;t&gt;,p=&lt;p&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>.
/// </summary>
public static class Argon2idHasher
{
    private const int SaltSizeBytes = 16; // PWD-012, mục 4.3
    private const int HashSizeBytes = 32; // mục 4.3
    private const int ArgonVersion = 19; // Argon2 v1.3 — duy nhất version Konscious hỗ trợ

    /// <summary>Hash <paramref name="credentialUtf8"/> với salt CSPRNG mới — KHÔNG zero buffer đầu vào (caller sở hữu vòng đời, mục 5.4).</summary>
    public static string Hash(byte[] credentialUtf8, Argon2Params parameters)
    {
        ArgumentNullException.ThrowIfNull(credentialUtf8);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = ComputeHash(credentialUtf8, salt, parameters);
        try
        {
            return FormatPhc(parameters, salt, hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    /// <summary>
    /// So sánh constant-time (mục 4.3). Ném <see cref="FormatException"/> nếu <paramref name="phc"/>
    /// không đúng cấu trúc PHC mong đợi — caller (đọc từ <c>auth.dat</c>) coi đây là dấu hiệu dữ
    /// liệu hỏng, không phải "sai mật khẩu".
    /// </summary>
    public static bool Verify(byte[] credentialUtf8, string phc)
    {
        ArgumentNullException.ThrowIfNull(credentialUtf8);
        (Argon2Params parameters, byte[] salt, byte[] expectedHash) = ParsePhc(phc);
        byte[] computed = ComputeHash(credentialUtf8, salt, parameters);
        try
        {
            return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computed);
        }
    }

    /// <summary>
    /// Chạy Argon2id thật rồi huỷ kết quả ngay, không trả về/so sánh gì (Architecture/08 mục 7.9,
    /// ADR-84 — bù thời gian chống timing oracle nhánh <c>auth.dat</c> corrupt/chưa-setup). Tái
    /// dùng đúng <see cref="ComputeHash"/> nội bộ dùng cho verify thật — cùng effort CPU/bộ nhớ.
    /// </summary>
    public static void ComputeAndDiscard(byte[] credentialUtf8, byte[] salt, Argon2Params parameters)
    {
        byte[] hash = ComputeHash(credentialUtf8, salt, parameters);
        CryptographicOperations.ZeroMemory(hash);
    }

    private static byte[] ComputeHash(byte[] credentialUtf8, byte[] salt, Argon2Params parameters)
    {
        using var argon2 = new Argon2id(credentialUtf8)
        {
            Salt = salt,
            DegreeOfParallelism = parameters.Parallelism,
            Iterations = parameters.Iterations,
            MemorySize = parameters.MemoryKb,
        };
        return argon2.GetBytes(HashSizeBytes);
    }

    private static string FormatPhc(Argon2Params p, byte[] salt, byte[] hash) =>
        FormattableString.Invariant(
            $"$argon2id$v={ArgonVersion}$m={p.MemoryKb},t={p.Iterations},p={p.Parallelism}${EncodeUnpadded(salt)}${EncodeUnpadded(hash)}");

    private static (Argon2Params Parameters, byte[] Salt, byte[] Hash) ParsePhc(string phc)
    {
        ArgumentNullException.ThrowIfNull(phc);
        string[] parts = phc.Split('$');
        // "" , "argon2id", "v=19", "m=..,t=..,p=..", "<salt>", "<hash>" — 6 phần (chuỗi bắt đầu bằng '$').
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id")
        {
            throw new FormatException($"Malformed Argon2id PHC string (segment count/prefix): '{phc}'.");
        }

        if (parts[2] != FormattableString.Invariant($"v={ArgonVersion}"))
        {
            throw new FormatException($"Unsupported Argon2 version segment: '{parts[2]}'.");
        }

        Argon2Params parameters = ParseParamsSegment(parts[3]);
        byte[] salt = DecodeUnpadded(parts[4]);
        byte[] hash = DecodeUnpadded(parts[5]);
        return (parameters, salt, hash);
    }

    private static Argon2Params ParseParamsSegment(string segment)
    {
        // "m=32768,t=2,p=2"
        string[] pairs = segment.Split(',');
        if (pairs.Length != 3)
        {
            throw new FormatException($"Malformed Argon2id parameter segment: '{segment}'.");
        }

        int memoryKb = ParseNamedInt(pairs[0], "m");
        int iterations = ParseNamedInt(pairs[1], "t");
        int parallelism = ParseNamedInt(pairs[2], "p");
        return new Argon2Params(memoryKb, iterations, parallelism);
    }

    private static int ParseNamedInt(string pair, string expectedName)
    {
        string[] kv = pair.Split('=');
        if (kv.Length != 2 || kv[0] != expectedName || !int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"Malformed Argon2id parameter '{pair}' (expected '{expectedName}=<int>').");
        }

        return value;
    }

    private static string EncodeUnpadded(byte[] data) => Convert.ToBase64String(data).TrimEnd('=');

    private static byte[] DecodeUnpadded(string base64NoPad)
    {
        int paddingNeeded = (4 - (base64NoPad.Length % 4)) % 4;
        string padded = paddingNeeded == 0 ? base64NoPad : base64NoPad + new string('=', paddingNeeded);
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Malformed base64 segment in Argon2id PHC string: '{base64NoPad}'.", ex);
        }
    }
}
