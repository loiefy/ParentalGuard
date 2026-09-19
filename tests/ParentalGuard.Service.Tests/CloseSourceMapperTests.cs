using ParentalGuard.Ipc.Protocol;
using ParentalGuard.Service.Ipc;

namespace ParentalGuard.Service.Tests;

public class CloseSourceMapperTests
{
    [Theory]
    [InlineData(CloseSource.Manual, "manual")]
    [InlineData(CloseSource.AutoTimeout, "auto-timeout")]
    [InlineData(CloseSource.Unspecified, "manual")] // fail-secure: không nên xảy ra (Overlay luôn set), nghiêng về giá trị ít gây hiểu nhầm nhất
    public void ToAuditLogValue_MapsExactLiteralsRequiredByBE089b(CloseSource source, string expected)
    {
        Assert.Equal(expected, CloseSourceMapper.ToAuditLogValue(source));
    }
}
