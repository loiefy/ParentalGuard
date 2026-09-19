using System.Runtime.CompilerServices;

// Cho phép tests gọi OverlayStrings.AutoTimeoutCountdown (internal) — pure function format
// chuỗi đếm ngược FE-016g, không mở rộng bề mặt public API.
[assembly: InternalsVisibleTo("ParentalGuard.Overlay.Tests")]
