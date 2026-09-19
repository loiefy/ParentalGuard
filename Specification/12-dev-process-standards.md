# 12 — Dev Process & Code Management Standards

> Version: v0.4.1 | Trạng thái: Approved | Cập nhật: 2026-09-17

## 1. Mục đích & phạm vi

Đây là bộ quy tắc quản lý mã nguồn/dự án — khác bản chất với 11 file kia (những file đó là requirement sản phẩm). File này quy định: cấu hình GitHub, quản lý secret/key, code style, quy trình review/release. Áp dụng từ khi bắt đầu code (System Design trở đi), nhưng thiết lập GitHub/repo có thể làm sớm hơn nếu cần.

Requirement ID prefix mới: `DEV-xxx` (bổ sung vào bảng ở `00-INDEX.md` mục 3).

Bối cảnh quan trọng ảnh hưởng toàn bộ tài liệu này:
- **ĐÃ CHỐT (v0.2.0)**: Dự án **hoàn toàn miễn phí và mã nguồn mở** — bỏ hẳn kế hoạch bán thương mại đã cân nhắc trước đó (lý do: không có cơ chế purchase/account management vì app zero-network, nên về bản chất không thể enforce "chỉ mua mới dùng được"; đồng thời source đã mở nên license-gating vô nghĩa về kỹ thuật). Điều này ảnh hưởng trực tiếp tới ngân sách cho chứng chỉ ký số (xem `DEV-012`) — không còn nguồn thu để tự trả phí EV certificate hàng năm.
- App **zero-network tuyệt đối** (`GEN-034`) — không gọi API nào, nên bề mặt "secret" nhỏ hơn hẳn dự án thông thường (không có API key backend, không có connection string, không có OAuth secret...). Secret thực sự đáng lo chỉ có: **chứng chỉ ký số code signing**.
- Quy trình dev dùng Claude Code Agent cho Dev/Test/Debug/Report (`TEST-002`), Approve do chủ dự án (`TEST-003`).

## 2. Cấu hình GitHub repository

- `DEV-001`: **Visibility**: Public (khớp quyết định mã nguồn mở).
- `DEV-001a` (ĐÃ CHỐT v0.3.0): **Dùng chung 1 repo GitHub duy nhất** cho cả code lẫn tài liệu (`Specification/`, `Architecture/`) — không tách repo riêng. Lý do: dự án nhỏ, 1 người maintain, giữ chung giúp lịch sử commit/PR liên kết trực tiếp code ↔ Requirement ID mà không phải đồng bộ thủ công giữa 2 repo.
- `DEV-002`: **Branch protection cho `main`**:
  - Không cho phép push trực tiếp vào `main`, kể cả chủ dự án — mọi thay đổi qua Pull Request.
  - Bắt buộc CI (mục 5) pass trước khi merge (build thành công + test unit/integration pass + `dotnet format --verify-no-changes`).
  - Bắt buộc branch up-to-date với `main` trước khi merge (tránh merge code đã lỗi thời).
  - Không cho phép force-push vào `main`.
  - Lý do áp dụng kể cả khi chỉ 1 người Approve: PR tạo ra 1 điểm dừng bắt buộc để tự review diff + để CI chạy, giảm rủi ro commit lỗi/secret lọt thẳng vào `main`.
