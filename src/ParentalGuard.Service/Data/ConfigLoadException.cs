namespace ParentalGuard.Service.Data;

/// <summary>
/// <c>config.db</c> không đọc/giải mã được — kích hoạt nhánh fail-secure
/// (BE-061/061a/061b, Architecture/04-data-architecture.md mục 6.1).
/// </summary>
public sealed class ConfigLoadException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
