namespace ParentalGuard.Service.Auth;

/// <summary>Snapshot toàn bộ nội dung <c>auth.dat</c> (Architecture/04-data-architecture.md mục 4).</summary>
public sealed record AuthData(PasswordEntryData Password, RecoveryKeyEntryData RecoveryKey, RateLimitData RateLimit);

public sealed record Argon2ParamsData(int MemoryKb, int Iterations, int Parallelism)
{
    public static Argon2ParamsData From(Argon2Params p) => new(p.MemoryKb, p.Iterations, p.Parallelism);
}

public sealed record PasswordEntryData(string HashPhc, Argon2ParamsData Argon2Params, long UpdatedAtUnixMs);

public sealed record RecoveryKeyEntryData(string HashPhc, Argon2ParamsData Argon2Params, long CreatedAtUnixMs, bool Used);

/// <summary>`rate_limit` (ADR-26, Architecture/04 mục 4) — persist để sống sót qua restart Service.</summary>
public sealed record RateLimitData(int ConsecutiveFailures, long? LastFailureAtUnixMs, long? DelayUntilUnixMs)
{
    public static readonly RateLimitData Initial = new(0, null, null);
}