- `DEV-003`: **`.gitignore`** chuẩn cho .NET/WinUI 3 — tối thiểu phải chặn: `bin/`, `obj/`, `.vs/`, `*.user`, `*.suo`, `publish/`, `AppPackages/`, `BundleArtifacts/`, và toàn bộ nhóm file secret liệt kê ở `DEV-011`.
- `DEV-004`: **Signed commits** (GPG hoặc SSH signing) khuyến nghị bật cho `main` — tăng độ tin cậy nguồn gốc commit, phù hợp tinh thần minh bạch của 1 app bảo mật/giám sát (đã có nguyên tắc minh bạch ở `SEC-003`).
- `DEV-005`: **Dependabot/security alerts** bật cho repo (NuGet packages) — cảnh báo tự động khi dependency có lỗ hổng đã biết (CVE), review định kỳ.
- `DEV-006`: **`SECURITY.md`** — công bố quy trình báo lỗ hổng bảo mật có trách nhiệm (responsible disclosure), kênh liên hệ riêng (không public issue cho lỗ hổng chưa vá) — liên kết tinh thần với `SEC-031`/`MISC-070` (Software Behavior Disclosure) đã có ở `04-security-spec.md`/`10-additional-mechanisms-spec.md`.
- `DEV-007`: **`CODEOWNERS`** — dù hiện chỉ 1 người Approve (`TEST-003`), vẫn định nghĩa rõ để dễ mở rộng về sau, và để GitHub tự động request review đúng người.
- `DEV-008`: **Issue/PR template** — template PR bắt buộc có mục "Requirement ID liên quan" (map về `BE-xxx`/`SEC-xxx`/... trong spec) và "Checklist test đã chạy" (map về mục 3 ở `11-testing-qa-process.md`) — giữ liên kết truy vết spec ↔ code ↔ test xuyên suốt dự án.
- `DEV-009`: **Release**: git tag theo semver (khớp quy ước version đã dùng cho spec), GitHub Releases đính kèm installer đã ký số + file `SHA256SUMS` để người dùng tự verify checksum trước khi cài.

## 3. Quản lý secret/key

- `DEV-010`: **Nguyên tắc tuyệt đối**: không bao giờ commit bất kỳ secret nào vào git dưới bất kỳ hình thức nào (kể cả trong lịch sử commit cũ, kể cả branch đã xoá — nếu lỡ commit phải rotate secret + rewrite history, không chỉ xoá ở commit sau).
- `DEV-011`: **Danh sách file/pattern phải nằm trong `.gitignore`**: `*.pfx`, `*.p12`, `*.snk`, `*.cer` (nếu chứa private key), `.env`, `appsettings.*.local.json`, `secrets.json`, bất kỳ file nào có "secret"/"key"/"credential" trong tên.
- `DEV-012` (ĐÃ CHỐT v0.2.0 — sửa theo mô hình free/open-source): **Chứng chỉ ký số** (liên kết `SEC-030`): vì dự án không còn nguồn thu, **không tự mua EV Code Signing Certificate** (chi phí hàng năm + HSM/USB token vật lý, không khả thi cho dự án cộng đồng miễn phí). Dùng **chứng chỉ OV miễn phí qua chương trình SignPath Foundation cho dự án open-source** (`signpath.io/solutions/open-source-community`) — ký qua pipeline CI/CD tích hợp GitHub Actions, private key nằm hoàn toàn trên HSM của SignPath. Nhờ vậy dự án **không bao giờ cầm/quản lý private key ký số nào cả** — đơn giản hoá đáng kể so với phương án tự mua/tự giữ chứng chỉ. Điều kiện tham gia: cần đã có ít nhất 1 bản release công khai trước, nên bản release đầu tiên có thể cần phát hành tạm thời chưa ký hoặc dùng chứng chỉ cá nhân rẻ tiền, rồi apply SignPath ngay sau đó.
- `DEV-013`: Nếu dùng GitHub Actions để tự động hoá bước ký số: dùng **Encrypted Secrets** (repo/environment secrets của GitHub), giới hạn workflow nào được đọc secret đó, **không** `echo`/log giá trị secret ra console ở bất kỳ bước nào (bật `::add-mask::` nếu cần).
- `DEV-014`: Cài **`gitleaks`** (hoặc tương đương) làm pre-commit hook + chạy lại trong CI — quét tự động phát hiện secret vô tình bị stage/commit trước khi nó thực sự vào lịch sử git.
- `DEV-015`: Với secret cấu hình cho local dev (nếu phát sinh, ví dụ token test nội bộ) — dùng `dotnet user-secrets` (lưu ngoài thư mục repo, trong profile máy dev), không hardcode trong code hay commit file config chứa giá trị thật.
- `DEV-016` (ghi chú, không phải requirement mới): Vì `GEN-034` đã chốt app zero-network tuyệt đối, dự án này **không có** API key/connection string/OAuth secret nào cần quản lý ở runtime — toàn bộ nhóm secret ở mục này chỉ xoay quanh **build & release** (ký số), không phải secret app dùng lúc chạy.

## 4. Code styling

