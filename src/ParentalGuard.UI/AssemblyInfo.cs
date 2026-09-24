using System.Runtime.CompilerServices;

// Cho phép tests gọi DashboardViewModel.ApplyStatus (internal) — seam test-only để verify logic suy ra
// CardState/health-check text mà không cần dựng DispatcherQueue/XamlRoot thật (S2, Đợt 6 giai đoạn 2).
[assembly: InternalsVisibleTo("ParentalGuard.UI.Tests")]
