# tests/

Khung thư mục test, mirror theo `src/` — mỗi project code sẽ có project test tương ứng (`xUnit`/`NUnit`, xem `Specification/11-testing-qa-process.md` mục 2). Chưa tạo project test thật, tạo cùng lúc với `.csproj` thật của từng process trong `src/`.

Quy trình test/debug/report dùng agent `test-runner`, `security-privacy-auditor`, `debugger`, `release-reporter` (`.claude/agents/`) theo đúng Feature Gate ở `TEST-002`.
