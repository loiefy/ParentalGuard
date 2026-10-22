# Chạy bởi Setup (quyền Administrator) TRƯỚC khi chép file.
# 1) Cài đè bản cũ: Service và Watchdog canh chừng lẫn nhau (dừng 1 bên thì bên kia khởi động lại), nên phải chuyển cả 2 sang
#    Disabled trước rồi mới dừng — không thì file .exe đang chạy sẽ không ghi đè được. Sau khi chép xong, setup-services.ps1 bật lại.
# 2) Cài lại sau khi gỡ mà CHƯA khởi động lại máy (bug 2026-10-12): trình gỡ bản cũ hẹn Windows xoá Uninstaller.exe + thư mục cài đặt
#    lúc khởi động lại — nếu để nguyên, lần khởi động sau sẽ xoá mất file của bản VỪA CÀI. Huỷ đúng các lệnh hẹn trỏ vào thư mục này.
param([string]$AppDir = (Join-Path $env:ProgramFiles "ParentalGuard"))

$ErrorActionPreference = "Continue"
$names = "ParentalGuardWatchdog", "ParentalGuardService"
$processes = "ParentalGuard.UI", "ParentalGuard.Tray", "ParentalGuard.Vision", "ParentalGuard.Overlay", "ParentalGuard.Service", "ParentalGuard.Watchdog", "ParentalGuard.Uninstaller"

# Bug 2026-10-20 (cài đè lên bản đang chạy): "chuyển Disabled rồi sc stop lần lượt" KHÔNG dừng được — Service đang chạy tự đặt lại
# Start=Auto ngay khi bị đổi (ANTI, RegistryStartValueWatcher), dừng bên nào thì bên kia khởi động lại, và tiến trình bị giết thì
# Windows tự khởi động lại theo Recovery Options. Trình tự đã kiểm chứng trên máy thật: bỏ Recovery Options → giết CẢ 2 tiến trình
# trong CÙNG 1 lệnh → chuyển Disabled khi không còn bên nào canh. setup-services.ps1 bật lại; Service tự đặt lại Recovery Options
# mỗi lần khởi động (ScFailureConfigurator).
for ($attempt = 1; $attempt -le 5; $attempt++) {
    foreach ($name in $names) {
        if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
            & sc.exe failure $name reset= 0 actions= '""' | Out-Null
        }
    }

    & taskkill.exe /F /IM ParentalGuard.Service.exe /IM ParentalGuard.Watchdog.exe 2>&1 | Out-Null
    foreach ($name in $names) {
        if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
            & sc.exe config $name start= disabled | Out-Null
        }
    }

    # Tiến trình con (Vision/Overlay chạy trong phiên người dùng), Dashboard và icon khay có thể còn giữ file.
    Get-Process -Name $processes -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    if (-not (Get-Process -Name $processes -ErrorAction SilentlyContinue)) { break }
}

# PendingFileRenameOperations: REG_MULTI_SZ theo CẶP (nguồn, đích). Bỏ các cặp có nguồn nằm trong thư mục cài đặt; giữ nguyên mọi cặp khác.
$smKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager"
$pending = (Get-ItemProperty -Path $smKey -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations
if ($pending) {
    $prefix = "\??\" + $AppDir.TrimEnd("\")
    $kept = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i + 1 -lt $pending.Count; $i += 2) {
        # Windows có thể ghi kèm tiền tố cờ trước "\??\" (vd "*1\??\C:\..." — thấy trên máy thật 2026-10-12) — bỏ tiền tố trước khi so.
        $source = $pending[$i] -replace '^(\*\d+|!)', ''
        $isOurs = $source.Equals($prefix, [StringComparison]::OrdinalIgnoreCase) -or
                  $source.StartsWith($prefix + "\", [StringComparison]::OrdinalIgnoreCase)
        if (-not $isOurs) {
            $kept.Add($pending[$i])
            $kept.Add($pending[$i + 1])
        }
    }

    if ($kept.Count -ne $pending.Count) {
        if ($kept.Count -eq 0) {
            Remove-ItemProperty -Path $smKey -Name PendingFileRenameOperations -ErrorAction SilentlyContinue
        } else {
            Set-ItemProperty -Path $smKey -Name PendingFileRenameOperations -Value ([string[]]$kept) -Type MultiString
        }
    }
}

Start-Sleep -Seconds 1
exit 0
