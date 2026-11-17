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

## 5. Uploaded weekly plans [CODE]

- Excel: first worksheet, first used row header.
- CSV: delimiter detected from the header (comma or semicolon).
- Header normalization removes Vietnamese diacritics and detects product/quantity/lot/standard-time columns.
- Parser skips blank product rows.
- Reader does not establish a permanent persistence mechanism; UI's current upload and dispatch state must be inspected before making persistence claims.

## 6. Security and reliability requirements

- No real secrets, connection strings, employee dumps or sensitive operational values in Git.
- Production DB is not an AI playground. Use a sanitized test database or schema-only export.
- Oracle access must be enforced with SELECT-only database grants; never rely only on an AI prompt or code review to prevent writes.
- Any new file import must validate size, format, expected headers, row count, numeric bounds and duplicate handling.
- For generated snapshots, add schema version, generated timestamp, source revision and checksum only after the owner approves the format.
- Treat a missing/malformed source as an explicit diagnostic state. Do not silently switch to stale or incompatible data.


## 7. Overtime — SQL Server `TA.dbo.Overtime` [OWNER-SUPPLIED SCHEMA]

Relevant fields supplied:
- `ID`: employee identifier (matching to `NHANVIEN.UserName` is assumed and must be confirmed).
- `date`: overtime date stored as NVARCHAR; actual format must be inspected.
- `SOGIO`: overtime hours, stored as NVARCHAR; parse/validation format must be confirmed.
- `STATUS`, `QUANLYDUYET`, `GADUYET`: approval/status fields; valid/approved combination is not yet defined.
- `NHOM`: group field; matching rule to employee group must be confirmed.

Use only read-only parameterized SQL Server queries. For a normal day, valid overtime adds `SOGIO × 60` minutes to regular capacity and extends the timeline. For a company holiday, only the matching employee with a valid overtime record is eligible, and capacity is SOGIO hours only. Do not regard any arbitrary row as approved before status semantics are confirmed.

## 8. Company holidays — SQL Server `TA.dbo.nghile` [OWNER-SUPPLIED SCHEMA]

Relevant fields supplied: `id`, `repetition_type`, `date` (NVARCHAR), `holiday_id`, `createdAt`, `updatedAt`.

Use read-only parameterized SQL Server queries. Actual date format, recurrence semantics and whether `holiday_id` joins to another master table must be inspected. Do not compare date strings lexicographically or invent recurrence rules.

## 9. Leave — SQL Server `STOCKMANAGEMENT.dbo.thutuchanhchinh` [OWNER-SUPPLIED SCHEMA]

Relevant fields supplied:
- `ID`: employee identifier.
- `STARTDATE`, `ENDDATE`: leave interval.
- `TRANGTHAI`: NVARCHAR status; owner rule is `TRANGTHAI <> 7`.

The initial rule excludes the matching employee on dates covered by a qualifying interval. The table also has time/half-day fields (`STARTTIME`, `ENDTIME`, `LOAITHOIGIAN`, `BUOINGHI`), but their behavior is not specified yet. Confirm NULL status and partial-day handling. Read-only queries only.

## 10. Jig/fixture input — `dulieubanga.xlsx`

The uploaded sample has one sheet `Sheet1`, 494 data rows and no header row; row 1 is a data row. Owner-defined positional mapping:
- B: product code.
- C: quantity/workload input to worker-count rules.
- L: category flag T/J.
- M: fixture description/identifier.
- N: numeric condition in the T/J rules (business meaning needs confirmation).
- O: number of physical jig sets.

Do not skip row 1 as a header. Some sample rows have blank L/N/O; missing metadata must be reported, not silently defaulted. See `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md` for exact worker-count thresholds and resource constraints.
