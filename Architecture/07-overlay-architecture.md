# 07 — Overlay Architecture (vùng loại trừ, đa cửa sổ/z-index/gộp, multi-monitor)

> Version: v0.2.2 | Trạng thái: Draft | Cập nhật: 2026-09-20

## 0. Ghi chú tổ chức tài liệu (vì sao có file này, vì sao đánh số `07`)

`ROADMAP.md` mục 3 (bảng domain) ghi Đợt 2 (Overlay hoàn chỉnh) phụ thuộc `Architecture/02`, `03`, `08` — con số này viết **trước khi** `Architecture/00-INDEX.md` chốt tên file cụ thể cho từng số, và `08` sau đó bị gán cho `08-ui-architecture.md` (kiến trúc Dashboard WinUI 3, Đợt 6) — hoàn toàn khác `ParentalGuard.Overlay` (WinForms thuần, đã code thật ở Đợt 1, xem `src/ParentalGuard.Overlay/`). Đây là sai lệch câu chữ ở `ROADMAP.md`, không phải chỉ dẫn cứng phải nhồi nội dung Overlay vào file `08`.

Quyết định tổ chức tài liệu (thuộc thẩm quyền `architecture-writer`, không phải quyết định sản phẩm): nội dung Đợt 2 (vùng loại trừ 3 lớp, đa cửa sổ/z-index/gộp, multi-monitor phía Overlay/Service) đủ lớn và đủ khác biệt để xứng đáng **1 file riêng**, không nhồi vào `02-process-architecture.md` (vốn chỉ mô tả lifecycle/state machine cấp cao, không phải chi tiết rendering) — tạo file mới, đánh số `07` (chèn ngay sau `06-security-architecture.md`, trước 4 file "chưa viết" còn lại), dồn `07-anti-tamper-architecture.md`→`08`, `08-ui-architecture.md`→`09`, `09-deployment-release-architecture.md`→`10`, `10-dev-automation-architecture.md`→`11`. Vì 4 file đó **chưa từng được viết** (xác nhận qua `Architecture/00-INDEX.md` cột trạng thái + không có file thật trên đĩa), việc dồn số chỉ là sửa tên trong bảng mục lục + vài dòng tham chiếu bằng số ở `02`/`03`/`06` (đã sửa cùng lượt, xem changelog 3 file đó), không có nội dung thật nào bị mất/phải archive. Đầy đủ lý do + danh sách file bị ảnh hưởng: `Architecture/00-INDEX.md` mục 6 (changelog).

## 1. Mục đích và phạm vi

File này trả lời **HOW** cho phần còn lại của Overlay/Service chưa thiết kế ở `02-process-architecture.md` (vốn chỉ nói "Overlay là hàm render thuần theo danh sách rect", không đi vào chi tiết dựng rect/quản lý nhiều instance) và `03-ipc-communication.md` (vốn chỉ định nghĩa schema on-the-wire, không định nghĩa thuật toán 2 đầu dùng schema đó):

- **Vùng loại trừ nút đóng 3 lớp** (`FE-016`, `FE-016a`–`FE-016e`) — mục 2.
- **Đa cửa sổ vi phạm đồng thời**: dựng danh sách phía `Service` (`BE-084`–`086`, đã có sẵn từ code Đợt 1), z-index khớp cửa sổ gốc (`BE-087`), giới hạn 10 overlay + chế độ gộp (`BE-088`/`089`) — mục 3.
- **Multi-monitor phía Overlay/Service**: icon trạng thái mỗi màn hình (`BE-083`), DPI, hot-plug — mục 4. Phần capture/chọn cửa sổ phía `Vision` (`BE-080`–`082`, `IMG-020`) đã thiết kế ở `05-image-pipeline-architecture.md` mục 3.5 (Đợt 2), không lặp lại ở đây.

Không phát minh yêu cầu sản phẩm mới. Toàn bộ quyết định ở đây phải trích được về Requirement ID trong `Specification/` hoặc là ADR thuần kỹ thuật (mục 5).

**Cập nhật v0.2.0**: bản v0.1.0 của file này từng flag mục 3.4 ("`FE-016` đình chỉ ở chế độ gộp") là **suy luận kỹ thuật đang chờ chủ dự án phủ quyết**, chưa có căn cứ tường minh trong `Specification/`. Chủ dự án đã xác nhận trực tiếp qua `spec-maintainer` (2026-09-19): `Specification/02-backend-spec.md` thêm `BE-088a`/`BE-089a`/`BE-089b` (v0.13.0) và `Specification/03-frontend-ui-spec.md` thêm `FE-016f`/`FE-016g` (v0.9.0) — full-screen lock ở chế độ gộp nay là **quyết định sản phẩm đã CHỐT**, không còn là suy luận kiến trúc chờ duyệt. Mục 3.4 dưới đây viết lại hoàn toàn theo nội dung đã chốt này (thay thế toàn bộ nội dung "flagged for review" của v0.1.0). Đồng thời, phiên này cũng mở rộng đầy đủ UX icon trạng thái multi-monitor (`FE-020`–`022`) ở mục 4 — phạm vi trước đây bị giới hạn ở "chỉ cấu trúc" (ADR-62 cũ) nay được chủ dự án quyết định làm luôn ở Đợt 2 (xem mục 4.1).

Mã nguồn tham chiếu (Đợt 1, đã qua sandbox gate — xem `ROADMAP.md` mục 5): `src/ParentalGuard.Overlay/Rendering/OverlayCoordinator.cs`, `src/ParentalGuard.Overlay/Rendering/ContentBlurOverlayForm.cs`, `src/ParentalGuard.Overlay/Windows/OverlayWindowInterop.cs`, `src/ParentalGuard.Service/Ipc/OverlayDecisionCoordinator.cs`. Thiết kế ở file này là phần **mở rộng** các class đó, không phải viết lại — code Đợt 1 đã dùng đúng `Dictionary<ulong windowHandle, ...>` (không phải 1 field đơn), nên `BE-084`–`086` (mỗi cửa sổ 1 overlay, xử lý độc lập) thực chất **đã hiện thực hoá cấu trúc dữ liệu đúng từ Đợt 1** — phần còn thiếu chỉ là (a) vùng loại trừ, (b) z-index, (c) ngưỡng 10 + gộp, (d) multi-monitor, đúng 4 việc file này giải quyết.

## 2. Vùng loại trừ nút đóng 3 lớp (`FE-016c`)

### 2.1 Nguyên tắc: overlay hiển thị ngay, vùng loại trừ nâng cấp dần (không chặn hiển thị)

Đúng tinh thần `FE-016c` lớp 3 ("đảm bảo overlay không bao giờ phải chờ UI Automation mà trễ việc che nội dung"): `ContentBlurOverlayForm` **luôn** dựng vùng loại trừ bằng lớp 3 (fallback cố định, mục 2.4) **ngay lập tức, đồng bộ** khi `Show()` — không có đường code nào chờ UI Automation trước khi hiển thị overlay lần đầu. Lớp 1 (UI Automation, mục 2.2) chạy **song song, bất đồng bộ**, ngân sách 150ms (`FE-016c` lớp 1, đã "ĐÃ CHỐT CHÍNH THỨC v0.4.2"); nếu trả kết quả hợp lệ trong ngân sách, vùng loại trừ được **nâng cấp** (thay `Region` hiện tại bằng vùng chính xác hơn từ lớp 1+2, mục 2.3) — nếu overlay đã hiển thị được vài trăm ms bằng vùng fallback trước khi nâng cấp, đó là hành vi chấp nhận được (không vi phạm `FE-016`, vì nút đóng gốc **vẫn luôn nằm trong** vùng fallback 160×50 ở mọi thời điểm — mục 2.4 giải thích tại sao).

```
ContentBlurOverlayForm.Show(rect)
   ├─▶ [đồng bộ, ngay lập tức] ApplyExclusionRegion(FallbackRect(rect, dpiScale))   // lớp 3, luôn chạy trước
   └─▶ [bất đồng bộ, nền] StartUiAutomationLookup(windowHandle, timeout=150ms)
              │ (chạy trên Thread STA riêng — mục 2.2)
              ▼ nếu tìm được trong 150ms
        ApplyExclusionRegion(PadRect(uiaResult, padding=16px·dpiScale))            // lớp 1+2, ghi đè lớp 3
```

### 2.2 Lớp 1 — UI Automation

