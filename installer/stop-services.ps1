# Chạy bởi Setup (quyền Administrator) TRƯỚC khi chép file khi cài đè bản cũ.
# Service và Watchdog canh chừng lẫn nhau (dừng 1 bên thì bên kia khởi động lại), nên phải chuyển cả 2 sang Disabled
# trước rồi mới dừng — không thì file .exe đang chạy sẽ không ghi đè được. Sau khi chép xong, setup-services.ps1 bật lại.
$ErrorActionPreference = "Continue"
$names = "ParentalGuardWatchdog", "ParentalGuardService"

foreach ($name in $names) {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        & sc.exe config $name start= disabled | Out-Null
    }
}

foreach ($name in $names) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        & sc.exe stop $name | Out-Null
        try { $svc.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30)) } catch { }
    }
}

# Tiến trình con (Vision/Overlay chạy trong phiên người dùng) và Dashboard có thể còn giữ file.
Get-Process -Name "ParentalGuard.UI", "ParentalGuard.Vision", "ParentalGuard.Overlay", "ParentalGuard.Service", "ParentalGuard.Watchdog" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
exit 0
