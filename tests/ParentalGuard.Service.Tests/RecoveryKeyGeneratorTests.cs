using System.Text;
using ParentalGuard.Service.Auth;

namespace ParentalGuard.Service.Tests;

/// <summary>PWD-030/033, Architecture/08 mục 6 — format Crockford Base32, entropy, chuẩn hoá lúc verify.</summary>
public class RecoveryKeyGeneratorTests
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void GenerateCanonical_HasExpectedLengthAndAlphabet()
    {
        string canonical = RecoveryKeyGenerator.GenerateCanonical();

        Assert.Equal(24, canonical.Length);
        Assert.All(canonical, c => Assert.Contains(c, CrockfordAlphabet));
    }

    [Fact]
    public void GenerateCanonical_ExcludesConfusingCharacters()
    {
        // 200 lần sinh để có cơ hội thực tế phát hiện nếu bảng chữ bị lẫn I/L/O/U (mục 6.1).
        for (int i = 0; i < 200; i++)
        {
            string canonical = RecoveryKeyGenerator.GenerateCanonical();
            Assert.DoesNotContain('I', canonical);
            Assert.DoesNotContain('L', canonical);
            Assert.DoesNotContain('O', canonical);
            Assert.DoesNotContain('U', canonical);
        }
    }

    [Fact]
    public void GenerateCanonical_ManyCalls_ProduceDistinctValues()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 500; i++)
        {
            seen.Add(RecoveryKeyGenerator.GenerateCanonical());
        }

        Assert.Equal(500, seen.Count); // 120-bit entropy — collision thực tế bằng 0 trong 500 mẫu
    }

    [Fact]
    public void FormatGrouped_ProducesSixGroupsOfFourSeparatedByHyphen()
    {
        string canonical = RecoveryKeyGenerator.GenerateCanonical();
        string grouped = RecoveryKeyGenerator.FormatGrouped(canonical);

        string[] groups = grouped.Split('-');
        Assert.Equal(6, groups.Length);
        Assert.All(groups, g => Assert.Equal(4, g.Length));
        Assert.Equal(canonical, grouped.Replace("-", string.Empty));
    }

    [Fact]
    public void FormatGrouped_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => RecoveryKeyGenerator.FormatGrouped("TOOSHORT"));
    }

    [Fact]
    public void NormalizeUtf8_StripsHyphensAndWhitespace_UppercasesLowercase()
    {
        string canonical = RecoveryKeyGenerator.GenerateCanonical();
        string grouped = RecoveryKeyGenerator.FormatGrouped(canonical);
        string userTyped = " " + grouped.ToLowerInvariant() + "\t"; // mô phỏng phụ huynh gõ lại: hoa/thường lẫn lộn, khoảng trắng thừa

        byte[] normalized = RecoveryKeyGenerator.NormalizeUtf8(Encoding.UTF8.GetBytes(userTyped));

        Assert.Equal(canonical, Encoding.UTF8.GetString(normalized));
    }

    [Fact]
    public void NormalizeUtf8_DoesNotReduceEntropy_AllOutputCharsStillInAlphabet()
    {
        byte[] normalized = RecoveryKeyGenerator.NormalizeUtf8(Encoding.UTF8.GetBytes("ab12-cd34"));

        Assert.All(Encoding.UTF8.GetString(normalized), c => Assert.Contains(c, CrockfordAlphabet));
    }
}
