using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// CA5392: giới hạn tìm DLL native (advapi32/kernel32/wtsapi32/fwpuclnt) trong System32,
// không tìm kiếm theo thứ mục ứng dụng/PATH (chặn DLL planting).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// Test hook cho regression test zero-out (AuthCoordinator.PendingSetupForTest — giống pattern
// FrameClassificationPipeline.PixelBufferForTest ở Vision, IMG-003).
[assembly: InternalsVisibleTo("ParentalGuard.Service.Tests")]
