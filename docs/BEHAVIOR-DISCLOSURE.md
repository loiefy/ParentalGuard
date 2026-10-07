# ParentalGuard — Bản công khai hành vi phần mềm (Behavior Disclosure)

> `MISC-070` — tài liệu này mô tả trung thực, đầy đủ hành vi kỹ thuật của ParentalGuard, dùng cho hai
> mục đích: (a) đính kèm khi báo cáo false-positive tới Microsoft Defender hoặc hãng antivirus khác nếu
> phần mềm bị nhận diện nhầm là mã độc; (b) công bố công khai trên trang dự án để tăng tính minh bạch,
> tin cậy. Viết cho người đọc không chuyên kỹ thuật (ví dụ nhân viên phân tích của hãng AV) lẫn người
> dùng kỹ thuật muốn tự kiểm chứng. Bản tiếng Anh đặt ngay dưới mỗi mục tiếng Việt.

## 1. ParentalGuard là gì / What is ParentalGuard

**Tiếng Việt**: ParentalGuard là phần mềm giám sát màn hình chạy trên Windows, mục đích duy nhất là
phát hiện và tự động che/chặn nội dung khiêu dâm hiển thị trên màn hình máy tính gia đình, nhằm bảo vệ
trẻ em. Đây là **dự án phần mềm nguồn mở, phi lợi nhuận, do cộng đồng phát triển** — không phải sản
phẩm thương mại, không thu thập dữ liệu người dùng vì mục đích kinh doanh.

**English**: ParentalGuard is a Windows screen-monitoring application whose sole purpose is to detect
and automatically obscure/block pornographic content displayed on a family computer's screen, in order
to protect children. This is an **open-source, non-commercial, community-developed project** — not a
commercial product, and it does not collect user data for business purposes.

## 2. Capture màn hình & xử lý ảnh / Screen capture & image processing

**Tiếng Việt**:
- ParentalGuard chụp lại khung hình màn hình định kỳ (mặc định 1-2 giây/lần) bằng Windows Desktop
  Duplication API (DXGI) — API chính thức của Microsoft dành cho phần mềm chụp màn hình hợp pháp
  (screen recorder, remote desktop, trình chiếu...).
- **Toàn bộ xử lý ảnh diễn ra cục bộ trên máy**, bằng 1 mô hình AI nhỏ (dựa trên kiến trúc MobileNetV2,
  chuyển đổi sang định dạng ONNX) chạy hoàn toàn offline.
- **Không bao giờ lưu ảnh chụp màn hình xuống đĩa** dưới bất kỳ hình thức nào (không file tạm, không
  cache, không log ảnh). Mọi buffer chứa pixel trong bộ nhớ (RAM/VRAM) đều bị ghi đè bằng 0 (zero-out)
  ngay sau khi dùng xong trong cùng 1 chu kỳ xử lý.
- **Không bao giờ gửi ảnh/dữ liệu pixel qua mạng** dưới bất kỳ hình thức nào.
- Tiến trình xử lý ảnh (`ParentalGuard.Vision.exe`) bị **chặn hoàn toàn khả năng kết nối mạng ở tầng hệ
  điều hành** (Windows Filtering Platform — cùng công nghệ nền tảng của Windows Firewall), áp dụng cho
  cả kết nối đi (outbound) lẫn đến (inbound), cả IPv4 lẫn IPv6 — không phải chỉ "tắt qua code", mà bị
  chặn cứng ở tầng OS ngay cả khi code có lỗi hoặc bị khai thác.
- Kết quả xử lý duy nhất được giữ lại là 1 con số điểm rủi ro (risk score) + toạ độ khung hình chữ nhật
  của vùng cần che — không có nội dung ảnh nào trong dữ liệu này.

**English**:
- ParentalGuard periodically captures the screen (default every 1–2 seconds) using the Windows Desktop
  Duplication API (DXGI) — Microsoft's official API intended for legitimate screen-capture software
  (screen recorders, remote desktop, presentation tools, etc.).
- **All image processing happens entirely on-device**, using a small AI model (MobileNetV2-based
  architecture, converted to ONNX format) running fully offline.
- **Screenshots are never written to disk** in any form (no temp files, no cache, no image logging).
  Every in-memory buffer holding pixel data (RAM/VRAM) is explicitly zeroed out immediately after use
  within the same processing cycle.