- **API**: COM `IUIAutomation` (raw interop qua `UIAutomationClient.dll`, `CUIAutomation` CLSID) — không dùng managed `System.Windows.Automation` (kéo theo assembly WPF `UIAutomationClient`/`UIAutomationTypes` không cần thiết cho 1 project WinForms thuần, cùng tinh thần giảm dependency đã áp dụng ở `05-image-pipeline-architecture.md` ADR-50/51).
- **Threading (ADR-53)**: mỗi lần cần tra cứu (lần hiển thị overlay đầu tiên + mỗi lần `FE-016b` yêu cầu tính lại do resize/move, mục 2.6), chạy trên **1 `Thread` mới, `SetApartmentState(ApartmentState.STA)`**, không dùng `Task.Run`/ThreadPool. Lý do: COM call của `IUIAutomation` là **block đồng bộ**, không hỗ trợ cancel giữa chừng (`CancellationToken` chỉ dừng việc *chờ*, không dừng được lệnh COM đang treo bên dưới) — nếu app hiếm gặp bị treo (ví dụ target app không phản hồi UIA), thread đó có thể leak tới khi process app kia thoát; dùng `Thread` riêng (không phải ThreadPool) giới hạn thiệt hại ở đúng 1 thread bị leak, không làm cạn ThreadPool dùng chung cho IPC. Số lượng tra cứu đồng thời tối đa bị chặn trên bởi `BE-088` (≤ 10 overlay), nên tổng số thread có thể leak trong tình huống xấu nhất vẫn có trần.
- **Điều kiện tìm control** (`FE-016c` lớp 1: "`ControlType = Button`, `AutomationId`/`Name`/`LocalizedControlType` gợi ý 'Close'"): `IUIAutomation` core chỉ hỗ trợ `PropertyCondition` khớp chính xác (không hỗ trợ "contains" ở tầng provider) — quy trình 2 bước:
  1. `ElementFromHandle(hwnd)` lấy root element, `FindAll(TreeScope_Subtree, CreatePropertyCondition(ControlTypePropertyId, UIA_ButtonControlTypeId))` lấy **toàn bộ** button trong cây (giới hạn thời gian bởi cùng 1 ngân sách 150ms tổng, không tách ngân sách con).
  2. Lọc client-side (LINQ) theo heuristic OR: `AutomationId` khớp chính xác 1 trong tập `{"Close", "CloseButton", "closeButton", "PART_CloseButton", "CaptionButtonClose", "Chrome_CloseButton", "Box_CloseButton"}` (tập khởi điểm, mở rộng thêm khi test thực tế theo danh sách app ưu tiên ở `BE-075`) **HOẶC** `Name`/`LocalizedControlType` chứa (không phân biệt hoa/thường) `"close"` hoặc `"đóng"` (khớp cả app tiếng Anh lẫn tiếng Việt).
  3. Nếu nhiều kết quả khớp (false positive khả dĩ, ví dụ "Close tab" lẫn "Close window"), chọn phần tử có `BoundingRectangle` **gần góc trên-phải cửa sổ nhất** (khoảng cách Euclid tới `(windowRect.Right, windowRect.Top)`).
- **Timeout 150ms** (`FE-016c`, đã chốt chính thức): đo từ lúc bắt đầu `FindAll` tới lúc có kết quả; `Task.WhenAny(uiaTask, Task.Delay(150))` ở thread gọi (không phải thread STA thực thi) để không block UI thread chính khi chờ — nếu timeout thắng, **bỏ qua** kết quả UIA nếu nó tới muộn sau đó (không áp dụng, tránh giật hình do nâng cấp trễ vô nghĩa sau khi user đã tương tác), giữ nguyên lớp 3.
- **Câu hỏi validate thực nghiệm** (tương tự tiền lệ DXGI+Low IL đã validate ở `05` mục 8): `Overlay` chạy Low Integrity Level (`06-security-architecture.md` mục 2.4) — `IUIAutomation` client-side (đọc thuộc tính cửa sổ khác) về lý thuyết không bị MIC chặn (không phải write-up), nhưng cần xác nhận thực nghiệm không có giới hạn UIPI (User Interface Privilege Isolation) nào chặn Low IL đọc cây UIA của 1 cửa sổ chạy Medium/High IL — ghi vào câu hỏi mở (mục 6), không chặn thiết kế (đã có lớp 3 fallback không phụ thuộc UIA).

### 2.3 Lớp 2 — khoảng đệm an toàn (áp dụng lên kết quả lớp 1)

- **Padding = 16px mỗi cạnh, ở 100% DPI** (nhân theo `dpiScale = GetDpiForWindow(windowHandle) / 96.0` — dùng đúng handle cửa sổ vi phạm, không dùng DPI của overlay form, vì overlay luôn ở cùng màn hình với cửa sổ nó che nên 2 giá trị luôn khớp nhau, chỉ chọn API rõ ràng hơn). Con số 16px chọn dựa theo đúng tinh thần tính toán "dư ra ~20px rộng/~18px cao" đã dùng để suy ra 160×50 ở `FE-016c` mục 2 — làm tròn thành 1 hằng số đối xứng áp dụng đều 4 cạnh quanh rect chính xác đo được từ lớp 1 (khác lớp 3 vốn không có rect chính xác để pad quanh, phải dùng nguyên khối 160×50 neo góc, mục 2.4).
- **Giá trị 16px là tạm thời, cùng nhóm câu hỏi mở với 160×50** (`FE-016c` đã ghi rõ "cần đo thực tế trên các app cụ thể ở System Design để xác nhận hoặc tinh chỉnh trước khi khoá cứng vào code") — không tự chốt cứng ở đây, ghi lại ở mục 6.
- Rect cuối = `InflateRect(uiaButtonRect, 16·dpiScale)`, sau đó `Intersect` với `windowRect` (không để vùng loại trừ tràn ra ngoài biên cửa sổ — không có ý nghĩa và có thể gây lỗi khi convert sang toạ độ overlay-relative ở mục 2.5).

### 2.4 Lớp 3 — dự phòng cố định

- **Rect = 160px × 50px ở 100% DPI** (`dpiScale` như mục 2.3), neo **góc trên-phải cửa sổ**: `X = windowRect.Right − 160·dpiScale`, `Y = windowRect.Top`, `Width = 160·dpiScale`, `Height = 50·dpiScale` — dùng `windowRect` lấy qua `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` (đúng API `IMG-012`/`05` mục 4.1 đã dùng cho crop, tái dùng nhất quán, không dùng `GetWindowRect` thô có thể lệch do shadow).
- Cả 2 con số 160/50 **giữ nguyên đúng giá trị đã "tạm chốt v0.4.1" ở spec** — file này không tự đổi số, chỉ hiện thực hoá công thức neo góc + scale DPI.

### 2.5 Áp dụng vùng loại trừ lên `ContentBlurOverlayForm`

- Dùng `System.Windows.Forms.Region` chuẩn WinForms: `form.Region = Region.FromRectangle(form.ClientRectangle); form.Region.Exclude(exclusionRectRelativeToForm);` — vùng bị `Exclude` trở thành **trong suốt hoàn toàn và click-through** tới cửa sổ/control gốc bên dưới, đúng `FE-016a` ("không phải overlay tự vẽ 1 nút X giả... phải là nút X thật của cửa sổ gốc"): vì `Region.Exclude` chỉ cắt vùng vẽ/hit-test của **overlay form**, không tạo bất kỳ control giả nào — click rơi thẳng xuống `WS_EX_LAYERED`/`WS_EX_TRANSPARENT`? **Không cần 2 cờ này**: `Region` loại trừ đã đủ để Windows định tuyến click vào đúng vùng đó xuống cửa sổ Z-order thấp hơn (hành vi chuẩn của `SetWindowRgn`, WinForms `Region` property dùng đúng cơ chế này bên dưới).
- Toạ độ `exclusionRectRelativeToForm` = rect tuyệt đối (màn hình) tính ở mục 2.3/2.4, trừ đi `form.Location` (góc trên-trái form, đã set = `windowRect.Location` từ `ApplyRect`, xem `Architecture/02` mục 2.4 và code hiện tại `ContentBlurOverlayForm.ApplyRect`).

### 2.6 Tính lại khi cửa sổ di chuyển/đổi kích thước (`FE-016b`, đồng bộ `FE-014`)

**Gap cần đóng cùng lượt này**: code Đợt 1 hiện tại chỉ cập nhật `rect` khi `Service` gửi `OverlayRectListCommand` mới (tần suất phụ thuộc chu kỳ capture của `Vision`, `PERF-010` — có thể 1-5 giây), **chưa** có `WinEventHook` theo dõi real-time như `BE-031` yêu cầu ("`GetWindowRect` + `WinEventHook` để theo dõi resize/move real-time"). `FE-016b` yêu cầu tường minh vùng loại trừ phải tính lại "mỗi khi overlay theo dõi cửa sổ thay đổi vị trí/kích thước" — không thể thoả mãn đúng nghĩa nếu chỉ cập nhật theo chu kỳ capture chậm. Đây không phải 1 sản phẩm/WHAT mới (đã `APPROVED` ở `BE-031`/`FE-014`/`FE-016b`), chỉ là hoàn thiện 1 phần HOW đã bị hoãn tối giản ở Đợt 1 — file này chốt thiết kế, `feature-dev` hiện thực ở Đợt 2:

