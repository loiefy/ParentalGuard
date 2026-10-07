# Việc cần làm — phiên làm việc tiếp theo

> Ghi lại: 2026-10-01 (cuối phiên); cập nhật 2026-10-05: mục 2, 8, 10–16 đã làm (Spec `APPROVED v0.9.0`, 547 test pass).
> **Cập nhật 2026-10-07: TẤT CẢ 16 mục đã làm** (Spec `APPROVED v0.13.1`, 601/601 test pass tại máy và trên CI). Chỉ còn: chủ dự án
> chọn giấy phép dự án (mục 6), ký số bản build (SignPath `SEC-030` — chủ dự án để lại), người bản ngữ duyệt bản dịch (mục 5). Trạng thái lúc dừng: Spec `APPROVED v0.8.8`, commit `3bde5ad`, 510/510 test pass,
> bản mới nhất đã cài ở `C:\Program Files\ParentalGuard`. Mỗi việc dưới đây là **quyết định sản phẩm (WHAT)** →
> phải cập nhật `Specification/` đúng quy trình archive (CLAUDE.md) trước/cùng lúc với code.

## 1. Theo dõi tối đa 10 cửa sổ cùng lúc (thay vì 4) — ✅ ĐÃ LÀM 2026-10-07 (`BE-071c`)

- Hiện tại: `BE-071a`/`BE-071b` — trần **4 cửa sổ/chu kỳ** (`CandidateWindowSelector.MaxCandidatesPerCycle = 4`),
  foreground mỗi chu kỳ + 3 suất xoay vòng.
- Cần: nâng lên **10**. Đổi spec `BE-071a`/`PERF-020a`/`IMG-020a` (trần), cân nhắc CPU: mỗi cửa sổ = 1 lần
  capture + (nếu nội dung đổi) 1 lần inference → đo lại CPU với 10 cửa sổ trên máy thật (`PERF-0xx`).
- Lưu ý liên quan: ngưỡng chế độ overlay gộp `BE-088` cũng là 10 overlay.

## 2. Nút cài đặt (bánh răng) trên cửa sổ blur nhỏ lại ~1/2 — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- Hiện tại: `ContentBlurOverlayForm` — nút `\uE713` font Segoe MDL2 14pt, `Padding = 6`, `AutoSize` (`FE-016i`).
- Cần: thu nhỏ còn khoảng bằng kích thước icon (ước lượng giảm 1 nửa): font ~10–11pt, padding ~2, vùng bấm vẫn
  đủ dễ bấm.

## 3. Nút "Tắt nội dung" phải TẮT ĐƯỢC ứng dụng vi phạm (force) — ✅ ĐÃ LÀM 2026-10-07 (`BE-034d`: 3 giây sau WM_CLOSE, Service kiểm tra an toàn rồi mới kết thúc tiến trình)

- Hiện tại: `BE-032`/`BE-089a` — Overlay chỉ `PostMessage(WM_CLOSE)` lên cửa sổ vi phạm; nhiều app (trình duyệt
  nhiều tab, app hỏi "Lưu thay đổi?") **không đóng**. `BE-034c` hiện ghi rõ "**Không kill tiến trình**".
- Cần: force tắt được app vi phạm → đổi spec (supersede phần "không kill" của `BE-034c`, cập nhật `BE-032`).
  Hướng đề xuất: WM_CLOSE trước, sau N giây nếu cửa sổ vẫn còn → `TerminateProcess` tiến trình sở hữu cửa sổ.
- Điểm cần quyết định khi làm: kill ai làm? `Overlay` chạy **Low IL** → không đủ quyền terminate tiến trình Medium IL
  của user → nên để `Service` (SYSTEM) kill theo PID, có kiểm tra an toàn (không kill tiến trình hệ thống/
  `explorer.exe`/chính ParentalGuard, PID phải đúng chủ cửa sổ vi phạm). Ghi audit log. Cảnh báo mất dữ liệu chưa lưu
  của app bị kill (ghi rõ trong `docs/BEHAVIOR-DISCLOSURE.md`).

