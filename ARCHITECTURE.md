# KeHoachSX – Architecture

## Project responsibilities

| Project | Responsibility | May reference |
|---|---|---|
| `KeHoachSX.Core` | Domain models and production-planning data structures | .NET base libraries |
| `KeHoachSX.Application` | Use-case services, service contracts, repository/source abstractions, dispatch algorithm | Core |
| `KeHoachSX.Infrastructure` | SQL Server/Dapper repositories, connection factory, Oracle adapter, JSON/CSV/Excel readers, DI composition | Application, Core, JtecShare.Core |
| `KeHoachSX.UI` | Blazor UI and presentation | Application, Core, JtecShare.UI |
| `KeHoachSX.App` | Windows Forms + Blazor Hybrid desktop host | UI, Application, Infrastructure |
| `KeHoachSX.Api` | API/module host integration | Application, Infrastructure, Core |

The intended dependency direction is:

```text
KeHoachSX.App ---> KeHoachSX.UI
       |                |
       |                v
       +----------> KeHoachSX.Application ---> KeHoachSX.Core
       |
       +----------> KeHoachSX.Infrastructure ---> KeHoachSX.Application

KeHoachSX.Api ---> KeHoachSX.Infrastructure
       |
       +----------> KeHoachSX.Application ---> KeHoachSX.Core

KeHoachSX.UI ---> KeHoachSX.Application ---> KeHoachSX.Core
```

The UI does not reference Dapper, SqlClient, Oracle providers, or file parsing implementations. Application defines abstractions; Infrastructure implements them.

## Data access boundaries

- `SqlAssemblyEmployeeRepository` owns the SQL query for active LR1–LR4 workers.
- `SqlStandardTimeRepository` owns SQL Server standard-time lookup.
- `SqlConnectionFactory` owns connection-string selection and SQL connection creation.
- `OracleStandardTimeProvider` adapts the optional JtecShare Oracle service.
- `JsonAssemblyEmployeeSource` reads the employee and efficiency JSON fallback files.
- `CsvSkillMatrixDataSource` reads the two existing skill-matrix CSV files.
- `WeeklyPlanFileReader` reads Excel/CSV input and returns normalized `WeeklyPlanItem` records.

Application services orchestrate these contracts. They do not create database connections or parse JSON/CSV/Excel directly.

## Preserved business behavior

The refactor intentionally preserves these existing rules:

- SQL Server remains the primary source for active assembly employees; JSON is used when SQL is unavailable or returns no rows.
- Eligible assembly groups remain LR1, LR2, LR3 and LR4. CHIEF, LEADE and LEADER remain excluded.
- Missing/non-positive employee efficiency defaults to 20%.
- Standard time lookup priority remains Oracle, SQL Server, then 60 minutes.
- A standard-time value supplied by the uploaded plan takes precedence over database lookup.
- Lot splitting and the existing `standardMinutes / 0.3` planned-duration calculation remain unchanged.
- The detailed employee-skill CSV remains the source of `AssemblyWorker.SkillMap`; the product-to-worker CSV supplements the experienced-worker list but does not fabricate detailed skill counts.

## File deployment

The desktop project copies the existing `doccuments/*.csv` files to the application output folder under `data/`. The skill-matrix source checks this location as well as the original deployment locations, including `Plugins`, the solution's `doccuments` folder, and `D:/JTEC-WEB/Documents`.

The employee JSON fallback continues to look in the existing deployment locations. JSON files are not generated or overwritten by this refactor.

## Dependency injection

`KeHoachSXRegisterServices.RegisterServices` is the module composition point. It registers both application services and Infrastructure adapters. The desktop host first registers shared JtecShare services, then registers KeHoachSX services so the optional Oracle adapter can detect the shared Oracle service.

The skill matrix is loaded lazily on first use instead of using an unobserved fire-and-forget startup task.

## Local verification

Run these commands from the repository root after checking the external JtecShare project paths on the development machine:

```powershell
dotnet restore .\KeHoachSX.sln
dotnet build .\KeHoachSX.sln -c Debug
dotnet build .\KeHoachSX.App\KeHoachSX.App.csproj -c Release
```

The desktop host targets `net8.0-windows`; build it on a Windows development machine with the corresponding .NET 8 SDK and WebView2 prerequisites.