- `OverlayCoordinator` đăng ký **1 `WinEventHook` dùng chung** cho toàn bộ instance đang quản lý: `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT)` — callback lọc theo `hwnd` tham số có nằm trong `_overlays.Keys` hay không (bỏ qua ngay nếu không, tránh xử lý sự kiện của toàn bộ hệ thống).
- Khi khớp: đọc lại `windowRect` (`DwmGetWindowAttribute`, mục 2.4) → cập nhật `Bounds` của đúng `ContentBlurOverlayForm` đó (thực hiện đúng `BE-031`/`FE-014` real-time) → tính lại vùng loại trừ **bắt đầu lại từ lớp 3** (đồng bộ, ngay lập tức, đúng mục 2.1) rồi kích lại lớp 1 bất đồng bộ (150ms) để nâng cấp — mỗi lần move/resize là 1 chu trình mới y hệt lúc `Show()` lần đầu.
- **Debounce 50ms** (ADR chung với mục 3.5 bên dưới — cùng 1 hook dùng cho cả rect lẫn z-order): gom các sự kiện `LOCATIONCHANGE` liên tiếp trong 1 cửa sổ 50ms thành 1 lần tính lại duy nhất — tránh gọi lại toàn bộ chu trình (bao gồm spawn 1 thread STA UIA mới) hàng chục lần/giây trong lúc user đang kéo chuột di chuyển cửa sổ.

## 3. Đa cửa sổ vi phạm đồng thời: rendering, z-index, giới hạn + chế độ gộp

### 3.1 Dựng danh sách phía `Service` (`BE-084`–`086`) — đã có, không đổi

`OverlayDecisionCoordinator` (Đợt 1) đã đúng kiến trúc: `Dictionary<ulong windowHandle, OverlayRect> _active`, cập nhật/xoá theo từng `VisionInferenceResult` nhận được, đẩy lại **toàn bộ danh sách hiện hành** (`BuildCommand()`) mỗi khi có thay đổi — không đợi hết 1 chu kỳ mới gửi (đúng `BE-086`: "xử lý xong cửa sổ nào, hiện overlay cửa sổ đó ngay"). `OverlayCoordinator` phía Overlay (`ApplyOverlayList`) đã diff đúng theo `window_handle`, tạo/xoá `ContentBlurOverlayForm` tương ứng (đúng `BE-084`: mỗi cửa sổ 1 overlay độc lập). **Không cần sửa 2 class này cho mục 3.1** — phần cần thêm chỉ là mục 3.2 (ngưỡng + gộp) và mục 3.5 (z-index).

### 3.2 Giới hạn 10 overlay + chế độ gộp (`BE-088`/`089`)

**Ngưỡng vào/ra có hysteresis (ADR-57, technical judgment — `BE-088` chỉ chốt ngưỡng VÀO = 10, không chốt ngưỡng RA)**:

```
MERGE_ENTER_THRESHOLD = 10   // BE-088: "vượt quá 10" → kích hoạt khi count > 10 (≥ 11)
MERGE_EXIT_THRESHOLD  = 8    // ADR-57: thấp hơn ngưỡng vào để tránh "flapping" khi count dao động quanh 10-11
```

Mở rộng `OverlayDecisionCoordinator.BuildCommand()`:

```
lock (_sync):
    if !_mergedModeActive && _active.Count > MERGE_ENTER_THRESHOLD:
        _mergedModeActive = true
    else if _mergedModeActive && _active.Count <= MERGE_EXIT_THRESHOLD:
        _mergedModeActive = false

    if !_mergedModeActive:
        return _active.Values                                    // hành vi Đợt 1, không đổi

    allHandles = _active.Keys.ToList()                            // BE-089: danh sách TOÀN HỆ THỐNG
    groups = _active.Values.GroupBy(r => r.MonitorId)
    return groups.Select(g => {
        representative = g.OrderBy(r => r.OverlayId).First()      // ổn định giữa các lần rebuild (giảm giật hình)
        return new OverlayRect {
            WindowHandle = representative.WindowHandle,
            Rect = representative.Rect,                            // KHÔNG dùng ở phía Overlay khi IsMerged=true (mục 3.3)
            MonitorId = g.Key,
            OverlayId = StableMergedOverlayId(g.Key),              // namespace riêng, xem dưới
            Reason = OverlayReason.ContentViolation,
            IsMerged = true,
            MergedWindowHandles = allHandles,                      // giống hệt nhau ở MỌI rect gộp — đúng nghĩa đen BE-089
        }
    })
```

- **`StableMergedOverlayId(monitorId)`**: `Dictionary<uint monitorId, uint>` riêng (namespace tách biệt khỏi `_nextOverlayId` của overlay đơn — 2 khái niệm "overlay_id" không bao giờ trộn lẫn giữa 2 chế độ, tránh 1 `ForceCloseRequest` cũ từ chế độ trước bị hiểu nhầm sang chế độ sau). Gán id mới (tăng dần) lần đầu 1 `monitorId` xuất hiện ở chế độ gộp, giữ nguyên cho các lần rebuild sau miễn còn active.
- **Không cần sửa `HandleForceCloseAsync`**: khi Overlay bấm nút overlay gộp, nó gửi **nhiều** `ForceCloseRequest` tuần tự (1 cho mỗi handle trong `merged_window_handles`, mục 3.3) — mỗi request xử lý y hệt code Đợt 1 hiện tại (xoá đúng 1 `window_handle` khỏi `_active`, ghi 1 dòng audit log, `PushCurrentList()`). Sau khi xử lý hết batch, `_active.Count` giảm xuống dưới `MERGE_EXIT_THRESHOLD`, `_mergedModeActive` tự tắt ở lần `BuildCommand()` kế tiếp — **không có nhánh code riêng nào cho việc "thoát chế độ gộp"**, nó là hệ quả tự nhiên của ngưỡng hysteresis áp dụng lại mỗi lần build. Đây là điểm mạnh thiết kế: bề mặt thay đổi code ở `Service` chỉ nằm gọn trong `BuildCommand()`.

### 3.3 Xử lý phía Overlay khi `IsMerged = true`

Mở rộng `OverlayCoordinator.ApplyOverlayList`: nếu 1 `window_handle` đã có `ContentBlurOverlayForm` tồn tại nhưng `existing.IsMerged != incoming.IsMerged` (chuyển đổi giữa 2 chế độ trùng đúng lúc 1 window_handle được chọn làm representative) → xoá + tạo lại form mới (không tái dùng `ApplyRect` tại chỗ, vì cấu trúc control bên trong 2 chế độ khác nhau — có/không có `Region` loại trừ, mục 3.4).

Khi `IsMerged = true`, `ContentBlurOverlayForm` (biến thể "merged"):

- **Bounds**: **bỏ qua field `rect`** nhận được — tự resolve toàn bộ màn hình vật lý qua `MonitorFromWindow(new IntPtr(WindowHandle), MONITOR_DEFAULTTONEAREST)` → `GetMonitorInfo(hMonitor)` → dùng `rcMonitor` (toàn bộ màn hình, **không phải** `rcWork` — che cả vùng taskbar, đúng nghĩa "che toàn bộ màn hình đó" ở `BE-088`, không chừa khoảng hở nào có thể lộ nội dung hoặc bị dùng để thao tác vòng qua overlay) (ADR-58, giải thích lý do không cần Service tính/truyền toạ độ màn hình — mục 5).
- **Không carve vùng loại trừ** (mục 3.4 giải thích tại sao).
- **Nội dung hiển thị**: tái dùng **nguyên vẹn** thông điệp đã cấu hình (`FE-012`) + nhãn nút "Tắt nội dung" (`FE-012`/`FE-060`) — **không có biến thể văn bản riêng cho chế độ gộp** (tránh phát minh nội dung UI mới ngoài phạm vi đã chốt ở `Specification/03-frontend-ui-spec.md`).
- **Xử lý bấm nút** (thay `HandleCloseButtonClicked` hiện tại khi `IsMerged`) — hàm dùng chung `TriggerForceCloseAll(source)`, gọi từ 2 nơi: (a) click nút thủ công, (b) hết giờ 30 giây tự động (mục 3.4):

```
TriggerForceCloseAll(CloseSource source):               // source = MANUAL (nút bấm) hoặc AUTO_TIMEOUT (mục 3.4)
    _autoTimeoutTimer?.Stop()                             // huỷ ngay bộ đếm cục bộ, tránh trigger 2 lần (mục 3.4)
    foreach handle in MergedWindowHandles:
        OverlayWindowInterop.RequestClose(handle)          // PostMessage(WM_CLOSE) cục bộ — best-effort, y hệt cơ chế đơn (BE-032)
    foreach handle in MergedWindowHandles:
        sendForceClose(new ForceCloseRequest {
            WindowHandle = handle,
            OverlayId = thisOverlay.OverlayId,               // overlay_id CHUNG cho cả batch — phục vụ đối chiếu audit log
            ClickedAtUnixMs = now,
            Source = source,                                 // BE-089b: bắt buộc để Service ghi audit log phân biệt manual/auto-timeout
        })
    RemoveOverlay(...)                                       // xoá chính overlay gộp này khỏi _overlays cục bộ, chờ list mới từ Service

// nút "Tắt nội dung" gọi: TriggerForceCloseAll(CloseSource.MANUAL)
```