## 4. Chế độ hiệu năng (Cân bằng / Bảo vệ tối đa) — làm rõ hoặc bỏ — ✅ ĐÃ LÀM 2026-10-07 (`PERF-050c`: giữ cả 2, thêm dòng giải thích)

- Hiện đã có định nghĩa: `PERF-050b` (`08-performance-cpu-spec.md`) + `AdaptiveFrameRateCoordinator`:
  - **Cân bằng**: tần suất chụp thích ứng — nội dung tĩnh giãn tới ~5 giây/lần, nội dung đang thay đổi/nghi ngờ thì
    tăng lên ~1 giây/lần.
  - **Bảo vệ tối đa**: luôn ~1 giây/lần (`MaximumProtectionIntervalMs = 1000`), tốn CPU/pin hơn.
- Cần: chủ dự án quyết định **giữ (và ghi giải thích ngắn ngay dưới lựa chọn ở `S4`)** hay **bỏ setting** (cố định
  1 chế độ). Nếu bỏ → `PERF-050b` DEPRECATED, xoá `performance_mode` khỏi UI (giữ field IPC/config cho tương thích).

## 5. Cài đặt ngôn ngữ (Việt, Anh, Pháp, Tây Ban Nha, Bồ Đào Nha, Trung Quốc) — ✅ ĐÃ LÀM 2026-10-07 (`FE-063a`: tiếng Trung giản thể; bản dịch do AI tạo, CHƯA có người bản ngữ duyệt)

- Hiện tại: `FE-063` — Phase 1 chỉ tiếng Việt, chưa có UI chọn ngôn ngữ; hạ tầng `.resx` đã sẵn
  (`UiStrings.resx`, `OverlayStrings.resx`, `ResourceManager` theo `CurrentUICulture`).
- Cần: mục chọn ngôn ngữ trong `S4` + bản dịch `UiStrings.{en,fr,es,pt,zh-Hans}.resx` và
  `OverlayStrings.{...}.resx` (overlay/icon cũng phải đổi theo). Lưu lựa chọn ở `config.db` (Service), đẩy xuống
  Overlay qua IPC (giống `OverlayMessageUpdate`). Câu thông điệp overlay mặc định đổi theo ngôn ngữ (`FE-062`).
  Cần quyết định: Trung Quốc giản thể hay phồn thể; ai dịch/duyệt bản dịch.

## 6. Tab Giới thiệu: open source, link GitHub, giấy phép, ghi công mô hình AI — ✅ ĐÃ LÀM 2026-10-07 (`FE-093`; ghi công Marqo; `THIRD-PARTY-NOTICES.md`) — ⏳ CÒN: chủ dự án chọn giấy phép dự án để thêm `LICENSE` (tab Giới thiệu đang hiện "(đang cập nhật)")

- Hiện tại: tab `S10` (`FE-090`/`FE-091`) — tên đơn vị/email/PayPal để trống.
- Cần thêm: ghi rõ **ứng dụng mã nguồn mở**, link `https://github.com/loiefy/ParentalGuard`, **giấy phép phân phối**,
  và **ghi công mô hình AI**: `GantMan/nsfw_model` (giấy phép MIT), đã chuyển sang ONNX — ghi tên tác giả, link gốc,
  giấy phép theo đúng yêu cầu MIT (kèm notice).
- Việc phải làm trước: **repo hiện CHƯA có file `LICENSE`** → chủ dự án chọn giấy phép (vd MIT / Apache-2.0 / GPL-3.0)
  rồi thêm `LICENSE` + `THIRD-PARTY-NOTICES.md` (mô hình AI, ONNX Runtime, Windows App SDK, CommunityToolkit…).

## 7. Xuất lịch sử ra báo cáo PDF — ✅ ĐÃ LÀM 2026-10-07 (`MISC-011`, PDFsharp MIT)