- **Pixel/image data is never sent over the network** in any form.
- The image-processing process (`ParentalGuard.Vision.exe`) has its network capability **completely
  blocked at the operating-system level** (Windows Filtering Platform — the same underlying technology
  as Windows Firewall), covering both outbound and inbound connections, IPv4 and IPv6 — this is not
  merely "disabled in code" but a hard OS-level block that remains in effect even if the code has a bug
  or is exploited.
- The only processing output that is retained is a numeric risk score plus the rectangular coordinates
  of the region to obscure — no image content is contained in this data.

## 2a. Buộc đóng ứng dụng vi phạm / Force-closing the offending application

**Tiếng Việt**:
- Khi nút **"Tắt nội dung"** trên lớp che được bấm, ParentalGuard yêu cầu cửa sổ đó đóng lại một cách bình thường.
- Nếu sau **3 giây** cửa sổ vẫn còn (ví dụ ứng dụng đang hỏi "Lưu thay đổi?" hoặc bỏ qua yêu cầu đóng), dịch vụ
  ParentalGuard **buộc kết thúc tiến trình** sở hữu cửa sổ đó. Dữ liệu **chưa lưu** của ứng dụng đó sẽ bị mất; với trình
  duyệt, toàn bộ cửa sổ/tab thuộc cùng tiến trình cũng đóng theo.
- Không bao giờ buộc đóng tiến trình hệ thống Windows (Explorer, tiến trình lõi), tiến trình không thuộc phiên người dùng,
  hay chính ParentalGuard. Việc tự động đóng do hết thời gian chờ **không** buộc kết thúc tiến trình. Mỗi lần buộc đóng
  (hoặc bị từ chối vì không đủ điều kiện an toàn) đều được ghi vào nhật ký.

**English**:
- When the **"Close content"** button on the overlay is pressed, ParentalGuard asks the window to close normally.
- If the window is still open **3 seconds** later (e.g. the app shows "Save changes?" or ignores the request), the
  ParentalGuard service **terminates the process** that owns the window. **Unsaved** data in that application is lost; for
  browsers, every window/tab belonging to the same process closes too.
- Windows system processes (Explorer, core processes), processes outside the user's session, and ParentalGuard itself are
  never terminated. The automatic close after the timeout never terminates a process. Every forced close (or refusal for
  safety reasons) is recorded in the event log.

## 3. Watchdog kép & chống gỡ cài đặt / Dual watchdog & anti-uninstall protection

**Tiếng Việt**:
- ParentalGuard chạy dưới dạng 2 Windows Service độc lập giám sát lẫn nhau (dual watchdog): nếu 1 bên
  bị dừng/gỡ bất thường, bên còn lại tự khởi động lại nó. Mục đích **duy nhất** của cơ chế này là ngăn
  trẻ tự tắt/gỡ phần mềm giám sát mà không có sự đồng ý của phụ huynh — **không phải cơ chế tự nhân
  bản, không lây lan sang máy khác, không có hành vi giống ransomware** (không mã hoá file người dùng,
  không đòi tiền chuộc, không khoá máy).
- ParentalGuard **không ẩn mình khỏi Task Manager** — mọi tiến trình (`ParentalGuard.Service.exe`,
  `ParentalGuard.Watchdog.exe`, `ParentalGuard.Vision.exe`, `ParentalGuard.Overlay.exe`,
  `ParentalGuard.UI.exe`) hiển thị bình thường với tên rõ ràng, có thể xem trong Task Manager/Process
  Explorer như mọi phần mềm hợp pháp khác.
- Có **giao diện Dashboard rõ ràng** (`ParentalGuard.UI.exe`) để phụ huynh xem trạng thái, tạm dừng
  giám sát có thời hạn (ví dụ khi cần dùng máy cho việc chính đáng), xem lịch sử, đổi cài đặt.
- Có **cơ chế gỡ cài đặt hợp pháp** qua Windows "Add/Remove Programs" chuẩn, yêu cầu xác thực bằng mật
  khẩu ParentalGuard hoặc Recovery Key đã lưu — không phải "không thể gỡ được", chỉ là gỡ cần đúng
  người có quyền (phụ huynh), tương tự phần mềm diệt virus doanh nghiệp hay phần mềm quản lý thiết bị
  (MDM) hợp pháp khác cũng yêu cầu xác thực trước khi gỡ.

