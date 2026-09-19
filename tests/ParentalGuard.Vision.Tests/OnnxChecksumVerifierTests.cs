using System.Security.Cryptography;
using System.Text;
using ParentalGuard.Vision.ModelIntegrity;

namespace ParentalGuard.Vision.Tests;

public class OnnxChecksumVerifierTests
{
    [Fact]
    public void Verify_MatchingHash_ReturnsTrue()
    {
        byte[] modelBytes = Encoding.UTF8.GetBytes("fake-onnx-bytes-for-test");
        byte[] expected = SHA256.HashData(modelBytes);

        Assert.True(OnnxChecksumVerifier.Verify(modelBytes, expected));
    }

    [Fact]
    public void Verify_TamperedBytes_ReturnsFalse()
    {
        byte[] original = Encoding.UTF8.GetBytes("fake-onnx-bytes-for-test");
        byte[] expected = SHA256.HashData(original);

        byte[] tampered = Encoding.UTF8.GetBytes("fake-onnx-bytes-for-test-tampered");

        Assert.False(OnnxChecksumVerifier.Verify(tampered, expected));
    }

    [Fact]
    public void Verify_WrongExpectedHash_ReturnsFalse()
    {
        byte[] modelBytes = Encoding.UTF8.GetBytes("fake-onnx-bytes-for-test");
        byte[] wrongExpected = new byte[32];

        Assert.False(OnnxChecksumVerifier.Verify(modelBytes, wrongExpected));
    }
}
