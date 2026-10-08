# 11 — Deployment & Release Architecture

> Version: v0.1.0 | Trạng thái: Draft | Cập nhật: 2026-10-08

## 1. Mục đích

Mô tả cách đóng gói và phát hành ParentalGuard cho người dùng cuối: bản build phát hành, trình cài đặt, mục gỡ cài đặt, checksum.
Ký số (SignPath, `SEC-030`/`DEV-012`) **chưa làm** — chủ dự án để lại; mục 5 ghi chỗ sẽ gắn vào.

## 2. Bản build phát hành

- Cờ build: `ParentalGuardDeveloperMode=false` (bắt buộc — bỏ khung viền/điểm debug `DEV-050`–`052`), các cờ debug khác giữ mặc định `false`, `ParentalGuardModel=Marqo` (xem `docs/BUILD-FLAGS.md`).
- Publish 6 executable self-contained `win-x64` vào `publish\release\ParentalGuard`, **`ParentalGuard.Vision` cuối cùng** (UI/Overlay ghi đè DLL dùng chung — đã gây crash Vision 2026-10-08 khi bỏ qua thứ tự này).
- Model: **chỉ** `models\nsfw_marqo_384.onnx` (`IMG-014a`, checksum `MISC-090`) — không kèm GantMan/Falconsai.
- Một lệnh: `installer\build-installer.ps1 -Version x.y.z` (publish + chép model + biên dịch installer + `SHA256SUMS`).

## 3. Trình cài đặt — Inno Setup 6 (ADR-160)

**ADR-160**: dùng Inno Setup (miễn phí) thay MSI/MSIX dự kiến ban đầu ở `00-INDEX`:
- MSIX không phù hợp app có Windows Service chạy LocalSystem + Watchdog + WFP.
- MSI tự tạo mục gỡ cài đặt theo GUID sản phẩm và cho phép gỡ thẳng (`msiexec /x`) **không cần mật khẩu** — trái `ANTI-0xx`; trong khi `ParentalGuard.Uninstaller` (Đợt 4) đã thiết kế sẵn để xoá đúng khoá `Uninstall\ParentalGuard`.
- Inno cho phép tắt trình gỡ riêng (`Uninstallable=no`) và tự ghi mục gỡ cài đặt trỏ tới Uninstaller có mật khẩu.

Hành vi `installer\ParentalGuard.iss`:

| Bước | Chi tiết |
|---|---|
| Quyền | `PrivilegesRequired=admin`, chỉ Windows 10 1809+ x64 |
| Thư mục | Cố định `%ProgramFiles%\ParentalGuard` (`DisableDirPage`) — Service/Watchdog/Uninstaller dùng đường dẫn này (`InstallPaths`/`UninstallerPaths`) |
| Cài đè | `stop-services.ps1` (trước khi chép): chuyển `ParentalGuardWatchdog` + `ParentalGuardService` sang Disabled TRƯỚC rồi mới dừng (2 bên canh chừng lẫn nhau, dừng 1 bên thì bên kia bật lại), kill UI/Vision/Overlay còn giữ file |
| Chép file | Toàn bộ `publish\release\ParentalGuard` |
| Service | `setup-services.ps1` (sau khi chép): tạo/cập nhật 2 service LocalSystem, tự khởi động (`start= auto`), `binPath` **trong ngoặc kép** (tránh lỗ hổng unquoted service path — bản đăng ký thủ công cũ không có ngoặc kép), khởi động cả 2; lỗi → thông báo khởi động lại máy |
| Gỡ cài đặt | `HKLM\…\Uninstall\ParentalGuard`: `UninstallString` = `ParentalGuard.Uninstaller.exe` (bắt mật khẩu `ANTI`), `NoModify`/`NoRepair`; Uninstaller xoá khoá này ở bước dọn dẹp |
| Shortcut | Start Menu (luôn) + Desktop (tuỳ chọn) → `ParentalGuard.UI.exe`; cuối cùng tuỳ chọn mở Dashboard dưới quyền người dùng gốc |
| Ngôn ngữ trình cài đặt | Tiếng Việt (bản dịch cộng đồng Inno, `installer\Languages\Vietnamese.isl`), Anh, Pháp, Tây Ban Nha, Bồ Đào Nha; tiếng Trung chưa có bản dịch Inno → hiện tiếng Anh |
| Nén | `lzma2/max`, nén trong tiến trình riêng (`ultra64` làm ISCC 32-bit hết bộ nhớ) — ~95 MB từ ~310 MB |

## 4. Phát hành

- Đầu ra `dist\ParentalGuard-Setup-<version>-win-x64.exe` + `dist\SHA256SUMS` (`DEV-009`); `dist/` không đưa vào git — đính kèm GitHub Releases.
- Version lấy từ `-p:Version` (mặc định `VersionPrefix` trong `src/Directory.Build.props`).

## 5. Việc còn lại

- **Ký số** Setup.exe + 6 executable qua SignPath (`SEC-030`) — chủ dự án để lại. Khi làm: ký executable TRƯỚC khi biên dịch installer, ký Setup.exe sau; Architecture/03 mục 4.2 điều kiện 3b (đối chiếu thumbprint) mới bật được.
- **Kiểm thử trên máy sạch/máy ảo** trước khi phát hành: cài mới, cài đè, gỡ qua Settings (mật khẩu), Watchdog khôi phục Service. Máy dev của chủ dự án đang chạy chế độ thử nghiệm không có Watchdog — không chạy installer trực tiếp ở đó khi chưa test VM.
- Bản dịch tiếng Trung cho trình cài đặt.

## 6. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.1.0 | 2026-10-08 | Khởi tạo — chủ dự án yêu cầu "xây dựng bản cài đặt, bỏ cờ khung viền debug, chỉ giữ model Marqo": Inno Setup (ADR-160), script build một lệnh, mục gỡ cài đặt trỏ Uninstaller có mật khẩu, đăng ký 2 service với binPath có ngoặc kép |