**English**:
- ParentalGuard runs as two independent Windows Services that monitor each other (dual watchdog): if
  one is stopped/removed unexpectedly, the other restarts it. The **sole purpose** of this mechanism is
  to prevent a child from disabling/uninstalling the monitoring software without a parent's consent —
  **it is not a self-replication mechanism, does not spread to other machines, and exhibits no
  ransomware-like behavior** (no file encryption, no ransom demand, no screen/device lock-out).
- ParentalGuard **does not hide itself from Task Manager** — every process
  (`ParentalGuard.Service.exe`, `ParentalGuard.Watchdog.exe`, `ParentalGuard.Vision.exe`,
  `ParentalGuard.Overlay.exe`, `ParentalGuard.UI.exe`) is visible under its plain, descriptive name in
  Task Manager/Process Explorer like any other legitimate software.
- There is a **clear Dashboard UI** (`ParentalGuard.UI.exe`) for parents to view status, temporarily
  pause monitoring for a set duration (e.g., when the computer is needed for a legitimate task), review
  history, and change settings.
- There is a **legitimate uninstall path** via the standard Windows "Add/Remove Programs" flow, gated
  by the ParentalGuard password or a saved Recovery Key — this is not "impossible to uninstall", it
  simply requires the correct authorized person (the parent), the same way enterprise antivirus or
  legitimate MDM (mobile device management) software also requires authentication before removal.

## 4. Zero network / zero telemetry / zero auto-update / mã nguồn mở

**Tiếng Việt**:
- **Zero network**: ngoài giao tiếp nội bộ giữa các tiến trình của chính ParentalGuard trên cùng 1 máy
  (Named Pipe — cơ chế liên lạc nội bộ tiêu chuẩn của Windows, không đi qua mạng), phần mềm **không mở
  bất kỳ kết nối mạng nào ra Internet hay mạng nội bộ** — không có API server, không có cloud backend,
  không đồng bộ dữ liệu lên bất kỳ đâu.
- **Zero telemetry**: không thu thập, không gửi bất kỳ dữ liệu sử dụng, dữ liệu chẩn đoán, hay thống kê
  nào ra bên ngoài máy. Thư viện AI runtime (ONNX Runtime) được cấu hình tường minh để **tắt hẳn** tính
  năng telemetry mặc định của chính nó.
- **Zero auto-update**: ParentalGuard **không tự động tải/cài bản cập nhật** từ bất kỳ máy chủ nào —
  người dùng phải tự tải bản cài đặt mới và cài đè thủ công khi muốn nâng cấp.
- **Mã nguồn mở**: toàn bộ mã nguồn công khai, ai cũng có thể tự đọc, tự kiểm chứng, tự build lại từ mã
  nguồn để xác nhận bản build không khác gì so với mã nguồn công khai.

**English**:
- **Zero network**: aside from communication between ParentalGuard's own processes on the same
  machine (Named Pipes — a standard Windows local IPC mechanism, not network traffic), the software
  **does not open any network connection** to the Internet or a local network — there is no API
  server, no cloud backend, and no data is synced anywhere.
- **Zero telemetry**: no usage data, diagnostic data, or statistics of any kind are collected or sent
  off the machine. The AI runtime library (ONNX Runtime) is explicitly configured to **fully disable**
  its own default telemetry feature.
- **Zero auto-update**: ParentalGuard **does not automatically download/install updates** from any
  server — users must manually download and reinstall a new build whenever they want to upgrade.
- **Open source**: the entire source code is public; anyone can read it, audit it, and rebuild it from
  source themselves to confirm that a distributed build matches the public source code.

## 5. Liên hệ / báo cáo false-positive — Contact / false-positive reporting

**Tiếng Việt**: _(placeholder — chủ dự án tự điền thông tin liên hệ chính thức: email, trang GitHub
issue tracker, hoặc form báo cáo trước khi công bố tài liệu này công khai)_

- Trang dự án: `<điền URL repository/GitHub>`
- Email liên hệ: `<điền email liên hệ>`
- Báo cáo lỗi/false-positive: `<điền đường dẫn GitHub Issues hoặc kênh báo cáo khác>`

**English**: _(placeholder — project owner to fill in official contact details: email, GitHub issue
tracker link, or a report form before publishing this document publicly)_

- Project page: `<fill in repository/GitHub URL>`
- Contact email: `<fill in contact email>`
- Bug/false-positive reports: `<fill in GitHub Issues link or other reporting channel>`
