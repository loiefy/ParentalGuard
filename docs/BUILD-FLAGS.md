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

## Mô hình AI

- `models/nsfw_marqo_384.onnx` (22 MB) có trong repo.
- `models/nsfw_falconsai_224.onnx` (343 MB) KHÔNG có trong repo — tạo lại bằng `python tools/export_nsfw_models.py models`
  (cần `torch`, `timm`, `transformers`, `onnx`).
- Đổi file model ⇒ phải cập nhật SHA-256 trong `src/ParentalGuard.Vision/ModelIntegrity/ExpectedModelChecksum.cs` (`MISC-090`).
