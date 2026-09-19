using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>PWD-010/013/014, Architecture/04 mục 4 — <c>auth.dat</c> roundtrip + phát hiện hỏng.</summary>
public class AuthDataStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pg-auth-{Guid.NewGuid():N}.dat");

    private static AuthData SampleData() => new(
        new PasswordEntryData("$argon2id$v=19$m=32768,t=2,p=2$c2FsdA$aGFzaA", Argon2ParamsData.From(Argon2Params.Official), 1000),
        new RecoveryKeyEntryData("$argon2id$v=19$m=32768,t=2,p=2$c2FsdA$aGFzaA", Argon2ParamsData.From(Argon2Params.Official), 1000, Used: false),
        RateLimitData.Initial);

    [Fact]
    public void TryLoad_NonExistentFile_ReturnsNull()
    {
        Assert.Null(AuthDataStore.TryLoad(_path));
    }

    [Fact]
    public void Exists_NonExistentFile_ReturnsFalse()
    {
        Assert.False(AuthDataStore.Exists(_path));
    }

    [Fact]
    public void Save_ThenTryLoad_RoundTripsAllFields()
    {
        AuthData original = SampleData();

        AuthDataStore.Save(_path, original);
        AuthData? loaded = AuthDataStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(original.Password.HashPhc, loaded!.Password.HashPhc);
        Assert.Equal(original.Password.Argon2Params.MemoryKb, loaded.Password.Argon2Params.MemoryKb);
        Assert.Equal(original.RecoveryKey.HashPhc, loaded.RecoveryKey.HashPhc);
        Assert.False(loaded.RecoveryKey.Used);
        Assert.Equal(0, loaded.RateLimit.ConsecutiveFailures);
        Assert.True(AuthDataStore.Exists(_path));
    }

    [Fact]
    public void Save_RoundTripsRateLimitFields_WhenSet()
    {
        AuthData withFailures = SampleData() with { RateLimit = new RateLimitData(4, 12345, 12345 + 30_000) };

        AuthDataStore.Save(_path, withFailures);
        AuthData? loaded = AuthDataStore.TryLoad(_path);

        Assert.Equal(4, loaded!.RateLimit.ConsecutiveFailures);
        Assert.Equal(12345, loaded.RateLimit.LastFailureAtUnixMs);
        Assert.Equal(12345 + 30_000, loaded.RateLimit.DelayUntilUnixMs);
    }

    [Fact]
    public void Save_Overwrite_ReplacesEntireContent()
    {
        AuthDataStore.Save(_path, SampleData());
        AuthData second = SampleData() with { RecoveryKey = SampleData().RecoveryKey with { Used = true } };

        AuthDataStore.Save(_path, second);
        AuthData? loaded = AuthDataStore.TryLoad(_path);

        Assert.True(loaded!.RecoveryKey.Used);
    }

    [Fact]
    public void TryLoad_FileTooSmall_ThrowsAuthDataCorruptException()
    {
        File.WriteAllBytes(_path, [0x01, 0x02]);

        Assert.Throws<AuthDataCorruptException>(() => AuthDataStore.TryLoad(_path));
    }

    [Fact]
    public void TryLoad_UnknownVersion_ThrowsAuthDataCorruptException()
    {
        byte[] bogus = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x01, 0x02];
        File.WriteAllBytes(_path, bogus);

        Assert.Throws<AuthDataCorruptException>(() => AuthDataStore.TryLoad(_path));
    }

    [Fact]
    public void TryLoad_ValidVersionButGarbageCiphertext_ThrowsAuthDataCorruptException()
    {
        byte[] content = new byte[20];
        BitConverter.GetBytes(1u).CopyTo(content, 0); // version hợp lệ
        for (int i = 4; i < content.Length; i++)
        {
            content[i] = 0xAB; // không phải DPAPI blob hợp lệ
        }

        File.WriteAllBytes(_path, content);

        Assert.Throws<AuthDataCorruptException>(() => AuthDataStore.TryLoad(_path));
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
