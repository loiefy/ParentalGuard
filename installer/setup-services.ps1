# Chạy bởi Setup (quyền Administrator) SAU khi chép file: đăng ký (hoặc cập nhật) 2 Windows Service chạy LocalSystem, tự khởi
# động cùng Windows, rồi khởi động. Service tự thiết lập ACL, tường lửa (WFP) và Recovery Options lúc chạy.
param([Parameter(Mandatory = $true)][string]$AppDir)

$ErrorActionPreference = "Continue"
$services = @(
    @{ Name = "ParentalGuardService"; Exe = "ParentalGuard.Service.exe"; Display = "ParentalGuard Service";
       Description = "ParentalGuard - detects and covers sensitive on-screen content, entirely on this computer." },
    @{ Name = "ParentalGuardWatchdog"; Exe = "ParentalGuard.Watchdog.exe"; Display = "ParentalGuard Watchdog";
       Description = "ParentalGuard - watchdog that restarts ParentalGuard Service if it is stopped." }
)

foreach ($s in $services) {
    # Đường dẫn có dấu cách → BẮT BUỘC trong ngoặc kép (tránh lỗ hổng "unquoted service path": C:\Program.exe chạy với quyền SYSTEM).
    # Bug 2026-10-09: truyền chuỗi có ngoặc kép cho sc.exe từ PowerShell 5.1 thì ngoặc kép bị bỏ mất — nên tạo bằng New-Service
    # (gọi thẳng CreateService) và ghi ImagePath vào registry (cả khi service đã tồn tại từ bản cài cũ / đăng ký thủ công).
    $quoted = '"' + (Join-Path $AppDir $s.Exe) + '"'
    if (-not (Get-Service -Name $s.Name -ErrorAction SilentlyContinue)) {
        New-Service -Name $s.Name -BinaryPathName $quoted -DisplayName $s.Display -Description $s.Description -StartupType Automatic | Out-Null
    }

    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)"
    Set-ItemProperty -Path $key -Name ImagePath -Value $quoted -Type ExpandString
    Set-ItemProperty -Path $key -Name ObjectName -Value "LocalSystem"
    Set-ItemProperty -Path $key -Name DisplayName -Value $s.Display
    Set-ItemProperty -Path $key -Name Description -Value $s.Description
    & sc.exe config $s.Name start= auto | Out-Null
}

foreach ($s in $services) {
    Start-Service -Name $s.Name -ErrorAction SilentlyContinue
}

$running = @($services | Where-Object { (Get-Service -Name $_.Name -ErrorAction SilentlyContinue).Status -eq "Running" }).Count
if ($running -eq $services.Count) { exit 0 } else { exit 2 }
