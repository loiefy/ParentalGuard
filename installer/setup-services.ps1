# Chạy bởi Setup (quyền Administrator) SAU khi chép file: đăng ký (hoặc cập nhật) 2 Windows Service chạy LocalSystem, tự khởi
# động cùng Windows, rồi khởi động. Service tự thiết lập ACL, tường lửa (WFP) và Recovery Options lúc chạy.
param([Parameter(Mandatory = $true)][string]$AppDir)

$ErrorActionPreference = "Continue"
$services = @(
    @{ Name = "ParentalGuardService"; Exe = "ParentalGuard.Service.exe"; Display = "ParentalGuard Service" },
    @{ Name = "ParentalGuardWatchdog"; Exe = "ParentalGuard.Watchdog.exe"; Display = "ParentalGuard Watchdog" }
)

foreach ($s in $services) {
    # Đường dẫn có dấu cách → phải đặt trong ngoặc kép (tránh lỗ hổng "unquoted service path").
    $bin = '"' + (Join-Path $AppDir $s.Exe) + '"'
    if (Get-Service -Name $s.Name -ErrorAction SilentlyContinue) {
        & sc.exe config $s.Name binPath= $bin start= auto obj= LocalSystem DisplayName= $s.Display | Out-Null
    } else {
        & sc.exe create $s.Name binPath= $bin start= auto obj= LocalSystem DisplayName= $s.Display | Out-Null
    }
}

& sc.exe description ParentalGuardService "ParentalGuard - detects and covers sensitive on-screen content, entirely on this computer." | Out-Null
& sc.exe description ParentalGuardWatchdog "ParentalGuard - watchdog that restarts ParentalGuard Service if it is stopped." | Out-Null

foreach ($s in $services) {
    Start-Service -Name $s.Name -ErrorAction SilentlyContinue
}

$running = @($services | Where-Object { (Get-Service -Name $_.Name -ErrorAction SilentlyContinue).Status -eq "Running" }).Count
if ($running -eq $services.Count) { exit 0 } else { exit 2 }
