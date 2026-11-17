# KeHoachSX — AI Project Knowledge Base

Tài liệu này là mục lục cho mọi AI agent làm việc trong repository.

## Đọc theo thứ tự

1. `../../AGENTS.md` — quy tắc bắt buộc.
2. `PROJECT_CONTEXT.md` — mục tiêu, phạm vi và công nghệ.
3. `ARCHITECTURE.md` — cấu trúc project, dependency và luồng dữ liệu.
4. `BUSINESS_RULES.md` — nghiệp vụ và phân biệt trạng thái hiện tại/yêu cầu.
5. `DATA_SOURCES.md` — SQL Server, Oracle, JSON, CSV, Excel.
6. `KNOWN_GAPS.md` — mâu thuẫn, rủi ro và câu hỏi chưa xác nhận.
7. `AI_WORKFLOW.md` — quy trình giao nhiệm vụ và phối hợp công cụ.
8. `TASK_TEMPLATE.md` — mẫu yêu cầu cho từng thay đổi.
9. `decisions/` — quyết định kiến trúc đã được chủ dự án duyệt.

## Quy ước trạng thái thông tin

- **[VERIFIED-CODE]**: xác minh trực tiếp trong mã nguồn trên branch được ghi trong tài liệu.
- **[VERIFIED-DOC]**: được ghi trong tài liệu/ghi chú trong repository.
- **[APPROVED]**: chủ dự án đã xác nhận là quy tắc phải tuân thủ.
- **[PROPOSED]**: đề xuất, chưa phải hành vi bắt buộc.
- **[OPEN]**: chưa đủ thông tin; AI phải hỏi hoặc đánh dấu giả định.

Không được chuyển trạng thái [OPEN] hoặc [PROPOSED] thành [APPROVED] nếu không có sự xác nhận.

## Baseline

Tài liệu ban đầu được lập từ branch `main`, commit `08cc1b68d30ddc067f25ade9e13b355b26a9535e`, ngày 2026-11-16 (UTC). Khi branch thay đổi đáng kể, phải rà soát lại.
