# Architecture — KeHoachSX

## 1. Trạng thái và nguồn

Mô tả này phản ánh source code trên `main` commit `08cc1b68d30ddc067f25ade9e13b355b26a9535e` cùng `ARCHITECTURE.md` hiện có tại root. Đây là mô tả kiến trúc quan sát được, không phải giấy phép tái cấu trúc tùy ý.

## 2. Solution projects

| Project | Vai trò quan sát được | Trách nhiệm đúng |
|---|---|---|
| `KeHoachSX.Core` | `Models/KeHoachSXModels.cs`, `ProductionPlanner.cs` | Model/domain data structures, quy tắc thuần khi được đặt ở đây |
| `KeHoachSX.Application` | `Services/*`, `Abstractions/*`, `Contracts/*`, `Interfaces/*` | Use cases, điều phối và contract |
| `KeHoachSX.Infrastructure` | `Repositories/*`, `DataSources/*`, `Database/*`, DI registration | SQL/Dapper, Oracle adapter, JSON/CSV/Excel, connection factory |
| `KeHoachSX.UI` | `KeHoachSX_View.razor`, JS interop, UI registration | Blazor presentation |
| `KeHoachSX.App` | WinForms entry point, MainForm, desktop services, BlazorWebView | Desktop composition root/host |
| `KeHoachSX.Api` | Project references Core/Application/Infrastructure | API/module integration point; endpoints chưa được xác nhận |
| `JtecShare.Core/Services/UI` | External project references in solution | Shared infrastructure/UI; source nằm ngoài repository này |

## 3. Dependency rules

Intended direction:
```text
Core
  ↑
Application
  ↑
Infrastructure

UI ─────> Application/Core
App ─────> UI + Application + Infrastructure + JtecShare
Api ─────> Application + Infrastructure + Core
```

Rules:
- Core không tham chiếu Application, Infrastructure, UI hoặc host.
- Application không tạo `SqlConnection`, không gọi Oracle provider, không đọc CSV/Excel trực tiếp.
- Infrastructure implement abstractions thuộc Application.
- UI không viết SQL và không parse CSV/Excel.
- App/API là composition root; tránh đăng ký cùng một service theo nhiều nơi nếu không có lý do.
- JtecShare integration chỉ qua contract đã dùng, không sao chép logic chia sẻ vào KeHoachSX.

## 4. Các thành phần cụ thể đã xác minh

### Core
- `ShiftConfig`, `ShiftBreak`: cấu hình thời gian ca/nghỉ.
- `WeeklyPlanItem`: một hàng dữ liệu kế hoạch đầu vào.
- `LotTask`: một đơn vị nhiệm vụ/Lot được phân công.
- `LotTaskChunk`: phân đoạn giờ làm trong ngày.
- `AssemblyWorker`: nhân viên cùng hiệu suất, nhóm, skill map và lịch ngày.
- `DispatchResult`: kết quả phân công/KPI.
- `DispatchOptions`: tùy chọn thuật toán. Lưu ý `DefaultEfficiency` hiện khai báo mặc định 100, nhưng `AutoDispatchService.GetTaskDuration` dùng fallback 20 nếu hiệu suất nhân viên <= 0; cần giải quyết mâu thuẫn (xem KNOWN_GAPS).

### Application
- `WeeklyPlanParserService`: chuyển dòng kế hoạch đầu vào thành LotTask.
- `AssemblyEmployeeService`: tải, fallback, lọc và gắn kỹ năng cho công nhân.
- `SkillMatrixService`: nạp và cung cấp skill map/trải nghiệm theo product.
- `StandardTimeService`: tra cứu và cache phút định mức.
- `AutoDispatchService`: chọn worker, xếp Lot và phân đoạn giờ theo break.

### Infrastructure
- `SqlConnectionFactory`: lấy connection string theo ưu tiên `Server30StockConnection`, rồi `DefaultConnection`.
- `SqlAssemblyEmployeeRepository`: truy vấn nhân viên LR1–LR4 và hiệu suất mới nhất.
- `SqlStandardTimeRepository`: truy vấn giờ công từ `SANLUONGCAMLR`.
- `OracleStandardTimeProvider`: adapter tới `ILayGioCongHeThong` trong JtecShare; hiện truyền mã công đoạn `1523`.
- `JsonAssemblyEmployeeSource`: đọc `NHANVIEN.json`, `HIEUSUATNHANVIEN.json`.
- `CsvSkillMatrixDataSource`: đọc hai CSV ma trận kỹ năng.
- `WeeklyPlanFileReader`: parse CSV/Excel bằng ClosedXML cho Excel.
- `KeHoachSXRegisterServices`: đăng ký repository, data sources, adapters và application services.

### UI/Desktop
- `KeHoachSX_View.razor`: route `/kehoachsx`; nạp công nhân, import plan, dispatch, filter, xem và sửa assignment.
- `KeHoachSXMainForm`: tạo DI, đăng ký SharedRegisterServices trước KeHoachSXRegisterServices, cấu hình BlazorWebView.
- Desktop host dùng `wwwroot/index.html` và copy `doccuments/*.csv` vào output `data/`.

## 5. Main data flow (observed)

```text
CSV/XLSX uploaded
  -> UI OnPlanFileUploaded
  -> IWeeklyPlanParserService
  -> IWeeklyPlanFileReader (Infrastructure)
  -> List<WeeklyPlanItem>
  -> StandardTimeService (only when plan row lacks positive standard time)
  -> List<LotTask>
  -> IAutoDispatchService
  -> DispatchResult
  -> Blazor UI view/KPI/timeline/manual assignment
```

Employee/skill/standard-time flow:
```text
UI
  -> IAssemblyEmployeeService
  -> SQL Server repository
       -> if query fails OR returns zero rows: JSON fallback
  -> ISkillMatrixService
       -> detailed employee-to-product CSV
       -> product-to-worker supplemental CSV
  -> workers with SkillMap

Standard time:
Plan row positive value
  > Oracle adapter via JtecShare (process code 1523)
  > SQL Server SANLUONGCAMLR
  > default 60 minutes
```

Important: actual UI call order and error handling must be checked before changing a flow. Do not assume all data retrieval happens only once or is persisted.

## 6. External dependencies and build

- Solution references JtecShare projects via relative paths outside repository.
- Infrastructure targets `net8.0`.
- App targets `net8.0-windows`.
- API targets `net8.0`.
- Infrastructure uses Dapper, Microsoft.Data.SqlClient and ClosedXML.
- App uses Windows Forms BlazorWebView.
- Build may require Windows and external JtecShare directory.

## 7. Extension rules

When adding a new data provider:
1. Define/extend Application abstraction only when a real use case requires it.
2. Implement it in Infrastructure.
3. Register it in `KeHoachSXRegisterServices`.
4. Keep provider priority/fallback explicit and tested.
5. Add tests for provider unavailable, empty result, malformed data and valid result.
6. Never silently alter existing priority.

When adding a feature:
1. Keep scheduling/business calculation out of Razor UI.
2. Use existing contracts before inventing new interfaces.
3. Do not duplicate parsing or database access logic.
4. Update this document only after architecture really changes.