- `DEV-020`: Dùng **`.editorconfig`** chuẩn theo convention chính thức của .NET (Microsoft C# Coding Conventions), áp dụng đồng nhất cho toàn repo, enforce bằng `dotnet format --verify-no-changes` trong CI (fail build nếu code chưa format đúng).
- `DEV-021`: Naming convention chuẩn C#: `PascalCase` cho type/public member/method, `camelCase` cho local variable/private field (có thể prefix `_` cho private field theo convention phổ biến), `IPascalCase` cho interface, hằng số `PascalCase` (không dùng `ALL_CAPS` kiểu C++).
- `DEV-022`: Bật `<Nullable>enable</Nullable>` cho toàn bộ project — bắt buộc, không tuỳ chọn, vì đây là app xử lý nhiều buffer/con trỏ nhạy cảm (ảnh, token, mật khẩu), giảm rủi ro null-reference bug ở đúng những chỗ cần chắc chắn nhất.
- `DEV-023`: Bật bộ **security analyzer** của .NET (`Microsoft.CodeAnalysis.NetAnalyzers`, nhóm rule `CA2xxx`/`CA3xxx` — crypto, deserialization, injection...) — set **warning-as-error** riêng cho nhóm rule bảo mật, các nhóm khác (style, performance) có thể để warning thường.
- `DEV-024`: Commit message theo **Conventional Commits** (`feat:`, `fix:`, `security:`, `docs:`, `chore:`, `test:`...) — hỗ trợ generate changelog tự động sau này, nhất quán với thói quen changelog chi tiết đã dùng xuyên suốt bộ spec này.
- `DEV-025`: Code review checklist bắt buộc đối chiếu `TEST-001` — PR đụng tới vùng bảo mật/privacy (`PWD-*`, `ANTI-*`, `IMG-0*`, `SEC-*`) không được merge nếu checklist test tương ứng ở `11-testing-qa-process.md` mục 3 chưa pass.
- `DEV-026` (mới v0.4.0): **Code tinh gọn (lean code)** — chỉ code đúng phạm vi bài toán đã định nghĩa trong Requirement ID đang implement, không viết thêm tính năng/abstraction/config ngoài spec hiện tại (không thiết kế đón đầu nhu cầu tương lai chưa có requirement). Không comment mô tả "code này làm gì" (tên hàm/biến rõ ràng phải tự nói lên điều đó) — chỉ comment khi giải thích lý do (WHY) không hiển nhiên: constraint ẩn, workaround cho 1 vấn đề cụ thể, invariant dễ gây nhầm lẫn nếu không có ghi chú.

## 5. Liên kết với quy trình Agent (TEST-002/TEST-003)

- `DEV-030`: GitHub Actions CI (build + `dotnet format` + unit/integration test) đóng vai trò **lớp kiểm tra khách quan độc lập**, chạy song song/bổ trợ cho Claude Code Agent (`TEST-002`) — không thay thế nhau. Agent làm Dev/Test/Debug/Report chi tiết theo checklist; CI đảm bảo mọi PR đều qua 1 cổng kiểm tra tối thiểu, không phụ thuộc hoàn toàn vào việc Agent đã chạy đúng hay chưa.
- `DEV-031`: Approve cuối cùng (merge PR vào `main`) vẫn do chủ dự án thực hiện thủ công (`TEST-003`), dù Dev/Test do Agent hỗ trợ đến đâu — khớp nguyên tắc "không tự test tự duyệt" đã chốt.

## 6. Đánh giá fail case trước khi code & Dependency Mapping (mới v0.4.0)

- `DEV-040`: Trước khi bắt đầu viết code cho 1 feature, bắt buộc có 1 bước riêng đánh giá **fail case/exception** có khả năng phát sinh (input không hợp lệ, race condition, I/O lỗi, timeout IPC, tài nguyên không khả dụng...) — không được để phát sinh và xử lý ngẫu hứng giữa lúc code.
- `DEV-041`: Nếu fail case phát hiện ở `DEV-040` cho thấy hành vi mong muốn **chưa được mô tả hoặc mâu thuẫn** với spec hiện tại (ví dụ chưa rõ hành vi fail-secure/fail-safe cho 1 tình huống cụ thể) → phải quay lại cập nhật `Specification/` tương ứng trước (đúng quy trình archive bắt buộc ở `CLAUDE.md`/`00-INDEX.md` mục 4), rồi mới viết code xử lý exception đó. Không tự quyết định hành vi exception rồi code thẳng mà không phản ánh lại spec — tránh code và tài liệu lệch nhau.
- `DEV-042`: Dự án duy trì 1 **Dependency Map** — bảng/tài liệu truy vấn được, liệt kê mỗi hàm/function: tên, file chứa, danh sách hàm gọi nó (callers) và danh sách hàm nó gọi (callees). Mục đích: khi cần sửa 1 hàm, chỉ tra map để xác định phạm vi ảnh hưởng (impact analysis) và đọc/sửa đúng các hàm liên quan, không phải đọc lại toàn bộ codebase mỗi lần. Định dạng/công cụ sinh Dependency Map cụ thể (thủ công hay tự động hoá bằng Agent) thuộc phạm vi `Architecture/10-dev-automation-architecture.md` (HOW), không quyết ở đây.
- `DEV-043`: Mỗi lần tạo hàm mới, xoá hàm, hoặc thay đổi quan hệ gọi (thêm/bớt lệnh gọi hàm khác), bắt buộc cập nhật lại Dependency Map trong cùng commit/PR — không coi là việc làm sau, không được merge nếu Dependency Map chưa đồng bộ với thay đổi.

## 7. Câu hỏi mở

- [ ] Có bắt buộc GPG/SSH signed commits ngay từ đầu (`DEV-004`), hay để tuỳ chọn/khuyến nghị mềm giai đoạn đầu vì có thể gây friction khi mới bắt đầu code?
- [ ] (Mới, v0.2.0) Bản release đầu tiên (trước khi đủ điều kiện tham gia SignPath, xem `DEV-012`) nên phát hành chưa ký số, hay bỏ tiền túi mua 1 chứng chỉ OV cá nhân rẻ tiền cho riêng lần đầu?
- [ ] (Mới, v0.4.0) Dependency Map (`DEV-042`) nên là file tĩnh trong repo (ví dụ Markdown/JSON generate bằng tool) hay 1 index sống do Agent tự build lại mỗi phiên — quyết định cụ thể để ở `Architecture/10-dev-automation-architecture.md`.

## 8. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.4.1 | 2026-09-17 | Chủ dự án approve toàn bộ requirement trong file này — chuyển trạng thái file từ `Draft` sang `Approved` |
| v0.4.0 | 2026-09-17 | **Thêm nguyên tắc viết code mới**: `DEV-026` — code tinh gọn, không viết thừa, không comment rườm rà (mục 4). `DEV-040`–`DEV-043` (mục 6 mới) — bắt buộc đánh giá fail case/exception trước khi code, cập nhật spec tương ứng cho exception mới trước khi code xử lý; bắt buộc duy trì Dependency Map (hàm ↔ file ↔ caller/callee) để hỗ trợ impact analysis, cập nhật map mỗi lần thêm/sửa/xoá hàm hoặc quan hệ gọi. Thêm câu hỏi mở về định dạng Dependency Map |
| v0.3.0 | 2026-09-17 | Chốt câu hỏi mở: dùng chung 1 repo GitHub cho code + tài liệu (`DEV-001a` mới, thay thế phần "tuỳ chọn" cũ ở `DEV-001`). Đồng bộ với `ADR-11` mới ở `Architecture/01-tong-quan-kien-truc.md` |
| v0.2.0 | 2026-09-17 | **Chốt**: dự án chuyển hẳn sang free/open-source, bỏ kế hoạch bán thương mại. Sửa `DEV-012`: bỏ EV Code Signing Certificate tự mua, thay bằng chứng chỉ OV miễn phí qua SignPath Foundation (private key SignPath tự giữ, không phải dự án quản lý). Đồng bộ `SEC-030` ở `04-security-spec.md`. Trả lời 1/3 câu hỏi mở (nhà cung cấp EV cert không còn liên quan), phát sinh câu hỏi mở mới về bản release đầu tiên trước khi đủ điều kiện SignPath |
| v0.1.0 | 2026-09-17 | Khởi tạo — quy tắc GitHub settings, quản lý secret/key (trọng tâm: chứng chỉ EV code signing, vì app zero-network không có secret runtime truyền thống), code styling, liên kết quy trình Agent đã chốt ở `11-testing-qa-process.md` |
