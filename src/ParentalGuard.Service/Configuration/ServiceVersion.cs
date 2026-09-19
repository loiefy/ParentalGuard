using System.Reflection;

namespace ParentalGuard.Service.Configuration;

/// <summary>Version ghi vào <c>schema_meta.app_version_at_creation</c> (Architecture/04 mục 3.2).</summary>
public static class ServiceVersion
{
    public static readonly string Current =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
