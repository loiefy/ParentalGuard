namespace ParentalGuard.Service.Data;

/// <summary>
/// 1 dòng bảng <c>icon_positions</c> (Architecture/04-data-architecture.md mục 3.6a, `FE-020a`) —
/// khoá theo <c>MONITORINFOEX.szDevice</c> Win32 (vd <c>\\.\DISPLAY1</c>), KHÔNG phải
/// <c>monitor_id</c> dùng trong <c>OverlayRect</c> (2 khái niệm tách biệt, ADR-67).
/// </summary>
public sealed record IconPositionData(string DeviceName, int X, int Y);
