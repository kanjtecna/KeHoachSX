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

## 2. Oracle — strictly read-only [CODE + MANDATORY POLICY]

Oracle is accessed through optional JtecShare service `ILayGioCongHeThong`, adapted by `OracleStandardTimeProvider`.
- operation/process code passed by the current adapter: `1523`.
- parsed `GioCongHT` value must be positive.
- if the service is not registered, `NullOracleStandardTimeProvider` returns no result.
- Oracle failure, missing service or non-positive/no result causes `StandardTimeService` to try SQL Server.
- the implementation of `ILayGioCongHeThong` is in an external JtecShare project and is not present in this repository; its internal SQL must be inspected separately before declaring the full Oracle call path verified.

### Absolute restriction for AI-generated work
- Oracle is a **read-only source**. AI must only implement/read reviewed SELECT-based retrieval from approved Oracle objects.
- Never generate or execute Oracle DML: `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, or any equivalent data mutation.
- Never generate or execute Oracle DDL/security changes: `CREATE`, `ALTER`, `DROP`, `GRANT`, `REVOKE`, object/schema changes, or privilege changes.
- Never add write-capable Oracle repository methods, `Execute`/mutation calls, PL/SQL blocks, or calls to stored procedures/packages/functions whose side effects are not proven absent.
- Do not use `SELECT ... FOR UPDATE`, sequence `NEXTVAL`, or other constructs that change state or acquire write locks.
- Do not create another Oracle connection/query implementation just to bypass the existing shared service.
- Oracle credentials used by the application must belong to a database principal with SELECT-only grants on approved tables/views. A prompt rule is not sufficient protection; database permissions are the enforcement boundary.
- If a requirement asks to modify Oracle, stop that part of the task and explain that the AI implementation scope forbids Oracle writes. Any exceptional DBA operation must be handled manually by the authorized database administrator, not generated or executed by an AI agent.

Do not hardcode Oracle credentials or put Oracle SQL in Application/UI. Keep Oracle retrieval inside the existing Infrastructure adapter/shared-service boundary.

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

## 5. Uploaded plans [CODE + OWNER TARGET]

### Current parser behavior [CODE]
- Excel: first worksheet, first used row header.
- CSV: delimiter detected from the header (comma or semicolon).
- Header normalization removes Vietnamese diacritics and detects product/quantity/lot/standard-time columns.
- Parser skips blank product rows.
- The current reader/UI does not establish durable plan persistence.

### Target whole-week behavior [OWNER REQUIREMENT]
- In Whole week mode, one uploaded file is the complete weekly demand pool for the selected Friday–Thursday week.
- Rows are not pre-assigned to individual weekdays; the scheduling engine distributes their product/Lot work over the seven dates using available employee/date capacity and the approved hard constraints.
- Preserve group isolation: the file is associated with one explicitly selected/identified group. Do not silently treat it as a combined plan for all groups.
- Daily modes continue to associate the imported rows with the selected calendar date.
- Import replacement behavior for a repeated upload into the same group/week or group/date is still to be decided.

## 6. Security and reliability requirements

- No real secrets, connection strings, employee dumps or sensitive operational values in Git.
- Production DB is not an AI playground. Use a sanitized test database or schema-only export.
- Oracle access must be enforced with SELECT-only database grants; never rely only on an AI prompt or code review to prevent writes.
- Any new file import must validate size, format, expected headers, row count, numeric bounds and duplicate handling.
- For generated snapshots, add schema version, generated timestamp, source revision and checksum only after the owner approves the format.
- Treat a missing/malformed source as an explicit diagnostic state. Do not silently switch to stale or incompatible data.


## 7. Overtime — SQL Server `TA.dbo.Overtime` [OWNER-SUPPLIED SCHEMA + OWNER TARGET]

Relevant fields supplied:
- `ID`: employee identifier; owner confirms it always matches `NHANVIEN.UserName`.
- `date`: overtime date stored as NVARCHAR; observed format is ISO `yyyy-MM-dd`.
- `SOGIO`: overtime duration in hours, stored as NVARCHAR; observed decimal format is dot-separated (for example `"1.5"`). Parse with invariant decimal rules.
- `STATUS`, `QUANLYDUYET`, `GADUYET`: do not filter these fields in the initial phase.
- `NHOM`: group field, but matching/authorization semantics remain unconfirmed.

Use read-only parameterized SQL Server queries only. For the initial phase, **do not add a filter** based on `STATUS`, `QUANLYDUYET` or `GADUYET`; use records returned by the current retrieval rule. The owner will define approval filtering later.
- Normal working date: add `SOGIO × 60` minutes to the employee's regular capacity; overtime starts at **17:00**, immediately after the current normal shift end.
- Company holiday: only the employee with a matching overtime record can work; capacity is **SOGIO × 60** minutes only, starting at **08:00**. Do not add a normal shift.
- Treat `SOGIO` as the usable overtime duration in this phase; no separate overtime-break deduction has been specified.
- Multiple rows for the same normalized `ID + date` must contribute only one overtime value; never sum duplicates. Identical SOGIO duplicates may be collapsed; conflicting SOGIO values must be reported for review until a tie-breaker is approved.

## 8. Company holidays — SQL Server `TA.dbo.nghile` [OWNER-SUPPLIED SCHEMA + OWNER TARGET]

Relevant fields supplied: `id`, `repetition_type`, `date` (NVARCHAR), `holiday_id`, `createdAt`, `updatedAt`.

Each stored `date` value represents one specific holiday date and does **not** repeat. Use `date` as the authoritative one-off date; do not expand recurrence from `repetition_type` or `holiday_id`. Inspect the actual `date` format before parsing. Use read-only parameterized SQL Server queries only.

## 9. Leave — SQL Server `STOCKMANAGEMENT.dbo.thutuchanhchinh` [OWNER-SUPPLIED SCHEMA]

Relevant fields supplied:
- `ID`: employee identifier.
- `STARTDATE`, `ENDDATE`: leave interval.
- `TRANGTHAI`: NVARCHAR status; owner rule is `TRANGTHAI <> 7`.

For each calendar date covered inclusively by `STARTDATE`–`ENDDATE`, exclude the matching employee for the **whole date** when `TRANGTHAI <> N'7'`. Ignore partial-day/time fields (`STARTTIME`, `ENDTIME`, `LOAITHOIGIAN`, `BUOINGHI`) in this phase. Under normal SQL NULL semantics, NULL status does not satisfy `<>` and is excluded from the result; do not add NULL rows unless the owner changes the rule. Use read-only queries only.

## 10. Latest consolidated result workbook — quantity semantics [OWNER-SUPPLIED]

The latest reference file `file_ketqua_phanchia(2).xlsx` has six sheets:
- `kết quả`: 101 employee rows, 78 distinct employees, 43 products.
- `số lượng mã sản phẩm cần làm`: normalized total for each product.
- `LR1`, `LR2`, `LR3`, `LR4`: normalized quantity attributed to each employee department.

The result columns include `Số lượng` and `Số người làm`. Use the verified row contribution:
`Số lượng / Số người làm`.

- Product total = sum of row contributions for that product across all employees.
- Department/product total = sum of row contributions for that product only from employees whose `Nhóm` equals the department.
- Department total = sum of that department's product contributions.
- The product summary and the four department sheets must reconcile to one another within a small numeric tolerance.

Latest workbook totals:
- Product summary: 43 products, total 317.
- LR1: 7 products, total 42.833333.
- LR2: 13 products, total 101.25.
- LR3: 19 products, total 101.
- LR4: 10 products, total 71.916667.
- The four department sheets sum to 317 within floating-point tolerance.
- The raw sum of `Số lượng` across 101 employee rows is 542, but this is the numerator before dividing by `Số người làm`; it is not the normalized work total.

Six product codes have fractional allocations across two departments. These are intentional headcount-based attributions. Do not round intermediate values to integers, multiply the ratio by headcount again, or force the remainder into one department. Invalid or non-positive `Số người làm` must be flagged; do not divide by zero.

The prior standalone LR1–LR4 input files and earlier result workbook are superseded by this latest consolidated workbook for this sample. The six sheets contain stored values rather than live formulas, so editing `kết quả` requires regenerating the derived product and department summaries. Do not copy employee names/IDs from the workbook into repository docs or tests.

## 11. Jig/fixture input — `dulieubanga.xlsx`

The uploaded sample has one sheet `Sheet1`, 494 data rows and no header row; row 1 is a data row. Owner-defined positional mapping:
- B: product code.
- C: quantity/workload input to worker-count rules.
- L: category flag T/J.
- M: fixture description/identifier.
- N: numeric condition; for category J, `N >= 2` enables the worker-count rule and does not cap the resulting number of workers.
- O: number of physical jig sets available for the product; it limits concurrently active independent teams/work streams, not workers inside one team.

Do not skip row 1 as a header. Some sample rows have blank L/N/O; missing metadata must be reported, not silently defaulted. One team of multiple workers may share one jig set (for example, four workers working together on one product consume one set). See `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md` for exact worker-count thresholds and jig concurrency rules.


## 12. Plan output and persistence — current phase [OWNER REQUIREMENT]

- Do not add database persistence or durable plan storage in the initial scheduling phase.
- The in-memory assignment result can be exported to an Excel workbook after dispatch is complete.
- Excel export is a file output only; it must not write plan data to the production database.
- The exact workbook layout/columns and treatment of unassigned or overloaded work remain to be specified before export implementation.

## 13. Employee efficiency and system standard time [CODE-VERIFIED + OWNER TARGET]

### 13.1 Employee efficiency — `STOCKMANAGEMENT.dbo.HIEUSUATNHANVIEN`

Current SQL source is joined by `HIEUSUATNHANVIEN.IDNHANVIEN = NHANVIEN.UserName` (trimmed). The repository query takes the latest row using `TOP (1) ... ORDER BY h.[No] DESC` and reads `HIEUSUAT` as a positive numeric percentage. For example, a stored value of `45` means 45%, not 0.45. Missing, invalid or non-positive SQL values currently fall back to 20%.

The offline JSON source reads `HIEUSUATNHANVIEN.json`, maps `IDNHANVIEN` to `HIEUSUAT`, and also defaults to 20% when no positive value is found. Unlike the SQL query, the current JSON implementation does not explicitly sort by `No`; its result for duplicate employee rows depends on file order. Ensure the exported JSON snapshot is deterministic before relying on it as an equivalent latest-record source.

### 13.2 System standard time — Oracle and SQL Server

Current source priority:
1. A positive `GioCongDinhMuc` value present in the uploaded plan takes precedence.
2. Otherwise, `OracleStandardTimeProvider` calls the external JtecShare service `ILayGioCongHeThong.LayGioCongHeThongAsync(productCode, "1523")`, reads `GioCongHT` and parses a positive numeric value.
3. If Oracle returns no valid value or throws, `StandardTimeService` tries `STOCKMANAGEMENT.dbo.SANLUONGCAMLR.GIOCONG` for the product, taking the most recent `[TIME]` row.
4. If no positive value is found, the current service returns a default of 60.

The code comments and property names treat this value as minutes, but the external JtecShare implementation is not in this repository. The exact business unit/basis (minutes per product unit, per Lot or other) remains unverified and must be confirmed before changing quantity multiplication or dispatch math.

### 13.3 Use in efficiency-based dispatch

For each employee/date, calculate:
`DailyTargetStandardMinutes = DailyAvailableClockMinutes * HIEUSUAT / 100`.

For each assigned Lot:
`TaskActualClockMinutes = TaskSystemStandardMinutes / (HIEUSUAT / 100)`.

Track standard minutes and actual clock minutes separately. The target is an allocation objective; hard availability is still enforced in actual clock time. A missing/non-positive efficiency currently uses the 20% fallback and must be surfaced as a warning in the new scheduler. Do not confuse standard minutes with clock minutes or apply the efficiency percentage twice.
