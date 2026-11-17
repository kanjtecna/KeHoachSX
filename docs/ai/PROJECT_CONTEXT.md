# Project Context — KeHoachSX

## 1. Mục tiêu

[VERIFIED-CODE + VERIFIED-DOC] KeHoachSX là ứng dụng lập kế hoạch sản xuất và tự động phân công công việc cho công nhân công đoạn lắp ráp. UI hiện hiển thị kế hoạch tuần, các Lot, lịch phân công theo công nhân/ngày, tải công việc và thông tin ma trận kỹ năng.

Tên màn hình hiện tại trong Blazor là **Kế Hoạch & Phân Công Sản Xuất Lắp Ráp**.

## 2. Chức năng quan sát được

- Nhập kế hoạch tuần từ CSV hoặc Excel.
- Chuẩn hóa dòng kế hoạch thành `WeeklyPlanItem`.
- Từ mỗi dòng kế hoạch, tạo một hoặc nhiều `LotTask`.
- Tra cứu giờ công định mức từ file kế hoạch hoặc nguồn Oracle/SQL Server.
- Lấy danh sách công nhân lắp ráp đang hoạt động từ SQL Server, có nguồn JSON dự phòng.
- Nạp dữ liệu kỹ năng từ hai file CSV.
- Chạy thuật toán tự động phân công.
- Hiển thị KPI: tổng giờ kế hoạch, năng lực tuần, tỷ lệ lấp đầy và số Lot đã/chưa phân bổ.
- Lọc công nhân/mã sản phẩm, xem chi tiết Lot và cho phép điều chỉnh phân công thủ công (theo code UI hiện tại).

Không suy diễn các chức năng chưa thấy trong repository.

## 3. Người dùng và dữ liệu

[VERIFIED-CODE] Nhóm công nhân được lấy trong các nhóm LR1, LR2, LR3, LR4. Trạng thái nhân viên phải là '1'. Các cấp CHIEF, LEADE, LEADER bị loại khỏi danh sách đủ điều kiện.

Các bảng SQL Server được dùng trong mã hiện tại:
- `STOCKMANAGEMENT.dbo.NHANVIEN`
- `STOCKMANAGEMENT.dbo.HIEUSUATNHANVIEN`
- `STOCKMANAGEMENT.dbo.SANLUONGCAMLR`

Thông tin bảng và cột phải được đối chiếu với DB schema thực tế trước khi thay đổi.

## 4. Công nghệ quan sát được

- .NET 8.
- C# với nullable reference types và implicit usings.
- Blazor UI.
- Windows Forms + BlazorWebView desktop host.
- Dapper + Microsoft.Data.SqlClient cho SQL Server.
- ClosedXML cho Excel.
- JSON và CSV làm nguồn dữ liệu dự phòng/ma trận kỹ năng.
- Oracle được bọc qua adapter dùng dịch vụ chia sẻ JtecShare.
- Microsoft.Extensions.DependencyInjection và IConfiguration.
- GitHub repository: `kanjtecna/KeHoachSX`.

Không tự ý nâng .NET, thay provider, thay package hoặc chuyển UI framework như một phần của nhiệm vụ khác.

## 5. Hosts và tích hợp

- `KeHoachSX.App` là desktop host WinForms + Blazor Hybrid.
- `KeHoachSX.Api` hiện có project reference tới Core, Application, Infrastructure và ASP.NET Core framework reference. Từ các file đã xem, chưa xác nhận API endpoints/host implementation; không mô tả API như đã hoàn thiện.
- Solution phụ thuộc các project JtecShare ở đường dẫn tương đối bên ngoài repository. Agent phải kiểm tra các đường dẫn này trong môi trường local trước khi kết luận build.

## 6. Phạm vi không được giả định

Chưa xác minh được:
- Cơ chế lưu lâu dài kết quả phân công hoặc kế hoạch đã upload.
- Quy trình phê duyệt kế hoạch.
- Cách quản lý nghỉ phép/ngày nghỉ của từng công nhân trong source code hiện tại.
- Bảng dữ liệu bàn gá/số bộ bàn gá và quy tắc ràng buộc máy/bàn gá.
- Quy tắc khóa người đã được phân công cho một SEQ cụ thể qua các ngày.
- API contract và authentication.
- Cơ chế đồng bộ/phiên bản hóa snapshot JSON/CSV.
- Test project và coverage thực tế.

Mỗi nhiệm vụ liên quan những nội dung trên phải khảo sát và xác nhận trước.


## 7. Target scheduling capabilities supplied by the owner (not implemented yet)

- Default view Today, with Today/Tomorrow/Whole-week scopes.
- A production week is Friday through the following Thursday, seven dates.
- Users import product plans independently per assembly group.
- Working availability is date-specific and includes company holidays, employee overtime and approved leave.
- On normal days, valid overtime extends the regular work timeline; on holidays, only employees with valid overtime may work and only for their registered overtime hours.
- Jig/fixture category and physical set count from `dulieubanga.xlsx` affect the allowed worker/team arrangement and concurrency.

Detailed rules, evidence labels and unresolved conditions are in `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md`. These are target requirements, not current implemented behavior. Current UI/algorithm still has a six-day in-memory model and must not be described as already supporting this calendar.
