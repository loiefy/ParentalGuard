# Việc cần làm — phiên làm việc tiếp theo

> Ghi lại: 2026-10-01 (cuối phiên). Trạng thái lúc dừng: Spec `APPROVED v0.8.8`, commit `3bde5ad`, 510/510 test pass,
> bản mới nhất đã cài ở `C:\Program Files\ParentalGuard`. Mỗi việc dưới đây là **quyết định sản phẩm (WHAT)** →
> phải cập nhật `Specification/` đúng quy trình archive (CLAUDE.md) trước/cùng lúc với code.

## 1. Theo dõi tối đa 10 cửa sổ cùng lúc (thay vì 4)

- Hiện tại: `BE-071a`/`BE-071b` — trần **4 cửa sổ/chu kỳ** (`CandidateWindowSelector.MaxCandidatesPerCycle = 4`),
  foreground mỗi chu kỳ + 3 suất xoay vòng.
- Cần: nâng lên **10**. Đổi spec `BE-071a`/`PERF-020a`/`IMG-020a` (trần), cân nhắc CPU: mỗi cửa sổ = 1 lần
  capture + (nếu nội dung đổi) 1 lần inference → đo lại CPU với 10 cửa sổ trên máy thật (`PERF-0xx`).
- Lưu ý liên quan: ngưỡng chế độ overlay gộp `BE-088` cũng là 10 overlay.

## 2. Nút cài đặt (bánh răng) trên cửa sổ blur nhỏ lại ~1/2

- Hiện tại: `ContentBlurOverlayForm` — nút `\uE713` font Segoe MDL2 14pt, `Padding = 6`, `AutoSize` (`FE-016i`).
- Cần: thu nhỏ còn khoảng bằng kích thước icon (ước lượng giảm 1 nửa): font ~10–11pt, padding ~2, vùng bấm vẫn
  đủ dễ bấm.

## 3. Nút "Tắt nội dung" phải TẮT ĐƯỢC ứng dụng vi phạm (force)

- Hiện tại: `BE-032`/`BE-089a` — Overlay chỉ `PostMessage(WM_CLOSE)` lên cửa sổ vi phạm; nhiều app (trình duyệt
  nhiều tab, app hỏi "Lưu thay đổi?") **không đóng**. `BE-034c` hiện ghi rõ "**Không kill tiến trình**".
- Cần: force tắt được app vi phạm → đổi spec (supersede phần "không kill" của `BE-034c`, cập nhật `BE-032`).
  Hướng đề xuất: WM_CLOSE trước, sau N giây nếu cửa sổ vẫn còn → `TerminateProcess` tiến trình sở hữu cửa sổ.
- Điểm cần quyết định khi làm: kill ai làm? `Overlay` chạy **Low IL** → không đủ quyền terminate tiến trình Medium IL
  của user → nên để `Service` (SYSTEM) kill theo PID, có kiểm tra an toàn (không kill tiến trình hệ thống/
  `explorer.exe`/chính ParentalGuard, PID phải đúng chủ cửa sổ vi phạm). Ghi audit log. Cảnh báo mất dữ liệu chưa lưu
  của app bị kill (ghi rõ trong `docs/BEHAVIOR-DISCLOSURE.md`).

## 4. Chế độ hiệu năng (Cân bằng / Bảo vệ tối đa) — làm rõ hoặc bỏ

- Hiện đã có định nghĩa: `PERF-050b` (`08-performance-cpu-spec.md`) + `AdaptiveFrameRateCoordinator`:
  - **Cân bằng**: tần suất chụp thích ứng — nội dung tĩnh giãn tới ~5 giây/lần, nội dung đang thay đổi/nghi ngờ thì
    tăng lên ~1 giây/lần.
  - **Bảo vệ tối đa**: luôn ~1 giây/lần (`MaximumProtectionIntervalMs = 1000`), tốn CPU/pin hơn.
- Cần: chủ dự án quyết định **giữ (và ghi giải thích ngắn ngay dưới lựa chọn ở `S4`)** hay **bỏ setting** (cố định
  1 chế độ). Nếu bỏ → `PERF-050b` DEPRECATED, xoá `performance_mode` khỏi UI (giữ field IPC/config cho tương thích).