- Hiện tại: tab Lịch sử (`S3`) xem audit log, gate mật khẩu `view_audit_log`.
- Cần: nút "Xuất PDF" — chọn khoảng thời gian, lưu file qua hộp thoại Save. Lưu ý: không chứa ảnh (`IMG-0xx` — app
  không lưu ảnh), chỉ dữ liệu sự kiện; vẫn sau gate mật khẩu; hoàn toàn offline. Chọn thư viện PDF giấy phép phù hợp
  (vd QuestPDF — kiểm tra điều kiện license community, hoặc PdfSharp/MigraDoc MIT).

## 8. sửa lại background — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)
- chỗ nội dung các page chính ở dashboard, background nên thể hiện màu gradient, ở phía trên sáng hơn 1 chút, xuống dưới thì tối dần, nhưng sự khác biệt sáng tối chỉ vừa đủ để nhận biết, không được quá khác biệt

## 9. Chế độ "Bảo vệ chính phụ huynh" — ✅ ĐÃ LÀM 2026-10-07 (`PAUSE-040`–`043`: thử thách 5 phép tính/60 giây thay cho "trò chơi"; tắt chế độ cũng phải vượt thử thách)

- Cần: checkbox trong `S4` "Bảo vệ cả phụ huynh". Khi bật → hiện thông báo giải thích: dù có mật khẩu, phụ huynh
  cũng **không thể tạm dừng dễ dàng**. Khi bấm Tạm dừng + nhập đúng mật khẩu → phải **chơi 1 trò chơi và đạt đủ điểm**
  mới được tạm dừng giám sát.
- **Trò chơi: chưa chốt** — thảo luận và quyết định ở phiên sau (loại trò chơi, độ khó, điểm tối thiểu, giới hạn
  số lần thử/thời gian chờ).
- Điểm cần quyết định thêm: tắt chế độ này có cần chơi game không (nếu không → dễ bị lách); áp dụng cho cả Gỡ cài
  đặt/đổi whitelist hay chỉ Tạm dừng; tương tác với `PAUSE-021` (cảnh báo tạm dừng quá nhiều lần/ngày).

## 10. Dashboard lần đầu chạy: nêu bật giá trị cốt lõi của app — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- Khi Dashboard mở lần đầu trên máy (sau Onboarding `S1`), hiển thị **khối giới thiệu nổi bật, dễ nhìn**, gồm 5 điểm:
  1. Bảo vệ trẻ em khỏi nội dung nhạy cảm
  2. Bảo mật tuyệt đối
  3. Không lưu dữ liệu trên máy, không chia sẻ bất kỳ dữ liệu nào ra ngoài hoặc lên Internet
  4. Có cơ chế bảo vệ cả phụ huynh (liên quan mục 9)
  5. Mã nguồn mở (liên quan mục 6)
- Mỗi điểm dạng thẻ (icon + tiêu đề ngắn), **có hiệu ứng khi rê chuột** (sáng nhẹ/nổi lên).
- **ĐÃ CHỐT (2026-10-05):** khối này là màn hình chào **khi app chưa từng có mật khẩu** (lần chạy đầu, trước/cùng
  Onboarding `S1`). Đã tạo mật khẩu rồi thì **không bao giờ hiển thị lại** → điều kiện hiển thị = "chưa có mật khẩu"
  (đọc từ Service), không cần cờ "đã xem" riêng.
- Mỗi thẻ gồm **tiêu đề** (chữ đậm) + **1 câu mô tả ngắn** (chữ nhỏ hơn) bên dưới. Văn phong: **văn viết**, trang
  trọng, không dùng khẩu ngữ. Không dùng "không lưu dữ liệu"/"tuyệt đối" (sai sự thật hoặc cam kết quá mức).
