using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// CA5392: giới hạn tìm DLL native (advapi32) trong System32, không tìm theo thư mục ứng dụng/PATH.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// Test hook cho logic thuần nội bộ (PeerServiceRecovery.ResolvePeerBinaryPath/BuildCreateArguments,
// RegistryStartValueWatcher overload root-hive-tuỳ-ý) — Đợt 4, cùng pattern Service/AssemblyInfo.cs.
[assembly: InternalsVisibleTo("ParentalGuard.Service.Tests")]
[assembly: InternalsVisibleTo("ParentalGuard.Watchdog.Tests")]
