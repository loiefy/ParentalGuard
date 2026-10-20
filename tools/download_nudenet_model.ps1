# IMG-016c: tải NudeNet v3.4 640m.onnx (103.5 MB — vượt giới hạn 100 MB/file của GitHub nên không nằm trong repo) vào models\,
# kiểm SHA-256 khớp ExpectedModelChecksum.DetectorSha256Hex trước khi giữ lại file.
# Dùng: powershell -ExecutionPolicy Bypass -File tools\download_nudenet_model.ps1
$ErrorActionPreference = "Stop"
$expected = "04fe3d77980780c1f8297dc6d7f942fd5b3abe6942a188f742a85241e4f634eb"
$url = "https://github.com/notAI-tech/NudeNet/releases/download/v3.4-weights/640m.onnx"
$target = Join-Path (Split-Path $PSScriptRoot -Parent) "models\nudenet_640m.onnx"

if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected) {
    Write-Host "Đã có $target (SHA-256 khớp)." -ForegroundColor Green
    exit 0
}

$temp = "$target.download"
Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
$hash = (Get-FileHash $temp -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $expected) {
    Remove-Item $temp -Force
    throw "SHA-256 không khớp ($hash) — không dùng file này."
}

Move-Item $temp $target -Force
Write-Host "Xong: $target" -ForegroundColor Green