- **ĐÃ CHỐT câu chữ (chủ dự án phê duyệt 2026-10-05):**
  1. **Bảo vệ trẻ em khỏi nội dung nhạy cảm** — Tự động nhận diện và che phủ nội dung khiêu dâm theo thời gian thực.
  2. **Chỉ phụ huynh mới có quyền gỡ ứng dụng** *(chủ dự án chốt tiêu đề)* — Việc gỡ cài đặt, tạm dừng giám sát và
     thay đổi thiết lập đều yêu cầu mật khẩu của phụ huynh.
  3. **Xử lý hoàn toàn trên thiết bị** — Không lưu trữ hình ảnh, âm thanh hay nội dung hiển thị trên màn hình. Không
     kết nối Internet, không chia sẻ dữ liệu với bên thứ ba.
  4. **Bảo vệ cả phụ huynh** — Tùy chọn bổ sung bước xác minh khi tạm dừng giám sát, áp dụng cho cả người lớn.
     *(Chỉ hiển thị khi mục 9 đã hoàn thành.)*
  5. **Mã nguồn mở** — Mã nguồn được công khai, cho phép bất kỳ ai kiểm chứng cách ứng dụng hoạt động.
- Spec: thêm `FE-0xx` mới ở `03-frontend-ui-spec.md` (`S2`).

## 11. Onboarding (`S1`): giải thích vì sao phải đặt mật khẩu — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- Ở bước đặt mật khẩu lần đầu, thêm đoạn giải thích ngắn gọn, rõ ràng, ví dụ:
  "Mật khẩu này giúp ngăn trẻ tự ý tắt, tạm dừng hoặc gỡ ParentalGuard khi chưa có sự cho phép của người lớn.
  Hãy giữ kín mật khẩu."
- Spec: bổ sung mục `S1` (`03-frontend-ui-spec.md` mục 3.3) / có thể liên kết `PWD-00x`.

## 12. Tab Cài đặt (`S4`): panel đăng nhập, khoá toàn bộ khi chưa đăng nhập — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- Hiện tại: mỗi thao tác nhạy cảm trong `S4` hỏi mật khẩu riêng (gate theo từng thao tác, `S5`).
- Cần: đầu tab Cài đặt có **panel đăng nhập** (ô mật khẩu + nút Đăng nhập + link **"Quên mật khẩu?"** dẫn sang luồng
  Recovery `PWD-032`).
  - **Chưa đăng nhập**: toàn bộ phần cài đặt **bị làm mờ (greyed), không thao tác được**, nhưng **vẫn cuộn được** để
    xem hết nội dung.
  - **Đã đăng nhập**: ẩn panel đăng nhập, bỏ làm mờ, thao tác tự do (không hỏi lại mật khẩu từng thao tác).
- **Ngoại lệ:** mục **Ngôn ngữ** không cần đăng nhập (xem mục 13).
- **ĐÃ CHỐT (2026-10-05):**
  - **1 phiên đăng nhập dùng chung cho cả tab Lịch sử và tab Cài đặt** (tab Lịch sử khi chưa đăng nhập cũng hiện panel
    đăng nhập thay cho gate `view_audit_log` hiện tại).
  - Phiên **tự hết hạn sau 10 phút không thao tác**.
  - Có nút "Đăng xuất"; đóng Dashboard cũng kết thúc phiên.
  - Tạm dừng giám sát và Gỡ cài đặt **vẫn hỏi mật khẩu riêng** như hiện tại (không nằm trong phạm vi phiên này).
  - Sai mật khẩu nhiều lần: dùng lại cơ chế khoá tạm hiện có của `PWD-0xx`.
- Kỹ thuật: Service (SYSTEM) vẫn phải xác thực mỗi lệnh đổi cài đặt (UI chạy quyền user, không tin trạng thái
  "đã đăng nhập" phía UI) → cần token phiên do Service cấp, có hạn → kiểm tra với `SEC-0xx`/`04-security-spec.md`,
  có thể phải sửa `Architecture/` (IPC). Chạy security-privacy-auditor sau khi làm.
- Spec: đổi `S4`/`S5` ở `03-frontend-ui-spec.md`, có thể `06-password-management-spec.md` → archive đúng quy trình.

## 13. Mục Ngôn ngữ: không cần đăng nhập, hiển thị song ngữ — ✅ ĐÃ LÀM (giao diện 2026-10-05; cả 6 ngôn ngữ chọn được từ 2026-10-07)

