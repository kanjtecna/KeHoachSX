# Data Sources and Persistence

## 1. SQL Server — employees and standard time [CODE]

### Employee repository
`SqlAssemblyEmployeeRepository` queries:
- `STOCKMANAGEMENT.dbo.NHANVIEN`
  - `UserName` -> WorkerId
  - `Fullname` -> FullName
  - `GROUPBOPHAN` -> GroupName
  - `STATUS` and `LevelUser` filter eligibility
- `STOCKMANAGEMENT.dbo.HIEUSUATNHANVIEN`
  - newest `[No]` record per employee
  - `HIEUSUAT` parsed to positive floating-point efficiency
  - fallback efficiency 20

### Standard-time repository
`SqlStandardTimeRepository` queries:
- `STOCKMANAGEMENT.dbo.SANLUONGCAMLR`
- `IDPRODUCT = @ProductCode`
- positive `GIOCONG`
- newest `[TIME]`

### Connection selection
`SqlConnectionFactory` selects `Server30StockConnection`, then `DefaultConnection`. No connection string value should ever be copied to docs or committed. Current connection configuration is linked from external JtecShare path in the App project; inspect the deployment environment securely.

## 2. Oracle [CODE]

Oracle is accessed through optional JtecShare service `ILayGioCongHeThong`, adapted by `OracleStandardTimeProvider`.
- operation/process code passed: `1523`.
- parsed `GioCongHT` value must be positive.
- if service not registered, `NullOracleStandardTimeProvider` returns no result.
- Oracle source failure is caught by `StandardTimeService`, then SQL Server is tried.

Do not hardcode new Oracle credentials or SQL in Application/UI.

## 3. JSON employee fallback [CODE]

Files:
- `NHANVIEN.json`
- `HIEUSUATNHANVIEN.json`

Reader searches known locations including:
- `D:/JTEC-WEB/Documents`
- application base directory
- `data/`
- `Documents/`

The Application service uses JSON when SQL query throws OR returns zero rows. JSON is a fallback for employee source; it is not established as the authoritative write store.

## 4. Skill matrix CSV [CODE]

Files under `doccuments/`:
1. `_SELECT_TempTable_IDNHANVIEN_TempTable_Fullname_STRING_AGG_TempT.csv`
   - row fields are interpreted as employee ID, full name, and a comma-separated detailed product history such as `PRODUCT(count)`.
   - this is authoritative for detailed `SkillMap` counts.
2. `_SELECT_TempTable_IDPRODUCT_STRING_AGG_TempTable_ThongTinNhanVie_nhungnhanvientunglammahang.csv`
   - product code plus comma-separated worker entries formatted as `WorkerId-FullName`.
   - supplements product-to-worker membership.
   - if not already in detailed skill CSV, added with count 1 for display/listing but `IncludeInSkillMap=false`; it does not fabricate a detailed skill count.
3. `GioCongTieuChuan_LR.csv` — present in repository, but its active consumer and schema must be traced before modifying it.
4. `KeHoachTuanHienTai.csv` — present in repository; actual workflow/format must be checked before changing assumptions.

The skill matrix loads lazily on first use and is held in memory by `SkillMatrixService` for the service lifetime. No automatic file watcher/reload/versioning is evident in the inspected code.

## 5. Uploaded weekly plans [CODE]

- Excel: first worksheet, first used row header.
- CSV: delimiter detected from the header (comma or semicolon).
- Header normalization removes Vietnamese diacritics and detects product/quantity/lot/standard-time columns.
- Parser skips blank product rows.
- Reader does not establish a permanent persistence mechanism; UI's current upload and dispatch state must be inspected before making persistence claims.

## 6. Security and reliability requirements

- No real secrets, connection strings, employee dumps or sensitive operational values in Git.
- Production DB is not an AI playground. Use a sanitized test database or schema-only export.
- Any new file import must validate size, format, expected headers, row count, numeric bounds and duplicate handling.
- For generated snapshots, add schema version, generated timestamp, source revision and checksum only after the owner approves the format.
- Treat a missing/malformed source as an explicit diagnostic state. Do not silently switch to stale or incompatible data.
