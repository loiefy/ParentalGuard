# 12 — Dev Process & Code Management Standards

> Version: v0.1.0 | Trạng thái: Draft | Cập nhật: 2026-09-17

## 1. Mục đích & phạm vi

Đây là bộ quy tắc quản lý mã nguồn/dự án — khác bản chất với 11 file kia (những file đó là requirement sản phẩm). File này quy định: cấu hình GitHub, quản lý secret/key, code style, quy trình review/release. Áp dụng từ khi bắt đầu code (System Design trở đi), nhưng thiết lập GitHub/repo có thể làm sớm hơn nếu cần.

Requirement ID prefix mới: `DEV-xxx` (bổ sung vào bảng ở `00-INDEX.md` mục 3).

Bối cảnh quan trọng ảnh hưởng toàn bộ tài liệu này:
- Mã nguồn mở, nhưng bản build đóng gói được **bán thương mại** (đã chốt ở phiên thảo luận trước).
- App **zero-network tuyệt đối** (`GEN-034`) — không gọi API nào, nên bề mặt "secret" nhỏ hơn hẳn dự án thông thường (không có API key backend, không có connection string, không có OAuth secret...). Secret thực sự đáng lo chỉ có: **chứng chỉ ký số code signing**.
- Quy trình dev dùng Claude Code Agent cho Dev/Test/Debug/Report (`TEST-002`), Approve do chủ dự án (`TEST-003`).

## 2. Cấu hình GitHub repository

- `DEV-001`: **Visibility**: Public (khớp quyết định mã nguồn mở). Repo riêng cho code, tách biệt khỏi tài liệu spec nếu muốn (tuỳ chọn — không bắt buộc phải cùng 1 repo).
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
- `DEV-012`: **Chứng chỉ EV Code Signing** (secret quan trọng nhất của dự án, liên kết `SEC-030`): theo baseline requirement hiện hành của CA/Browser Forum, chứng chỉ EV **bắt buộc lưu trên HSM hoặc USB token vật lý** (ví dụ YubiKey, SafeNet) — đây là ràng buộc từ chính CA phát hành (DigiCert, Sectigo...), không phải chỉ là khuyến nghị nội bộ, và về bản chất **không thể export thành file mềm** để đưa vào git dù có muốn. Máy ký release phải là máy vật lý riêng hoặc runner CI có gắn token, không lưu bản sao ở máy dev thông thường.
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

## 5. Liên kết với quy trình Agent (TEST-002/TEST-003)

- `DEV-030`: GitHub Actions CI (build + `dotnet format` + unit/integration test) đóng vai trò **lớp kiểm tra khách quan độc lập**, chạy song song/bổ trợ cho Claude Code Agent (`TEST-002`) — không thay thế nhau. Agent làm Dev/Test/Debug/Report chi tiết theo checklist; CI đảm bảo mọi PR đều qua 1 cổng kiểm tra tối thiểu, không phụ thuộc hoàn toàn vào việc Agent đã chạy đúng hay chưa.
- `DEV-031`: Approve cuối cùng (merge PR vào `main`) vẫn do chủ dự án thực hiện thủ công (`TEST-003`), dù Dev/Test do Agent hỗ trợ đến đâu — khớp nguyên tắc "không tự test tự duyệt" đã chốt.

## 6. Câu hỏi mở

- [ ] Có bắt buộc GPG/SSH signed commits ngay từ đầu (`DEV-004`), hay để tuỳ chọn/khuyến nghị mềm giai đoạn đầu vì có thể gây friction khi mới bắt đầu code?
- [ ] Chọn nhà cung cấp EV Code Signing Certificate nào (DigiCert, Sectigo, GlobalSign...) — ảnh hưởng tới quy trình xin cấp + loại HSM/USB token cụ thể ở `DEV-012`, chưa quyết định trong tài liệu này.
- [ ] Repo code và repo/thư mục spec (`Specification/`) có nên tách thành 2 repo GitHub riêng biệt không, hay giữ chung 1 repo?

## 7. Changelog file này

| Version | Ngày | Thay đổi |
|---|---|---|
| v0.1.0 | 2026-09-17 | Khởi tạo — quy tắc GitHub settings, quản lý secret/key (trọng tâm: chứng chỉ EV code signing, vì app zero-network không có secret runtime truyền thống), code styling, liên kết quy trình Agent đã chốt ở `11-testing-qa-process.md` |