Đúng `BE-089a`: bấm nút ở **bất kỳ** overlay gộp nào (màn hình nào) đều đóng **toàn bộ** cửa sổ vi phạm hệ thống, vì `MergedWindowHandles` được `Service` gán giống hệt nhau ở mọi rect gộp (mục 3.2). Field `Source` (enum `CloseSource`, mới thêm `ForceCloseRequest` ở `03-ipc-communication.md` v0.4.0) áp dụng cho **cả 2 chế độ** (đơn lẫn gộp) để nhất quán 1 message duy nhất — ở chế độ đơn (`BE-032`, không có khái niệm timeout), `Overlay` luôn gửi `Source = CloseSource.MANUAL` tường minh (không để mặc định `UNSPECIFIED`).

### 3.4 Full-screen lock: `FE-016` không áp dụng ở chế độ gộp, timeout 30s tự động, audit log phân biệt nguồn (`BE-088a`/`BE-089a`/`BE-089b`, `FE-016f`/`FE-016g`, `GEN-007b`)

**Đã CHỐT chính thức** (thay thế toàn bộ nội dung "flagged for review" của v0.1.0 — xem mục 1): `Specification/02-backend-spec.md` (`BE-088a`/`BE-089a`/`BE-089b`, v0.13.0) và `Specification/03-frontend-ui-spec.md` (`FE-016f`/`FE-016g`, v0.9.0) đã chốt tường minh đây là **chủ đích thiết kế** ("khi người dùng cố tình mở >10 nội dung vi phạm, hệ thống phải quyết liệt hơn"), không còn là suy luận kỹ thuật chờ duyệt. Mục này chốt thiết kế HOW đầy đủ cho hành vi đã chốt.

#### 3.4.1 Full-screen lock thay thế hoàn toàn `FE-010`/`FE-016` ở overlay gộp

- Bounds toàn màn hình (`rcMonitor`, không carve vùng loại trừ nào) đã thiết kế ở mục 3.3 — **không đổi** ở amendment này, chỉ nay có căn cứ spec tường minh (`BE-088a`) thay vì suy luận.
- Đúng 1 nút "Tắt nội dung" duy nhất/màn hình (`BE-089a`) — đã thiết kế ở mục 3.3.
- `FE-016`/`FE-016a`-`FE-016e` (vùng loại trừ 3 lớp, mục 2 file này) **không áp dụng** cho overlay khi `IsMerged = true` — đây là ngoại lệ tường minh, đã chốt ở `FE-016f` và `GEN-007b` (`Specification/00-INDEX.md` mục 6), áp dụng **chỉ** cho overlay gộp; overlay chế độ thường (`IsMerged = false`) vẫn tuân thủ đầy đủ mục 2 không đổi.

#### 3.4.2 Lối thoát dự phòng thứ 2 — auto-timeout 30 giây cục bộ (`BE-089b`, `FE-016g`, `GEN-007b`)

`GEN-007b` yêu cầu tường minh: full-screen lock phải có **2 lối thoát độc lập** — (1) nút bấm thủ công (mục 3.3/3.4.1), (2) tự động hết giờ, **không phụ thuộc round-trip IPC tới `Service`** (để không bị trễ/kẹt nếu kênh IPC có vấn đề đúng lúc đó — đây là điểm mấu chốt khiến lối thoát thứ 2 thực sự "độc lập" với lối thoát thứ nhất và với chính hạ tầng IPC).

```
// Trong ContentBlurOverlayForm (biến thể merged), khởi tạo lúc Show() lần đầu — mỗi lần Show() là 1 vòng đếm mới,
// đúng nghĩa đen BE-089b: "mỗi overlay full-screen lock tự đếm giờ độc lập kể từ lúc chính nó hiển thị"
OnMergedShow():
    _remainingSeconds = 30
    _autoTimeoutTimer = new System.Windows.Forms.Timer { Interval = 1000 }   // tick 1 lần/giây — KHÔNG phải 1 timer bắn
                                                                               // đúng 30s duy nhất, xem lý do ở mục 3.4.3
    _autoTimeoutTimer.Tick += (s, e) => {
        _remainingSeconds--
        RaiseCountdownTick(_remainingSeconds)         // mục 3.4.3 — hook cho UI, KHÔNG bắt buộc phải có UI lắng nghe
        if (_remainingSeconds <= 0):
            TriggerForceCloseAll(CloseSource.AUTO_TIMEOUT)   // mục 3.3 — huỷ timer + PostMessage(WM_CLOSE) hàng loạt + ForceCloseRequest hàng loạt
    }
    _autoTimeoutTimer.Start()
```

- **`System.Windows.Forms.Timer`** (không phải `System.Threading.Timer`) — ADR-63: callback chạy trên UI thread của chính overlay form qua message loop chuẩn WinForms, tránh mọi vấn đề cross-thread khi callback cần thao tác control UI (mục 3.4.3) hoặc gọi API Win32 thao tác cửa sổ (`PostMessage`) — nhất quán với việc toàn bộ `ContentBlurOverlayForm` vốn đã là 1 `Form` chạy trên UI thread, không cần đồng bộ hoá thêm.
- **Không round-trip IPC để đếm giờ**: 30 giây đếm hoàn toàn bằng đồng hồ hệ thống cục bộ của tiến trình `Overlay` (`Environment.TickCount64` làm nguồn đếm nếu cần chống trôi do máy sleep — quyết định implement cụ thể để `feature-dev`, không ảnh hưởng kiến trúc), không gửi/chờ bất kỳ message nào tới `Service` trong lúc đếm — đúng yêu cầu `BE-089b` ("không phụ thuộc round-trip IPC tới Service để tránh trễ"). Việc gửi `ForceCloseRequest` chỉ xảy ra **1 lần duy nhất** ở cuối (khi hết giờ hoặc khi user bấm nút trước đó), không phải là cơ chế đếm.
- **Huỷ timer khi không còn cần** (tránh trigger thừa/leak): `_autoTimeoutTimer.Stop()` được gọi ở 2 nơi — (a) đầu `TriggerForceCloseAll` (mục 3.3, dù trigger bởi nguồn nào) để tránh gọi lại lần 2 nếu cả 2 nguồn xảy ra gần như đồng thời (race hiếm nhưng phải chặn — nếu `Tick` đã enqueue nhưng `TriggerForceCloseAll` từ click đã chạy trước, `Stop()` không huỷ được lệnh đã enqueue trên message queue nhưng `_remainingSeconds` đã âm/handles đã rỗng nên nhánh `if` không kích hoạt lại lần 2, hoặc `Service`-side `_active.Remove` trả `false` cho handle đã bị xoá — idempotent theo đúng thiết kế mục 3.2, an toàn dù có gọi trùng); (b) khi form bị `RemoveOverlay`/`Dispose` bởi lý do khác (ví dụ `ApplyOverlayList` xoá overlay này vì chế độ gộp đã tắt do 1 overlay khác vừa xử lý xong toàn bộ handles trước — mục 3.2 "không có nhánh code riêng cho thoát chế độ gộp", overlay này nhận list mới không còn chứa monitor của nó và bị xoá) — `Dispose()` override gọi `_autoTimeoutTimer?.Dispose()` để không leak timer/không tự kích hoạt `TriggerForceCloseAll` trên 1 form đã bị huỷ.

#### 3.4.3 Thiết kế cho câu hỏi mở UX (đếm ngược có hiển thị hay không) — KHÔNG tự quyết định

`Specification/03-frontend-ui-spec.md` mục 9 (câu hỏi mở) ghi rõ: có nên hiển thị đếm ngược trực quan (`"Tự động đóng sau: 00:30"`) trên overlay full-screen lock hay không — **chưa chốt UX**, `architecture-writer` không tự quyết định điểm này (đúng nguyên tắc không phát minh quyết định sản phẩm). Thiết kế ở mục 3.4.2 chủ động tách rời "cơ chế đếm giờ" khỏi "hiển thị đếm giờ" để **cả 2 khả năng đều triển khai được mà không đổi kiến trúc lõi**:

- `RaiseCountdownTick(int remainingSeconds)` là 1 event nội bộ của `ContentBlurOverlayForm` (biến thể merged), phát ra mỗi giây bất kể có UI lắng nghe hay không.
- Nếu chủ dự án chốt **có** hiển thị: thêm 1 `Label` (ẩn theo mặc định, `Visible` điều khiển bởi 1 constant/config cục bộ — không cần field IPC mới, không cần Service biết gì về việc này) subscribe event trên, format `"Tự động đóng sau: 00:{remainingSeconds:D2}"` (chuỗi lấy từ resource ngôn ngữ theo `FE-060`, không hardcode).
- Nếu chủ dự án chốt **không** hiển thị: không đăng ký listener nào cho `RaiseCountdownTick` — cơ chế đếm giờ/auto-timeout vẫn hoạt động y hệt, không ảnh hưởng.
- Bề mặt thay đổi khi UX được chốt sau này: thêm/bớt 1 `Label` + đăng ký event, **không** đổi `OnMergedShow`/`TriggerForceCloseAll`/IPC schema — đúng yêu cầu "dễ implement cả 2 khả năng mà không đổi kiến trúc lõi".

#### 3.4.4 Audit log phân biệt nguồn `"manual"` vs `"auto-timeout"` (`BE-089b`)

`BE-089b` yêu cầu tường minh audit log phải phân biệt nguồn kích hoạt force-close. Thiết kế xuyên suốt 2 lớp:

1. **Trên dây (IPC)**: field `source` (enum `CloseSource`) mới thêm vào message `ForceCloseRequest` — xem mục 3.3 và amendment `03-ipc-communication.md` v0.4.0 mục dưới.
2. **Trong audit log** (`%ProgramData%\ParentalGuard\audit.log`): `Service` (`OverlayDecisionCoordinator.HandleForceCloseAsync`, đã ghi event `ForceCloseRequested` từ Đợt 1 — xem `Architecture/04-data-architecture.md` mục 5.1) map `CloseSource.MANUAL → "manual"`, `CloseSource.AUTO_TIMEOUT → "auto-timeout"` (đúng 2 chuỗi literal `BE-089b` yêu cầu), thêm field `source` vào `detail` JSON của event đó — chi tiết đầy đủ ở amendment `04-data-architecture.md` mục 5.1 dưới đây (cùng lượt sửa).

Overlay chế độ đơn (không gộp) tái sử dụng đúng message `ForceCloseRequest`, luôn gửi `source = MANUAL` (mục 3.3) — vì vậy field `source` trong audit log **luôn có giá trị xác định** cho mọi sự kiện `ForceCloseRequested`, không có trường hợp thiếu dữ liệu cần fallback.

### 3.5 Z-index khớp cửa sổ gốc (`BE-087`)

Không hiện thực bằng field IPC (không cần `Service` tính toán z-order — `Service` không có quyền window-station để biết Z-order thật, đúng lý do Session 0 Isolation đã lặp lại nhiều lần ở `02-process-architecture.md`/`03-ipc-communication.md`). **`Overlay` tự đồng bộ Z-order cục bộ** (ADR-56), dùng chung `WinEventHook` đã đăng ký ở mục 2.6 (thêm `EVENT_SYSTEM_FOREGROUND` và `EVENT_OBJECT_REORDER` vào cùng callback, cũng lọc theo `_overlays.Keys`, cũng debounce 50ms):

```
ResyncZOrder():                                            // gọi từ callback WinEventHook (debounced), hoặc ngay sau ApplyOverlayList
    trackedHandles = _overlays.Keys
    zOrderedFrontToBack = EnumerateTopLevelWindowsInZOrder()   // EnumWindows — trả về top-to-bottom
        .Where(h => trackedHandles.Contains(h))
    // Đảo ngược (back-to-front) rồi SetWindowPos(TOPMOST) tuần tự: mỗi lệnh "đẩy" 1 window lên đỉnh dải topmost,
    // gọi theo đúng thứ tự back→front cho ra đúng thứ tự tương đối front→back mong muốn ở bước cuối.
    foreach h in zOrderedFrontToBack.Reverse():
        SetWindowPos(_overlays[h].Handle, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
```

`SWP_NOACTIVATE` bắt buộc — tránh việc resync z-order vô tình cướp focus của cửa sổ đang active (sẽ tự phá luôn thứ tự Z vừa set, và gây khó chịu cho user đang thao tác cửa sổ khác). Kỹ thuật "gọi `SetWindowPos(HWND_TOPMOST)` lặp lại theo đúng thứ tự mong muốn" là pattern Win32 chuẩn để enforce thứ tự tương đối giữa nhiều cửa sổ cùng thuộc dải topmost — không cần API/thư viện nào khác.

## 4. Multi-monitor phía Overlay/Service (`BE-080`–`083`)

Phần capture/chọn cửa sổ phía `Vision` đã ở `05-image-pipeline-architecture.md` mục 3.5. Phần dưới đây chỉ nói phần Overlay/Service tiêu thụ.

### 4.1 Icon trạng thái mỗi màn hình (`BE-033`/`BE-083`, UX đầy đủ `FE-020`–`022`)

**Cập nhật v0.2.0 — mở rộng phạm vi**: bản v0.1.0 giới hạn mục này ở phần cấu trúc (ADR-62 cũ), để dành UX đầy đủ (`FE-020`–`022`) cho 1 Đợt sau chưa xác định. Chủ dự án đã quyết định làm luôn UX đầy đủ trong Đợt 2 — mục dưới đây thiết kế đầy đủ: cấu trúc render, nguồn dữ liệu trạng thái, tooltip, kéo-thả + lưu vị trí bền vững.

#### 4.1.1 Cấu trúc render (giữ nguyên từ v0.1.0)

- 1 window nhỏ, luôn topmost, không chiếm taskbar (`ShowInTaskbar = false`) — **1 instance cho mỗi màn hình vật lý đang có**, tạo lúc `Overlay` khởi động, enumerate qua `EnumDisplayMonitors` (Win32, gọi trực tiếp từ `Overlay` — đã ở session tương tác, không qua `Service`).
- **Hot-plug**: `OverlayCoordinator` (đã là 1 `Form`, tự nhận `WM_DISPLAYCHANGE` qua `WndProc` không cần đăng ký thêm) → khi nhận, re-enumerate `EnumDisplayMonitors`, diff với danh sách instance hiện có (thêm mới cho màn hình vừa cắm, đóng instance của màn hình vừa rút) — cùng pattern diff-theo-key đã dùng cho `_overlays` (mục 3.1), tái dùng tư duy thiết kế nhất quán.
- **Kích thước/hình dạng** (ADR-64): `Form` nhỏ cố định (ví dụ 40×40px ở 100% DPI, nhân `dpiScale` như mục 2.3 — DPI của chính màn hình nó nằm trên, qua `GetDpiForWindow` trên handle của chính icon form đó), `FormBorderStyle = None`. Vẽ nội dung qua `WS_EX_LAYERED` + `UpdateLayeredWindow` (per-pixel alpha) để có hình khối tròn/bo góc đúng thẩm mỹ Fluent (`FE-001`) trên nền trong suốt, thay vì hình chữ nhật cứng — kỹ thuật tương tự lớp layered đã dùng cho blur ở mục 2 nhưng đơn giản hơn nhiều (chỉ vẽ 1 icon tĩnh + đổi màu theo state, không phải nội dung động liên tục).
- **Click-through ngoài icon đạt được tự nhiên** (`FE-020`, "không chặn tương tác với nội dung bên dưới") — khác overlay blur ở mục 2 (cần `Region.Exclude` vì che cả 1 cửa sổ lớn), icon chỉ chiếm đúng vùng nhỏ 40×40px của chính nó nên không cần kỹ thuật loại trừ vùng gì thêm: ngoài phạm vi form, click luôn rơi xuống window bên dưới theo hành vi mặc định của Windows.
- Icon glyph cụ thể (Fluent System Icons, theo `4. Design System` ở `03-frontend-ui-spec.md`) và mã màu chính xác (chỉ có khái niệm xanh/vàng/đỏ được chốt ở `FE-021`, chưa có mã hex) là chi tiết asset/theme thực hiện lúc code UI thật — không phải quyết định kiến trúc, không chặn thiết kế ở đây.

#### 4.1.2 Nguồn dữ liệu trạng thái (`FE-021`: 3 trạng thái màu)

`FE-021` yêu cầu 3 trạng thái: **Đang hoạt động** (xanh) / **Tạm dừng** (vàng, kèm đếm ngược) / **Lỗi-gián đoạn** (đỏ, hiếm gặp). Icon là 1 tiến trình con của `Overlay` — không tự biết state trung tâm, cần `Service` đẩy xuống, đúng nguyên tắc "Overlay là hàm render thuần, `Service` là nguồn sự thật duy nhất" (`ADR-12`, `02-process-architecture.md` mục 5.1). Gap kỹ thuật (HOW-level, không phải thiếu spec): chưa có message IPC nào mang trạng thái này — bổ sung `MonitoringStatusUpdate` (Service → Overlay, field 63, amendment `03-ipc-communication.md` v0.4.0):

```
IconState state                      // ACTIVE | PAUSED | ERROR
int64     pause_expires_at_unix_ms   // chỉ có ý nghĩa khi state == PAUSED, 0 nếu không áp dụng
```

- **Thời điểm push**: (a) ngay sau handshake `Overlay` connect — cùng lúc `Service` push `OverlayRectListCommand` hiện hành (mục 4.1 `03-ipc-communication.md`); (b) mỗi khi state trung tâm chuyển đổi ảnh hưởng icon.
- **Ánh xạ trạng thái trung tâm (`02-process-architecture.md` mục 3) → `IconState`** (ADR-65):

