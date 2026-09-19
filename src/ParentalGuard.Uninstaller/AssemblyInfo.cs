using System.Runtime.InteropServices;

// CA5392: giới hạn tìm DLL native (kernel32/advapi32) trong System32, không tìm theo thư mục ứng
// dụng/PATH (chặn DLL planting) — cùng pattern ParentalGuard.Service/AssemblyInfo.cs.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
