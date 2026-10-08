# Build bản cài đặt phát hành: publish 6 executable (ParentalGuardDeveloperMode=false, Vision CUỐI CÙNG) vào
# publish\release\ParentalGuard, chép CHỈ model Marqo, biên dịch installer\ParentalGuard.iss bằng Inno Setup 6, ghi SHA256SUMS.
# Dùng:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-Version 0.9.0]
param(
    [string]$Version = "0.9.0",
    [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "publish\release\ParentalGuard"
$flags = @("-p:ParentalGuardDeveloperMode=false", "-p:Version=$Version")

if (-not (Test-Path $Iscc)) { $Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $Iscc)) { throw "Không tìm thấy ISCC.exe — cài Inno Setup 6: winget install JRSoftware.InnoSetup --scope user" }
if (Test-Path $out) { throw "Thư mục $out đã tồn tại — xoá thủ công trước để bản cài không lẫn file cũ." }

foreach ($proj in "ParentalGuard.Service", "ParentalGuard.Overlay", "ParentalGuard.UI", "ParentalGuard.Watchdog", "ParentalGuard.Uninstaller", "ParentalGuard.Vision") {
    $csproj = Join-Path $root "src\$proj\$proj.csproj"
    dotnet build $csproj -c Release -r win-x64 --self-contained true --no-incremental @flags
    if ($LASTEXITCODE -ne 0) { throw "Build $proj thất bại." }
    dotnet publish $csproj -c Release -r win-x64 --self-contained true --no-build @flags -o $out
    if ($LASTEXITCODE -ne 0) { throw "Publish $proj thất bại." }
}

# Bản phát hành chỉ dùng model Marqo (IMG-014a) — không kèm GantMan/Falconsai.
New-Item -ItemType Directory -Force (Join-Path $out "models") | Out-Null
Copy-Item (Join-Path $root "models\nsfw_marqo_384.onnx") (Join-Path $out "models") -Force

& $Iscc /Q "/DAppVersion=$Version" (Join-Path $PSScriptRoot "ParentalGuard.iss")
if ($LASTEXITCODE -ne 0) { throw "Biên dịch installer thất bại." }

$dist = Join-Path $root "dist"
$setup = Join-Path $dist "ParentalGuard-Setup-$Version-win-x64.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $dist "SHA256SUMS") -Value "$hash  $(Split-Path $setup -Leaf)" -Encoding ascii
Write-Host "Xong: $setup" -ForegroundColor Green
Write-Host "SHA-256: $hash"