| State trung tâm | `IconState` | Ghi chú |
|---|---|---|
| `Running·Monitoring` (kể cả khi `Degraded·FailSecure`) | `ACTIVE` | Theo đúng `ADR-14` — `Degraded·FailSecure` vẫn là "đang giám sát", chỉ khác nguồn cấu hình, không phải state lỗi hiển thị cho trẻ |
| `Running·Paused` | `PAUSED` | Kèm `pause_expires_at_unix_ms` từ `PauseState` (`04-data-architecture.md` mục 3.4) |
| `Starting` | `ERROR` | Cửa sổ ngắn lúc boot trước khi vào 1 trong 2 state ổn định trên |
| Đang respawn `Vision`/`Overlay` sau mất heartbeat (`BE-023`, ≤3s) | `ERROR` | "Hiếm khi xảy ra, watchdog nên khắc phục nhanh" — đúng mô tả `FE-021` |
| **Bổ sung Đợt 4**: cửa sổ nghi vấn tấn công theo `ANTI-060` (restart lặp lại vượt ngưỡng N/T, gồm cả `Service`/`Watchdog` bị kill-restart hoặc registry tamper — không chỉ `Vision`/`Overlay`) | `ERROR` | **Giữ liên tục** (không chỉ thoáng qua như dòng trên) cho tới khi hết cửa sổ T phút không có sự kiện mới — đây chính là "banner on-screen, không Toast chủ động" mà `ANTI-060`/`ANTI-061` yêu cầu, tái dùng nguyên kênh `ERROR` đã có thay vì tạo state/message mới (`09-anti-tamper-architecture.md` mục 6.2, ADR-100). `Overlay` không phân biệt 2 nguyên nhân `ERROR` ở 2 dòng này — vẫn đúng nguyên tắc "hàm render thuần" (`02` mục 5.1), chỉ hiển thị đúng state nhận được |

- **Trường hợp đặc biệt — mất kết nối IPC tới `Service`** (ADR-66): nếu chính pipe `ParentalGuard.Svc.Overlay` đứt (`IOException`, `03-ipc-communication.md` mục 6), sẽ không có `MonitoringStatusUpdate` nào tới nữa trong lúc đó — `Overlay` **tự chuyển local toàn bộ icon sang `ERROR`** ngay khi phát hiện pipe đứt (không chờ push từ `Service`, vì trong tình huống này sẽ không có push nào tới), quay lại giá trị nhận qua `MonitoringStatusUpdate` đầu tiên sau khi reconnect thành công. Đây là lớp phòng thủ cục bộ giống tinh thần `ADR-54` (không bao giờ để icon "đứng hình" hiển thị sai trạng thái).
- **Đếm ngược khi `PAUSED`**: `Overlay` tự tính lại mỗi giây từ `pause_expires_at_unix_ms` (không round-trip liên tục) — cùng pattern timer 1s cục bộ đã thiết kế ở mục 3.4.2.

#### 4.1.3 Tooltip (`FE-022`)

Hover hiện `ToolTip` chuẩn WinForms, nội dung dựng từ resource ngôn ngữ (`FE-060`, không hardcode) theo `IconState` hiện tại + (nếu `PAUSED`) nối thêm chuỗi đếm ngược định dạng cục bộ — **không** hiện số liệu chi tiết khác (đúng `FE-022`: "không hiện thông tin nhạy cảm, chỉ trạng thái chung"). Ví dụ nội dung khái niệm (không phải chuỗi hardcode cuối cùng, chỉ mô tả cấu trúc): "Đang hoạt động" / "Tạm dừng — còn 04:32" / "Đang khôi phục...". `FE-022` không mô tả tương tác click nào khác ngoài hover — file này không phát minh hành vi click bổ sung (ví dụ mở Dashboard) vì không có căn cứ trong `Specification/`.

#### 4.1.4 Vị trí mặc định, kéo-thả, và lưu bền vững per-monitor (`FE-020`/`FE-020a`)

- **Vị trí mặc định**: góc dưới-phải (`FE-020`) của **work area** màn hình đó (`GetMonitorInfo(...).rcWork`, không phải `rcMonitor`, để không đè lên taskbar).
- **Kéo-thả** (`FE-020a`, không yêu cầu xác thực mật khẩu): pattern chuẩn WinForms — `MouseDown` lưu offset con trỏ so với góc form, `MouseMove` cập nhật `Form.Location` theo con trỏ, `MouseUp` commit vị trí cuối cùng và kích hoạt lưu (dưới đây).
- **Lưu vị trí bền vững qua các lần khởi động, theo từng màn hình** (`FE-020a`): vấn đề — `monitor_id` dùng trong `OverlayRect` (`03-ipc-communication.md` mục 3.3, do `Vision` gán) chỉ ổn định trong 1 phiên chạy, **không** dùng được làm khoá lưu trữ lâu dài xuyên suốt các lần khởi động. `Overlay` tự enumerate màn hình cục bộ (mục 4.1.1) đã có sẵn định danh ổn định hơn: `MONITORINFOEX.szDevice` (ví dụ `\\.\DISPLAY1`) — dùng làm khoá lưu trữ vị trí (ADR-67), **hoàn toàn tách biệt** khỏi `monitor_id` phía Vision/Service (2 khái niệm khác nhau, không trộn lẫn, đúng tinh thần đã áp dụng cho `overlay_id` ở mục 3.2).
- **`Overlay` không tự lưu file cấu hình riêng** (đúng nguyên tắc `Service` là nguồn sự thật duy nhất, `ADR-12`, và `Overlay` vốn không có quyền/thiết kế truy cập `config.db`, `SEC-017`) — vị trí icon đi qua `Service` như mọi state khác:
  - `IconPositionUpdate` (Overlay → Service, field 64, amendment `03-ipc-communication.md`): `{string device_name; int32 x; int32 y}` — gửi 1 lần mỗi khi user thả (drop) xong 1 lần kéo.
  - `IconLayoutSync` (Service → Overlay, field 65): `{repeated IconPositionUpdate positions}` — `Service` push ngay sau handshake, cùng thời điểm với `OverlayRectListCommand`/`MonitoringStatusUpdate` (mục 4.1 `03-ipc-communication.md`), chứa toàn bộ vị trí đã lưu cho mọi `device_name` từng ghi nhận. `Overlay` áp dụng cho màn hình nào có `device_name` khớp danh sách đang enumerate; màn hình không có bản ghi (lần đầu chạy, hoặc màn hình mới cắm chưa từng kéo) dùng vị trí mặc định (mục trên).
  - `Service` lưu vào bảng mới `icon_positions` trong `config.db` (amendment `04-data-architecture.md`, khoá chính `device_name`) — xem chi tiết schema ở amendment dưới.

#### 4.1.5 Banner nhắc tạm dừng (`S9`, `PAUSE-011`, bổ sung v0.2.2 — Đợt 5)

**Quyết định (ADR-108): tái dùng nguyên `ShowToastCommand`** (field 62, đã có sẵn từ Đợt 0, `03-ipc-communication.md` mục 3.2) — **không tạo message/schema IPC mới, không tạo 1 cửa sổ WinForms riêng cho banner**. Lý do khớp tự nhiên: Windows gọi chính xác kiểu thông báo popup ngắn hạn này là "banner" notification (phân biệt với "toast" kiểu cũ trước Windows 10 — cả 2 đi qua cùng 1 API `ToastNotificationManager`), đúng nghĩa đen `S9`/`PAUSE-011` ("Banner nhỏ nhắc app đang ở chế độ tạm dừng"); cùng mẫu hình tái dùng đã áp dụng cho `ANTI-060`/`ANTI-061` (`09-anti-tamper-architecture.md` ADR-100, tái dùng `IconState.ERROR` thay vì phát minh state mới).

- **Trigger**: `Service` gửi (không phải `Overlay` tự quyết định khi nào gửi — đúng nguyên tắc "`Overlay` là hàm render thuần", `ADR-12`) — thiết kế đầy đủ điều kiện/chu kỳ ở `02-process-architecture.md` mục 3a.3 (`PauseMonitor` tick 30 giây, ADR-102/103): lần đầu sau đúng **10 phút** kể từ `pause_started_at` (KHÔNG gửi ngay lúc vừa kích hoạt Pause), lặp lại mỗi 10 phút cho tới khi resume (thủ công hoặc tự động) — dừng ngay khi state rời `Running·Paused`, không có Toast "đã resume" bổ sung (icon `S8` đổi màu xanh đã đủ tín hiệu, tránh spam).
- **Nội dung**: `ShowToastCommand{severity=INFO, reason_code="PAUSE_REMINDER", text=<dựng sẵn, ví dụ "Giám sát đang tạm dừng — còn lại 3 giờ 42 phút">}` — `text` do `Service` build sẵn hoàn chỉnh (đúng nguyên tắc đã áp dụng cho mọi `ShowToastCommand` khác, `03-ipc-communication.md` mục 3.2: "`Overlay` KHÔNG rẽ nhánh/tự dịch theo `reason_code`"), tính từ `pause_expires_at_unix_ms - trusted_now` tại đúng thời điểm gửi. `Overlay` chỉ hiển thị y nguyên `text` nhận được — khác với đếm ngược hover tooltip ở icon (mục 4.1.2/4.1.3, nơi `Overlay` tự tính lại mỗi giây cục bộ), vì Toast là thông báo 1 lần tại 1 thời điểm, không phải hiển thị liên tục cần cập nhật real-time.
- **Không trùng lặp với `IconState.PAUSED`**: banner (`S9`, định kỳ, tự biến mất sau vài giây theo hành vi mặc định Windows Toast) và icon vàng + tooltip hover (`S8`, `FE-021`, luôn hiển thị liên tục suốt lúc Pause) là 2 cơ chế **bổ sung cho nhau**, không thay thế nhau — icon là tín hiệu thụ động luôn-hiện, banner là nhắc nhở chủ động định kỳ, đúng 2 mục đích khác nhau đã mô tả riêng biệt ở `Specification/03-frontend-ui-spec.md` (`S8` vs `S9`).