- Bổ sung cho mục 5: mục chọn ngôn ngữ trong `S4` **không bị gate** (đổi được khi chưa đăng nhập, không bị làm mờ).
- Nhãn mục và tên các ngôn ngữ hiển thị **song ngữ**: ngôn ngữ đang dùng + tiếng Anh, ví dụ
  "Ngôn ngữ / Language", "Tiếng Việt (Vietnamese)", "Tiếng Pháp (French)"… Nếu ngôn ngữ đang dùng là tiếng Anh thì
  **chỉ hiện tiếng Anh**.
- Ngôn ngữ không cần đăng nhập ở **cả** tab Cài đặt (vẫn sáng, không bị làm mờ khi các mục khác bị khoá).
- Gợi ý: tên mỗi ngôn ngữ nên kèm cả tên gốc ("Français", "Español", "中文") để người không đọc được ngôn ngữ hiện
  tại vẫn tìm được ngôn ngữ của mình — cần chủ dự án xác nhận.
- Lý do bỏ gate: đổi ngôn ngữ không làm giảm khả năng bảo vệ. Lưu ý: lệnh IPC đổi ngôn ngữ phải là lệnh riêng,
  Service chỉ cho phép đổi đúng trường ngôn ngữ khi không có phiên đăng nhập.

## 14. Tab Tổng quan: trạng thái tổng rõ ràng khi đang bảo vệ — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- Ở khối trạng thái tổng: nếu Vision đang chạy bình thường → **icon dấu tích màu xanh lá** + dòng chữ rõ ràng
  **"Máy tính đang được bảo vệ"**.
- Các trạng thái khác (tạm dừng / gián đoạn / lỗi) giữ hiển thị như hiện tại nhưng cần có icon + màu tương ứng
  (vàng/đỏ) để đối lập rõ với trạng thái xanh.
- **ĐÃ CHỐT (2026-10-05):** trạng thái xanh "Máy tính đang được bảo vệ" chỉ cần **Vision** (nhận diện) **và Overlay**
  (tiến trình hiển thị lớp che khi phát hiện vi phạm) đang chạy, giám sát không bị tạm dừng. **Không** phụ thuộc
  Watchdog (Watchdog lỗi thì hiển thị cảnh báo phụ, không làm mất trạng thái xanh).

## 15. Biểu đồ số lần chặn theo ngày: mở rộng tới 6 tháng — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- Hiện tại: `FE-071` — chuyển khoảng xem 7 ngày / 30 ngày.
- Cần: nút chọn nhanh **1 tuần / 1 tháng / 3 tháng / 6 tháng** (tối đa 6 tháng ≈ 180 ngày).
- **ĐÃ CHỐT (2026-10-05):** 1 tuần / 1 tháng → cột theo **ngày**; 3 tháng / 6 tháng → cột gộp theo **tuần**.
- Đã kiểm tra code (2026-10-05): `audit.log` (JSONL hash-chain, `AuditLogWriter`) **không có cơ chế xoay vòng/xoá**
  → dữ liệu 180 ngày có sẵn. Vẫn ghi vào spec yêu cầu "giữ tối thiểu 180 ngày" để sau này không ai thêm cơ chế xoá
  ngắn hơn.
- Spec: `FE-071` → requirement mới supersede khoảng xem.

## 16. Mục "Cách ứng dụng hoạt động & dữ liệu được lưu" — ✅ ĐÃ LÀM 2026-10-05 (Spec v0.9.0)

