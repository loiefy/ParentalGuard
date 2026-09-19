using System.Runtime.CompilerServices;

// Cho phép tests gọi FrameClassificationPipeline.ProcessFrame (internal) — seam test-only cho
// regression guard zero-out (IMG-040/041), không mở rộng bề mặt public API.
[assembly: InternalsVisibleTo("ParentalGuard.Vision.Tests")]