### 4.2 DPI awareness (ADR-61)

`ParentalGuard.Overlay` **bắt buộc** khai báo Per-Monitor DPI Aware V2 trong application manifest (`<dpiAwareness>PerMonitorV2</dpiAwareness>`, hoặc gọi `SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` sớm nhất trong `Program.Main`, trước khi tạo bất kỳ `Form` nào) — **điều kiện bắt buộc** để: (a) `GetDpiForWindow` (mục 2.3/2.4) trả đúng giá trị per-monitor thay vì DPI hệ thống chung; (b) WinForms tự scale đúng bounds khi 1 overlay nằm trên màn hình có scaling khác màn hình chính, tránh lệch vị trí/kích thước blur trên các màn hình phụ có DPI khác 100%. Thiếu khai báo này, toàn bộ công thức DPI-scale ở mục 2 sẽ sai trên máy multi-monitor DPI hỗn hợp (kịch bản phổ biến: màn hình laptop 150% + màn hình ngoài 100%).

### 4.3 Chế độ gộp và nhóm theo màn hình

Đã nói ở mục 3.2/3.3 (grouping key = `monitor_id` do `Vision` gán, Overlay tự resolve bounds thật qua `MonitorFromWindow` — không dùng số `monitor_id` để suy toạ độ, tránh vấn đề đối chiếu `monitor_id` giữa `Vision` và `Overlay` phải khớp số — 2 tiến trình **không bao giờ cần thống nhất ý nghĩa con số `monitor_id`**, chỉ cần dùng nó để "cùng nhóm hay khác nhóm", đúng thiết kế ở `03-ipc-communication.md` v0.3.0).

## 5. Bảng ADR (không map trực tiếp 1 Requirement ID)

| # | Quyết định | Lý do |
|---|---|---|
| ADR-53 | Tra cứu UI Automation chạy trên 1 `Thread` STA riêng mỗi lần, không `Task.Run`/ThreadPool | COM call `IUIAutomation` block đồng bộ, không cancel được giữa chừng — cô lập rủi ro treo vào đúng 1 thread, không cạn ThreadPool dùng chung với IPC; số lượng tra cứu đồng thời có trần tự nhiên nhờ `BE-088` (≤ 10 overlay) |
| ADR-54 | Overlay hiển thị ngay bằng vùng loại trừ lớp 3 (đồng bộ), nâng cấp lên lớp 1+2 bất đồng bộ nếu về kịp 150ms — không có đường code nào chờ UI Automation trước khi `Show()` | Đúng nghĩa đen `FE-016c` lớp 3 ("không bao giờ phải chờ UI Automation mà trễ việc che nội dung"); nút đóng gốc luôn nằm trong vùng fallback ở mọi thời điểm nên không có cửa sổ thời gian nào bị lộ |
| ADR-55 | Matching heuristic lớp 1: `FindAll` toàn bộ `Button` rồi lọc client-side theo `AutomationId`/`Name` khớp tập từ khoá "Close"/"Đóng", chọn ứng viên gần góc trên-phải nhất nếu nhiều kết quả | `IUIAutomation` core không hỗ trợ "contains" ở property condition; lọc client-side là cách chuẩn để làm "chứa chuỗi"; tie-break theo khoảng cách hình học giảm false positive khi cửa sổ có nhiều nút tên gần giống "Close" |
| ADR-56 | Z-index đồng bộ cục bộ tại `Overlay` bằng cách `EnumWindows` lấy thứ tự thật rồi `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)` lặp lại theo thứ tự back→front — không thêm field z-index vào IPC schema | `Service` (Session 0) không có quyền window-station để biết Z-order thật; `Overlay` đã ở đúng session, tự đủ dữ liệu; tránh round-trip IPC không cần thiết cho 1 việc thuần cục bộ |
| ADR-57 | Ngưỡng chế độ gộp có hysteresis: vào khi > 10 (`BE-088`), ra khi ≤ 8 | `BE-088` chỉ chốt ngưỡng vào; không có hysteresis sẽ gây "flapping" (bật/tắt chế độ gộp liên tục) khi số cửa sổ vi phạm dao động quanh biên 10-11 |
| ADR-58 | Overlay gộp tự resolve bounds toàn màn hình qua `MonitorFromWindow`+`GetMonitorInfo` (dùng `rcMonitor`) từ 1 `window_handle` đại diện, bỏ qua field `rect` nhận từ `Service` | Tránh phải xây kênh "báo cáo toạ độ màn hình" riêng từ `Vision`/`Service` lên `Overlay`; `Overlay` vốn đã có sẵn API Win32 phù hợp nhất cho việc này (đã ở đúng session tương tác) |
| ADR-59 | `merged_window_handles` gửi TOÀN BỘ danh sách hệ thống ở MỌI rect gộp (không chỉ subset theo màn hình); Overlay lặp gửi N `ForceCloseRequest` đơn (tái dùng message có sẵn), không thêm message batch mới | Đúng nghĩa đen `BE-089` ("toàn bộ...trên toàn hệ thống, không chỉ riêng màn hình chứa overlay đó"); tái dùng message hiện có giữ `Service`-side code không đổi ở `HandleForceCloseAsync` |
| ADR-60 | `FE-016` (vùng loại trừ) đình chỉ hoàn toàn ở overlay gộp — không carve bất kỳ vùng nào | **Cập nhật v0.2.0**: nay là quyết định sản phẩm đã CHỐT (`BE-088a`/`FE-016f`), không còn là suy luận kỹ thuật chờ duyệt — mục tiêu an toàn cốt lõi của `FE-016` được giữ qua nút "Tắt nội dung" duy nhất (`BE-089a`) + auto-timeout 30s (`BE-089b`) làm lối thoát dự phòng thứ 2 (`GEN-007b`) |
| ADR-61 | `Overlay` khai báo Per-Monitor DPI Aware V2 trong manifest | Bắt buộc để `GetDpiForWindow` và WinForms tự scale đúng trên máy nhiều màn hình có DPI khác nhau — không có lựa chọn nào khác cho multi-monitor đúng đắn |
| ADR-62 | ~~Icon trạng thái (`BE-083`) ở file này chỉ thiết kế cấu trúc — UX đầy đủ để ở Đợt xác định sau~~ | **SUPERSEDED v0.2.0**: chủ dự án quyết định làm luôn UX đầy đủ (`FE-020`–`022`) trong Đợt 2 — xem mục 4.1 và ADR-64 đến ADR-67 |
| ADR-63 | Bộ đếm auto-timeout 30 giây (`BE-089b`) dùng `System.Windows.Forms.Timer` (UI thread), tick mỗi 1 giây (không phải 1 timer bắn đúng 1 lần sau 30s) | Callback chạy trên UI thread chuẩn WinForms, tránh vấn đề cross-thread khi cần thao tác control/gọi Win32 API; tick 1s cho phép hook đếm ngược (mục 3.4.3) mà không đổi kiến trúc nếu UX quyết định hiển thị sau này |
| ADR-64 | Icon trạng thái dùng `WS_EX_LAYERED` + `UpdateLayeredWindow` (per-pixel alpha) để vẽ hình tròn/bo góc trên nền trong suốt, kích thước form nhỏ cố định (~40×40px) | Đúng thẩm mỹ Fluent (`FE-001`); form nhỏ tự nhiên đạt click-through ngoài icon (`FE-020`) mà không cần kỹ thuật `Region.Exclude` như overlay blur (mục 2) |
| ADR-65 | Trạng thái icon (`FE-021`) lấy từ message mới `MonitoringStatusUpdate` (Service → Overlay), ánh xạ trực tiếp từ state machine trung tâm (`02-process-architecture.md` mục 3) — không để `Overlay` tự suy luận | Nhất quán nguyên tắc "`Service` là nguồn sự thật duy nhất, Overlay là hàm render thuần" (`ADR-12`); tránh 2 nguồn suy luận trạng thái lệch nhau giữa `Service` và `Overlay` |
| ADR-66 | Khi pipe `Overlay`↔`Service` đứt, `Overlay` tự chuyển local toàn bộ icon sang `ERROR` ngay lập tức (không chờ push, vì sẽ không có push nào tới trong lúc đứt kết nối) | Tránh icon "đứng hình" hiển thị sai trạng thái khi đúng lúc mất kết nối lại là lúc `FE-021` cần báo "Lỗi-gián đoạn" nhất; nhất quán tinh thần phòng thủ cục bộ đã dùng ở `ADR-54` |
| ADR-67 | Vị trí icon sau kéo-thả (`FE-020a`) lưu theo khoá `MONITORINFOEX.szDevice` (không dùng `monitor_id` của `Vision`), đi qua 2 message mới `IconPositionUpdate`/`IconLayoutSync` và bảng `icon_positions` trong `config.db` (`Service` làm nguồn lưu trữ) | `monitor_id` chỉ ổn định trong 1 phiên chạy, không dùng được cho dữ liệu cần bền vững qua khởi động lại; `Overlay` không có quyền/thiết kế tự lưu file cấu hình riêng (`SEC-017`, `ADR-12`) |
| ADR-108 (v0.2.2) | Banner nhắc tạm dừng (`S9`, `PAUSE-011`) tái dùng nguyên `ShowToastCommand` (Windows Toast/banner notification có sẵn từ Đợt 0), không tạo message IPC hay cửa sổ WinForms riêng | Khớp tự nhiên ngữ nghĩa "banner" của Windows Toast; cùng mẫu hình tái dùng đã áp dụng cho `ANTI-060`/`ANTI-061` (`09` ADR-100); `Overlay` chỉ hiển thị `text` dựng sẵn, không tự tính/dịch lại |

