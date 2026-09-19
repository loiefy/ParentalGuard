using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 8.2 — khoá cứng đúng con số đã duyệt để
/// tránh regression vô tình đổi exit code (Service dựa vào đúng số này để quyết định IL fallback).
/// </summary>
public class VisionExitCodesTests
{
    [Fact]
    public void CaptureInitAccessDenied_Is17() => Assert.Equal(17, VisionExitCodes.CaptureInitAccessDenied);

    [Fact]
    public void ModelIntegrityCheckFailed_Is18() => Assert.Equal(18, VisionExitCodes.ModelIntegrityCheckFailed);

    [Fact]
    public void PipelineRepeatedFailure_Is19() => Assert.Equal(19, VisionExitCodes.PipelineRepeatedFailure);
}