- **ĐÃ CHỐT (2026-10-05):** đặt **trong tab Giới thiệu** (`S10`), không tạo tab riêng.
- Nội dung (văn viết, cùng văn phong với mục 10):
  - App xem màn hình **ngay trên máy**, dùng mô hình AI chạy offline để nhận diện nội dung nhạy cảm, phát hiện thì che
    mờ cửa sổ đó.
  - **Không lưu** hình ảnh, âm thanh hay nội dung người dùng xem; **không kết nối Internet**; **không chia sẻ** bất kỳ
    dữ liệu nào ra ngoài.
  - Dữ liệu lưu trên máy — **ghi đầy đủ đúng thực tế** (đã đối chiếu code 2026-10-05):
    - **Nhật ký sự kiện** (chỉ siêu dữ liệu — metadata, có chuỗi băm chống chỉnh sửa):
      - Thời điểm phát hiện vi phạm, tên ứng dụng (tiến trình) và điểm tin cậy của AI (`ContentBlocked`)
      - Lịch sử tạm dừng / tiếp tục giám sát (`PauseActivated`/`PauseResumed`)
      - Đăng nhập thành công/thất bại, thay đổi cài đặt (`AuthAttempt`, `ConfigChanged`)
      - Sự kiện an ninh: dấu hiệu can thiệp/gỡ cài đặt, khởi động lại dịch vụ (`TamperDetected`,
        `AttackPatternDetected`, `UninstallInitiated`, `ServiceStarted`, `ProcessRestarted`…)
    - **Cấu hình**: các cài đặt, danh sách whitelist; mật khẩu chỉ lưu dạng **băm một chiều** (không thể đọc ngược).
  - Có thể nêu thêm: tiến trình nhận diện bị **chặn mạng ở tầng tường lửa Windows (WFP)** — bằng chứng kỹ thuật cho
    cam kết "không kết nối Internet".
- Đồng bộ với `docs/BEHAVIOR-DISCLOSURE.md`. Kiểm tra "không kết nối Internet" đúng với cả bản build cuối
  (không telemetry, không kiểm tra cập nhật tự động).
- Spec: thêm `FE-0xx`/`MISC-0xx` mới.

---

### Nợ kỹ thuật/ghi chú còn treo (từ trước)

- Watchdog chưa đăng ký service trên máy dev (cố ý — `E2E-TESTING.md` mục 3c) nên Dashboard luôn báo "Gián đoạn".
- ✅ 2026-10-07: `Architecture/04` ADR-127 đã sửa thành 5000 ms (v0.5.3).
- ✅ 2026-10-07: GitHub Actions CI (`.github/workflows/ci.yml` — build + test bắt buộc; `dotnet format` mới chỉ báo cáo vì còn
  nhiều cảnh báo IDE1006 cũ: quy tắc đặt tên `.editorconfig` đang bắt cả hằng `const` phải có tiền tố `_`) và Dependabot
  (`.github/dependabot.yml`, NuGet + Actions hằng tuần).
- ⏳ Signed commits (`DEV-004`): cần chủ dự án tự tạo khoá GPG/SSH trên máy và bật trong GitHub — không làm thay được.
- ⏳ Ký số bản build (SignPath, `SEC-030`): chủ dự án để lại.
- Tối ưu CPU (2026-10-07, đã đo, KHÔNG áp dụng): gom 6 vùng con thành 1 lần suy luận batch=6 cho Marqo (export lại có trục
  batch động, kết quả trùng khớp < 2e-7). Trên GPU (DirectML) của máy chủ dự án: 6×batch1 = 339 ms vs 1×batch6 = 345 ms —
  không lợi; chỉ lợi ~20% ở chế độ CPU dự phòng (1017 → 798 ms). Không đáng đổi file model + checksum. Hướng khác nếu cần
  giảm tải: bỏ qua 5 vùng con khi điểm cả cửa sổ rất thấp (đã có mức sàn 0.10), hoặc giảm tần suất chấm vùng động.
- Lịch sử git (2026-10-07): 2 file lỡ commit (`launchSettings.json`, `tests/Log/logCantOpenUI`) đã xem lại — chỉ có đường dẫn
  cài đặt chuẩn và stack trace, KHÔNG có bí mật → không viết lại lịch sử (force-push đổi SHA mọi commit, rủi ro hơn lợi).
  `launchSettings.json` bị commit lại lần 2 ở `671a3c7` → đã gỡ và thêm `.gitignore`. Nếu chủ dự án vẫn muốn xoá hẳn khỏi
  lịch sử: `git filter-repo --invert-paths --path <file>` rồi force-push.