## 6. Câu hỏi mở

- [ ] **Validate thực nghiệm** (như tiền lệ DXGI/Low IL ở `05` mục 8): `IUIAutomation` client-side gọi từ `Overlay` (Low Integrity Level, `06-security-architecture.md` mục 2.4) đọc cây accessibility của cửa sổ chạy Medium/High IL — cần xác nhận không bị UIPI chặn trên máy Windows thật trước khi khoá cứng lớp 1 vào code (đã có lớp 3 fallback không phụ thuộc, không chặn tiến độ).
- [ ] Con số padding lớp 2 (**16px**, mục 2.3) và fallback **160×50** (mục 2.4, kế thừa từ `FE-016c`) đều là giá trị tạm thời — cần đo thực tế trên danh sách app ưu tiên `BE-075` (Chrome/Edge/Firefox custom title bar, VLC, PotPlayer...) trước khi khoá cứng, đúng chỉ dẫn đã có sẵn ở spec.
- [x] ~~`FE-016` đình chỉ ở chế độ gộp (mục 3.4, ADR-60) là suy luận kỹ thuật từ `BE-088`/`089` — đề nghị chủ dự án xác nhận~~ — **ĐÃ CHỐT v0.2.0**: `BE-088a`/`BE-089a`/`BE-089b` + `FE-016f`/`FE-016g` (xem mục 1, 3.4).
- [ ] **`FE-016g` — đếm ngược 30s có hiển thị trực quan hay không** (`Specification/03-frontend-ui-spec.md` mục 9): chưa chốt UX. Thiết kế ở mục 3.4.3 đã tách rời cơ chế đếm khỏi hiển thị để cả 2 khả năng đều triển khai được mà không đổi kiến trúc lõi — chờ chủ dự án quyết định UX trước khi `feature-dev` bật/tắt phần hiển thị.
- [x] ~~Đợt nào phụ trách UX đầy đủ của icon trạng thái (`FE-020`–`022`...)~~ — **ĐÃ CHỐT**: chủ dự án quyết định làm luôn ở Đợt 2, xem mục 4.1.
- [x] ~~Banner pause (`S9`, `PAUSE-0xx`) vẫn chưa thiết kế UX~~ — **Đã xong** (Đợt 5, mục 4.1.5: tái dùng `ShowToastCommand`, trigger/nội dung thiết kế đầy đủ ở `02-process-architecture.md` mục 3a.3, ADR-108).
- [ ] Icon glyph/mã màu hex cụ thể cho 3 trạng thái (`FE-021`) — chi tiết asset/theme, không chặn thiết kế kiến trúc (mục 4.1.1), để `feature-dev`/thiết kế UI quyết định lúc implement theo Design System đã có ở `03-frontend-ui-spec.md` mục 4.

## 7. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.2.2 | 2026-09-20 | PATCH — Đợt 5 (`ROADMAP.md`, Pause/Resume), amendment cùng lượt viết `02-process-architecture.md` mục 3a. Thêm mục 4.1.5: thiết kế banner nhắc tạm dừng (`S9`, `PAUSE-011`) — tái dùng nguyên `ShowToastCommand` đã có, không message/state UI mới (ADR-108), trigger/chu kỳ/nội dung do `Service` quyết định (thiết kế đầy đủ ở `02` mục 3a.3). Đóng câu hỏi mở "Banner pause vẫn chưa thiết kế UX" (mục 6). Giữ nguyên trạng thái `Draft` (chưa đổi thành Approved), vẫn chờ review tuần tự theo đúng thứ tự `00-INDEX.md` |
| v0.2.1 | 2026-09-19 | PATCH — amendment cùng lượt viết `09-anti-tamper-architecture.md` (Đợt 4). Mở rộng bảng ánh xạ `IconState` (mục 4.1.2) thêm 1 dòng: cửa sổ nghi vấn tấn công theo `ANTI-060` cũng map sang `ERROR`, nhưng **giữ liên tục** (không chỉ thoáng qua) cho tới hết cửa sổ T phút không có sự kiện mới — tái dùng nguyên kênh hiển thị đã có, không tạo state/message mới (ADR-100 ở `09`). `Overlay` không cần phân biệt nguyên nhân, vẫn đúng nguyên tắc "hàm render thuần". Giữ nguyên trạng thái `Draft` (chưa đổi thành Approved), vẫn chờ review tuần tự theo đúng thứ tự `00-INDEX.md` |
| v0.2.0 | 2026-09-19 | **MINOR — cập nhật theo 2 quyết định sản phẩm mới chốt qua `spec-maintainer`** (`BE-088a`/`BE-089a`/`BE-089b` v0.13.0, `FE-016f`/`FE-016g` v0.9.0): (1) Viết lại hoàn toàn mục 3.4 — thay "FE-016 đình chỉ ở chế độ gộp, flagged chờ review" (v0.1.0) bằng "full-screen lock đã CHỐT chính thức" + thiết kế mới auto-timeout 30 giây cục bộ (mục 3.4.2, `System.Windows.Forms.Timer`, ADR-63) làm lối thoát dự phòng thứ 2 (`GEN-007b`) + hook đếm ngược bindable cho UX chưa chốt (mục 3.4.3, không tự quyết định hiển thị) + audit log phân biệt nguồn `"manual"`/`"auto-timeout"` (mục 3.4.4). Cập nhật mục 3.3 (`Source` field trong `ForceCloseRequest`). (2) Mở rộng đầy đủ UX icon trạng thái multi-monitor (mục 4.1, ADR-64 đến ADR-67): cấu trúc render layered-window, nguồn dữ liệu trạng thái qua message mới `MonitoringStatusUpdate` ánh xạ từ state machine trung tâm, tooltip, kéo-thả + lưu vị trí bền vững qua `szDevice` + 2 message mới `IconPositionUpdate`/`IconLayoutSync`. Cập nhật ADR-60/62 (superseded), thêm ADR-63 đến ADR-67. Chốt 2 câu hỏi mở cũ, thêm 3 câu hỏi mở mới (đếm ngược UX chưa chốt, banner pause ngoài phạm vi, icon asset chi tiết). Viết cùng lượt amendment `03-ipc-communication.md` v0.3.0→**v0.4.0** (field `source`/`CloseSource` trong `ForceCloseRequest`; message mới `MonitoringStatusUpdate`/`IconPositionUpdate`/`IconLayoutSync`, field 63-65 kênh Overlay) và `04-data-architecture.md` v0.1.0→**v0.2.0** (event `ForceCloseRequested` thêm vào bảng event_type + field `source`; bảng mới `icon_positions`). Theo chỉ đạo chủ dự án — không dừng chờ review, đủ điều kiện giao `feature-dev` code Đợt 2 sau lượt này. |
| v0.1.0 | 2026-09-19 | Khởi tạo — file mới cho Đợt 2 (`ROADMAP.md`), đánh số `07` (dồn `07`→`08`/`08`→`09`/`09`→`10`/`10`→`11` cho 4 file "chưa viết", xem mục 0 + `00-INDEX.md`). Vùng loại trừ nút đóng 3 lớp (`FE-016c`): hiển thị overlay ngay bằng fallback đồng bộ, nâng cấp UI Automation bất đồng bộ trong 150ms (ADR-53/54), matching heuristic client-side (ADR-55), padding 16px lớp 2 (tạm thời). Đa cửa sổ: xác nhận `BE-084`–`086` đã đúng cấu trúc từ code Đợt 1, thêm z-index cục bộ qua `SetWindowPos` lặp (ADR-56, `BE-087`), giới hạn 10 + hysteresis 8 (ADR-57, `BE-088`), chế độ gộp full-system handles + resolve bounds cục bộ (ADR-58/59, `BE-089`), đình chỉ `FE-016` ở chế độ gộp — flagged rõ (ADR-60). Multi-monitor: icon cấu trúc + hot-plug qua `WM_DISPLAYCHANGE` (`BE-083`), DPI awareness bắt buộc (ADR-61, `BE-080`). 10 ADR (53-62), 4 câu hỏi mở. Viết cùng lượt với amendment `03-ipc-communication.md` v0.3.0 (2 field `OverlayRect`) và `05-image-pipeline-architecture.md` v0.2.0 (mục 3.5 multi-monitor phía Vision) — theo chỉ đạo chủ dự án, không dừng chờ review từng file, nhưng dừng lại báo cáo sau khi xong cả 3 file amendment + 1 file mới này (đúng chỉ đạo riêng cho lượt viết Đợt 2). |
