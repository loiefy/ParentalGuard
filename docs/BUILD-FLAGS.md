# Cờ build (MSBuild properties)

Khai báo trong `src/Directory.Build.props`, áp dụng cho cả 6 executable. Truyền khi build/publish bằng
`-p:<Tên>=<giá trị>` — **phải dùng cùng bộ cờ cho cả 6 project** của 1 bản build.

| Cờ | Mặc định | Ý nghĩa | Bản phát hành |
|---|---|---|---|
| `ParentalGuardModel` | `Marqo` | Mô hình AI nhận diện: `Marqo` (`IMG-014a`), `GantMan` (DEPRECATED, `IMG-014`), `Falconsai` (thử nghiệm). File `.onnx` tương ứng phải có trong `models/` của bản cài | `Marqo` |
| `ParentalGuardDeveloperMode` | `true` | Chế độ developer (`DEV-050`–`052`): viền đỏ cam + % quanh cửa sổ đang theo dõi, viền màu cho 5 vùng con, viền tím nét đứt cho vùng video/ảnh động | **`false` (bắt buộc)** |
| `ParentalGuardSuppressOverlay` | `false` | Bản debug quay màn hình: vẫn nhận diện + vẽ khung, nhưng KHÔNG dựng lớp che và không ghi `ContentBlocked` | `false` |
| `ParentalGuardFastDetection` | `false` | Bản debug quét nhanh: chu kỳ cố định 500 ms, mọi cửa sổ trong 1 chu kỳ | `false` |
| `ParentalGuardDiagnosticLog` | `false` | Log chẩn đoán runtime ra `C:\PGDebugLog\` (Vision chạy Low IL có thể không ghi được) | `false` |

## Lệnh mẫu

Publish đủ 6 executable vào chung 1 thư mục — **luôn publish `ParentalGuard.Vision` cuối cùng** (xem `publish/E2E-TESTING.md`):

```powershell
$flags = "-p:ParentalGuardDeveloperMode=false"   # bản phát hành
foreach ($proj in "ParentalGuard.Service","ParentalGuard.Overlay","ParentalGuard.UI","ParentalGuard.Watchdog","ParentalGuard.Uninstaller","ParentalGuard.Vision") {
    dotnet build   "src\$proj\$proj.csproj" -c Release -r win-x64 --self-contained true --no-incremental $flags
    dotnet publish "src\$proj\$proj.csproj" -c Release -r win-x64 --self-contained true --no-build $flags -o publish\ParentalGuard
}
```

`--no-incremental` khi đổi cờ: build tăng dần có thể giữ lại bản biên dịch với bộ cờ cũ.

## Bản cài đặt phát hành

Một lệnh (cần Inno Setup 6: `winget install JRSoftware.InnoSetup --scope user`):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0 -Arch x64
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0 -Arch x86
```

`GEN-001a`: 2 bản — **x64** (Windows 10/11 64-bit) và **x86** (Windows 10 32-bit; file cài x86 từ chối chạy trên Windows 64-bit).
Mỗi bản publish vào `publish\release\<Arch>\ParentalGuard`; `dist\SHA256SUMS` liệt kê cả 2 file cài. Bản x86 dùng ONNX Runtime
DirectML **1.22.1** (bản cuối còn 32-bit — `ParentalGuard.Vision.csproj` tự chọn theo `-r win-x86`). Chạy test ở chế độ 32-bit:
`dotnet test -a x86` (cần .NET 10 runtime x86; đặt `DOTNET_ROOT_X86` nếu cài ngoài `C:\Program Files (x86)\dotnet`).

Script publish đủ 7 executable với `ParentalGuardDeveloperMode=false` (Vision cuối cùng) vào `publish\release\ParentalGuard`,
chép model Marqo + NudeNet 640m, biên dịch `installer\ParentalGuard.iss`, ghi `dist\ParentalGuard-Setup-<version>-win-x64.exe` +
`dist\SHA256SUMS`. Thiết kế: `Architecture/11-deployment-release-architecture.md`.

## Mô hình AI

- `models/nsfw_marqo_384.onnx` (22 MB) có trong repo.
- `models/nudenet_640m.onnx` (103.5 MB, `IMG-016c` — xác minh bộ phận cơ thể trên vùng Marqo báo vi phạm) KHÔNG có trong repo
  (vượt giới hạn 100 MB/file của GitHub) — tải bằng `powershell -ExecutionPolicy Bypass -File tools\download_nudenet_model.ps1`
  (kiểm SHA-256; `build-installer.ps1` tự gọi). Thiếu file này Vision vẫn chạy nhưng chỉ với Marqo (chặn nhiều hơn; trạng thái
  Vision trên Dashboard có thêm `detector=missing`).
- `models/nsfw_falconsai_224.onnx` (343 MB) KHÔNG có trong repo — tạo lại bằng `python tools/export_nsfw_models.py models`
  (cần `torch`, `timm`, `transformers`, `onnx`).
- Đổi file model ⇒ phải cập nhật SHA-256 trong `src/ParentalGuard.Vision/ModelIntegrity/ExpectedModelChecksum.cs` (`MISC-090`).