## 5. Cài đặt ngôn ngữ (Việt, Anh, Pháp, Tây Ban Nha, Bồ Đào Nha, Trung Quốc)

- Hiện tại: `FE-063` — Phase 1 chỉ tiếng Việt, chưa có UI chọn ngôn ngữ; hạ tầng `.resx` đã sẵn
  (`UiStrings.resx`, `OverlayStrings.resx`, `ResourceManager` theo `CurrentUICulture`).
- Cần: mục chọn ngôn ngữ trong `S4` + bản dịch `UiStrings.{en,fr,es,pt,zh-Hans}.resx` và
  `OverlayStrings.{...}.resx` (overlay/icon cũng phải đổi theo). Lưu lựa chọn ở `config.db` (Service), đẩy xuống
  Overlay qua IPC (giống `OverlayMessageUpdate`). Câu thông điệp overlay mặc định đổi theo ngôn ngữ (`FE-062`).
  Cần quyết định: Trung Quốc giản thể hay phồn thể; ai dịch/duyệt bản dịch.

## 6. Tab Giới thiệu: open source, link GitHub, giấy phép, ghi công mô hình AI

- Hiện tại: tab `S10` (`FE-090`/`FE-091`) — tên đơn vị/email/PayPal để trống.
- Cần thêm: ghi rõ **ứng dụng mã nguồn mở**, link `https://github.com/loiefy/ParentalGuard`, **giấy phép phân phối**,
  và **ghi công mô hình AI**: `GantMan/nsfw_model` (giấy phép MIT), đã chuyển sang ONNX — ghi tên tác giả, link gốc,
  giấy phép theo đúng yêu cầu MIT (kèm notice).
- Việc phải làm trước: **repo hiện CHƯA có file `LICENSE`** → chủ dự án chọn giấy phép (vd MIT / Apache-2.0 / GPL-3.0)
  rồi thêm `LICENSE` + `THIRD-PARTY-NOTICES.md` (mô hình AI, ONNX Runtime, Windows App SDK, CommunityToolkit…).

## 7. Xuất lịch sử ra báo cáo PDF

- Hiện tại: tab Lịch sử (`S3`) xem audit log, gate mật khẩu `view_audit_log`.
- Cần: nút "Xuất PDF" — chọn khoảng thời gian, lưu file qua hộp thoại Save. Lưu ý: không chứa ảnh (`IMG-0xx` — app
  không lưu ảnh), chỉ dữ liệu sự kiện; vẫn sau gate mật khẩu; hoàn toàn offline. Chọn thư viện PDF giấy phép phù hợp
  (vd QuestPDF — kiểm tra điều kiện license community, hoặc PdfSharp/MigraDoc MIT).

## 8. Chế độ "Bảo vệ chính phụ huynh"

- Cần: checkbox trong `S4` "Bảo vệ cả phụ huynh". Khi bật → hiện thông báo giải thích: dù có mật khẩu, phụ huynh
  cũng **không thể tạm dừng dễ dàng**. Khi bấm Tạm dừng + nhập đúng mật khẩu → phải **chơi 1 trò chơi và đạt đủ điểm**
  mới được tạm dừng giám sát.
- **Trò chơi: chưa chốt** — thảo luận và quyết định ở phiên sau (loại trò chơi, độ khó, điểm tối thiểu, giới hạn
  số lần thử/thời gian chờ).
- Điểm cần quyết định thêm: tắt chế độ này có cần chơi game không (nếu không → dễ bị lách); áp dụng cho cả Gỡ cài
  đặt/đổi whitelist hay chỉ Tạm dừng; tương tác với `PAUSE-021` (cảnh báo tạm dừng quá nhiều lần/ngày).

---

### Nợ kỹ thuật/ghi chú còn treo (từ trước)

- Watchdog chưa đăng ký service trên máy dev (cố ý — `E2E-TESTING.md` mục 3c) nên Dashboard luôn báo "Gián đoạn".
- `Architecture/04-data-architecture.md` ADR-127 còn ghi `capture_interval_baseline_ms("balanced")=2000ms` (code đã 5000ms).
- Đợt 9 (ROADMAP): GitHub Actions CI, SignPath signing (`SEC-030`), Dependabot, signed commits — chưa làm.
