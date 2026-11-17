# Scheduling Calendar, Group Import, Overtime, Leave and Jig Constraints

> Status: **business requirements captured on 2026-11-17**. This document separates rules explicitly supplied by the owner from implementation gaps and questions. It does not authorize changing production behavior until the relevant acceptance rules are approved.

## 1. Required scheduling modes and date behavior

### 1.1 Default view

[OWNER REQUIREMENT]
- When the application opens, the selected scope defaults to **Today** using the plant's local business date.
- The initial screen shows the selected date's plan/dispatch data. If no plan exists for that date, show an empty state and allow importing that date's product list.
- The user can switch scope to **Tomorrow** or **Whole week**.
- Selecting Tomorrow targets the next calendar date, not the next working date.
- Selecting Whole week targets the production week containing the selected anchor date.

[CURRENT CODE GAP]
- Current `KeHoachSX_View.razor` has no date selection or today/tomorrow state.
- Current UI declares six names Monday–Saturday and `ShiftConfig.NumberOfDays` defaults to 6.
- Current `_selectedDayIndex = -1` means the current in-memory week, not a calendar week.
- Current import replaces the single `_currentLots` list and triggers dispatch for all currently loaded workers.

### 1.2 Production week definition

[OWNER REQUIREMENT]
- A production week contains exactly seven consecutive calendar dates.
- Week starts Friday and ends the following Thursday, inclusive.
- Example: Friday 2026-11-13 through Thursday 2026-11-19 is one production week.
- The seven dates must be represented explicitly; do not assume Monday–Sunday or Monday–Saturday.
- Each date can have its own working/holiday/leave/overtime availability. A seven-day week does not mean every worker works all seven days.

[IMPLEMENTATION GUIDANCE]
- Derive week start as the most recent Friday on or before the anchor date; week end is week start + 6 days.
- Use real date values (prefer `DateOnly` for calendar identity) and a configured plant/business time zone. Do not compare formatted date strings or assume UTC date equals local business date.
- A schedule item must be associated with an actual calendar date, not only a fixed 0..5 day index.
- Keep the production week start/end in the schedule context so the UI, imports and capacity calculations use the same boundaries.

### 1.3 Import scope

[OWNER REQUIREMENT]
- Today scope: imported product rows belong to today's date.
- Tomorrow scope: imported product rows belong to tomorrow's date.
- Whole week scope: imported plan belongs to the selected Friday–Thursday production week.
- The user may import data separately for a production group; the application must not require a single combined file for all groups.

[OWNER REQUIREMENT — CONFIRMED]
- Every imported plan is identified by its scope (date or Friday–Thursday week) and the production-group value read from a column in the file.
- When the same group and same scope are imported again, replace that group's previous plan for that exact scope. Do not append quantities or double the work.
- Replacing one group/scope must not overwrite another group or another date/week.
- The group value must be validated against the configured/recognized group list; do not silently normalize an unknown label into another group.
- The selected/identified group normally limits eligible workers to that group. Cross-group product cooperation is the controlled exception described in section 1.4.

[OWNER REQUIREMENT — CONFIRMED]
- In Whole week mode, one uploaded file contains the complete product demand for the selected production week. The file is a **weekly pool**, not a file with manually assigned dates: the scheduling engine distributes its product/Lot work across all seven Friday–Thursday dates according to employee/date capacity and the hard constraints in this document.
- Do not require the owner to split that weekly file into seven daily files or pre-assign each row to a weekday.
- The earlier group-isolation requirement remains in force: the weekly import belongs to one explicitly identified production group. It is not implicitly a combined file for LR1–LR4. The UI-versus-file mechanism for identifying the group is still open.
- The scheduler must account for all imported quantities. It must expose any work that cannot fit within the seven-day capacity; it must not silently drop or falsely mark it completed.

### 1.4 Controlled product sharing across groups

[OWNER REQUIREMENT — CONFIRMED]
- Default behavior is to keep all work for a product within one production group whenever that group has enough available capacity and eligible workers.
- A product may be assigned to workers from **two different groups** when necessary to complete the plan or use available capacity more effectively.
- This cross-group arrangement is an exception, not the default: minimize the number of groups per product and do not split across more than two groups without explicit owner approval.
- Keep each employee row's source quantity numerator and headcount metadata intact. When two departments share one product, attribute work to each department by summing `Số lượng / Số người làm` for employees in that department; do not call this normalized share a raw source-plan quantity.
- The assignment result must identify each worker's actual department and preserve the department's normalized contribution.
- The sample workbooks contain six product codes planned in two groups. This is an approved example of controlled sharing, not permission to spread every product across groups.

[IMPLEMENTATION GUIDANCE]
- Treat "same product code" and "same worker team" as different concepts. A product may have separate teams in two groups; a team may also contain workers from more than one group only when the chosen allocation requires that and the result preserves each group's source quantity.
- Prefer a strong same-group-first optimization objective. Only introduce a second department when the one-department allocation cannot meet capacity/skill/workload constraints or a clear operational benefit is required.
- Record/communicate why cross-department sharing was needed so it can be reviewed by the production manager.

[REFERENCE WORKBOOK]
- The latest consolidated workbook supersedes earlier standalone group-file snapshots for this example. Its LR1/LR2/LR3/LR4 sheets identify the employee department explicitly in column B.
- Do not carry forward labels or quantities from older standalone files when they conflict with the latest consolidated workbook.

## 2. Daily availability and capacity model

The scheduler must calculate availability **per employee, per date**, rather than assigning one fixed weekly capacity to every person.

For each production date:
1. Load eligible employees for the selected group.
2. Apply whole-day leave exclusion for records with `TRANGTHAI <> 7` on that date.
3. Determine whether the date is a company holiday/non-working date.
4. Read matching overtime records using the initial no-status-filter rule in section 4.3.
5. Compute regular and overtime capacity according to the rules below.
6. Apply jig/fixture constraints before placing tasks.
7. Produce timeline chunks using the actual capacity windows, preserving breaks and overtime boundaries.

A person absent on one date can still be scheduled on other dates in the same week. Do not exclude an employee from the entire week because of a one-day leave or holiday.

## 3. Company holidays — `TA.dbo.nghile`

[OWNER REQUIREMENT]
- Company holiday/non-working dates are stored in SQL Server table `TA.dbo.nghile`.
- Relevant columns supplied: `id`, `repetition_type`, `date`, `createdAt`, `updatedAt`, `holiday_id`.
- Each stored `date` value is one actual holiday date. Rows do **not** repeat; do not expand a date into recurring holidays.
- Use these one-off dates to classify the seven dates in the selected production week.

[IMPLEMENTATION RULES]
- Access this SQL Server table through Infrastructure using parameterized, read-only queries.
- Do not write to or modify this table as part of dispatch.
- Treat `date` as the authoritative date value. Do not infer recurrence from `repetition_type` or `holiday_id`.
- Do not compare the NVARCHAR date lexicographically. Parse/normalize it after inspecting the actual stored format.
- A company holiday is not a normal 490-minute shift; apply the holiday overtime window in section 4.2.

[REMAINING TECHNICAL CHECK]
- Inspect representative values to determine the stored format of `nghile.date` before implementing its parser. The date format is a technical data check; recurrence is already confirmed as absent.

## 4. Overtime — `TA.dbo.Overtime`

[OWNER REQUIREMENT]
Provided table: `TA.dbo.Overtime`.
- `ID`: employee identifier.
- `date`: overtime date (NVARCHAR).
- `SOGIO`: number of overtime hours (NVARCHAR).
- Other fields include `STATUS`, `NGUOIDANGKY`, `QUANLYDUYET`, `GADUYET`, `NHOM`, `LYDO`, `createdAt`.

Only `SOGIO` supplies the overtime duration. Overtime is specific to an employee and a calendar date.

### 4.1 Normal working date

[OWNER REQUIREMENT]
- The current normal shift ends at **17:00**.
- On a normal working date, overtime starts immediately after the normal shift, at **17:00**.
- Add `SOGIO × 60` minutes to the matching employee's normal daily capacity and extend their timeline from 17:00 for that duration.
- Keep the normal shift's existing working/break windows before 17:00. Do not apply normal-shift break deductions to the registered overtime duration.

The currently observed normal shift is 07:30–17:00 with 80 minutes of breaks, giving 490 net regular minutes. This remains the current default unless the owner approves a shift configuration change.

### 4.2 Company holiday/non-working date

[OWNER REQUIREMENT]
- Only employees with a matching overtime record for the exact holiday date can be scheduled on that date.
- An employee without a matching overtime record has zero capacity on that holiday.
- Holiday overtime starts at **08:00**.
- The employee's entire available holiday duration is `SOGIO × 60` minutes from 08:00. Do not add the normal shift capacity.
- Extend the visible timeline from 08:00 by the registered duration.

### 4.3 Overtime row selection and approval filtering

[OWNER REQUIREMENT — INITIAL PHASE]
- In the initial phase, use matching overtime records without adding a filter on `STATUS`, `QUANLYDUYET` or `GADUYET`.
- Do not invent status values or approval combinations. The owner will provide approval-filtering logic in a later change.
- This is a temporary data-selection policy, not a statement that every row is formally approved.

[OWNER REQUIREMENT — CONFIRMED DATA MAPPING]
- `Overtime.ID` always matches `NHANVIEN.UserName` and therefore the application's employee key derived from that field.
- Sample `date` format is ISO calendar date `yyyy-MM-dd` (example `2025-03-03`).
- Sample `SOGIO` is a decimal-hour string using dot decimal separator (example `"1.5"`). Parse as invariant decimal, not as a locale-dependent floating-point string.
- `createdAt`, `TIME` and `TIME1` in the supplied sample are ISO UTC timestamp strings. They are not overtime duration fields; do not use them to extend overtime duration unless another requirement defines their meaning.
- Deduplicate by employee ID + normalized overtime date. Repeated rows for the same employee/date contribute only one overtime value; never sum duplicate rows.
- If duplicate rows for the same employee/date have the same `SOGIO`, collapse them to one logical overtime record.
- If duplicate rows for the same employee/date have conflicting `SOGIO` values, flag a data conflict and do not sum or silently choose one until the owner defines the tie-breaker.
- `Overtime.NHOM` is not yet confirmed as an authorization/matching filter. The confirmed employee mapping is by ID.

[REMAINING TECHNICAL CHECKS]
- Verify the actual production schema/format against this sample before writing the parser; validate malformed date and decimal values visibly.
- The initial phase uses no filter on `STATUS`, `QUANLYDUYET` or `GADUYET`.
- No overtime-break deduction has been provided. Treat `SOGIO` as usable overtime duration in this phase.

## 5. Leave/absence — `STOCKMANAGEMENT.dbo.thutuchanhchinh`

[OWNER REQUIREMENT]
Relevant fields:
- `ID`: employee identifier.
- `STARTDATE`, `ENDDATE`: leave interval.
- `TRANGTHAI`: NVARCHAR status.

### 5.1 Scheduling behavior

- When `TRANGTHAI <> 7` and the employee/date interval matches, exclude that employee for the **entire covered calendar date**.
- Apply exclusion per date, not to the whole week. An employee may still be scheduled on dates outside their leave interval.
- Treat `STARTDATE` and `ENDDATE` as inclusive when deriving covered calendar dates.
- Use the employee ID, status and date fields; do not infer leave from `NAME`, `LOAIPHEP` or free-text reason.
- Query SQL Server read-only with parameters. Do not write back or approve leave requests.

### 5.2 Partial-day and NULL behavior

[OWNER REQUIREMENT — INITIAL PHASE]
- Ignore `STARTTIME`, `ENDTIME`, `LOAITHOIGIAN`, `BUOINGHI` and other partial-day fields.
- Any qualifying record excludes the employee for the whole date, even if the source row represents part of a day.

[SQL NULL SEMANTICS]
- Use the supplied inequality rule, equivalent to `TRANGTHAI <> N'7'` for the NVARCHAR column.
- A SQL NULL value does not satisfy `<>`; NULL-status rows are not included by this filter.
- Do not add `OR TRANGTHAI IS NULL` unless the owner changes the requirement.

## 6. Jig/fixture constraints — `dulieubanga.xlsx`

### 6.1 Verified shape of the supplied workbook

The uploaded workbook `dulieubanga.xlsx` was inspected:
- One worksheet: `Sheet1`.
- Used range: `A1:W494` (494 rows, 23 columns).
- Row 1 contains a data record, **not a header row**.
- Column B contains 494 unique product codes in this supplied snapshot.
- Column L contains `T`, `J` or blank values.
- Columns N and O contain integer values in the observed range 1–6 where present, but some rows have missing values.

Do not skip row 1 as a header. Validate future exports rather than assuming every workbook has the exact same shape.

### 6.2 Column mapping supplied by the owner

| Excel column | Use in scheduling |
|---|---|
| B | Product code / `MaSanPham` |
| C | Numeric quantity/workload value used by worker-count rules |
| L | Jig/fixture category flag: `T` or `J` |
| M | Fixture/jig description or identifier (informational until defined) |
| N | Numeric condition used by T/J rules |
| O | Number of physical jig sets currently available for the product |

Do not rename or reinterpret the columns without owner confirmation.

### 6.3 Category T rule [OWNER REQUIREMENT]

When all conditions hold:
- Column L = `T`;
- Column N is 2 or 3;
- Column C > 50;

the product may be assigned to **one or two workers**.

[OWNER REQUIREMENT — CONFIRMED]
- The scheduler may choose either one or two workers according to employee/date capacity, product workload, skills/efficiency and the fit of the overall weekly allocation.
- Do not invent another fixed C threshold to choose between one and two. This is a scheduling decision, not an additional quantity rule.

### 6.4 Category J rule [OWNER REQUIREMENT]

When:
- Column L = `J`;
- Column N >= 2;

apply the worker-count target derived from C:
- C >= 400: 4 workers.
- C >= 300 and < 400: 3 workers.
- C >= 200 and < 300: 2 workers.

[OWNER REQUIREMENT — CONFIRMED]
- N is only the condition that enables this J rule; it is **not** a maximum worker count.
- For example, N=2 and C>=400 still requires four workers.
- C < 200 has no J-specific worker-count rule yet. Do not invent one or extrapolate the thresholds.

### 6.5 Physical jig-set capacity — column O [OWNER REQUIREMENT]

- Column O is the number of physical jig sets currently available for that product.
- O=1 means only one independent team/work stream can use that product's jig at a time.
- A team can contain multiple workers. For example, four workers collaborating on one product as one team share and consume **one** jig set, not four.
- O does not limit the maximum number of workers in one team.
- O=2 permits up to two independent teams/work streams on that product at the same time, provided the work is divided into separate tasks and all other constraints are met.
- Each simultaneously active independent team using the product consumes one physical jig set. Do not double-book the same set over overlapping time intervals.

[REMAINING OPEN ITEMS]
- Define the safe behavior when O=0, blank or non-numeric. Do not treat missing jig-set data as unlimited capacity or silently substitute O=1.
- Confirm whether a jig set remains reserved during breaks and overnight/day boundaries, or may be released for another team during those intervals.

### 6.6 Missing/invalid jig metadata

[OWNER REQUIREMENT — CONFIRMED]
- When required jig metadata is missing or invalid, skip the jig constraint for that product in this phase; do not block the entire product solely because jig fields are blank.
- Continue scheduling that product using the other valid constraints (worker eligibility, leave, daily capacity, skills and workload).
- Surface an informational warning that jig information was unavailable and the jig-capacity check was skipped.
- Do not invent L/N/O values or claim that actual jig resource capacity was verified for that product.
- When valid O is present, enforce the physical jig concurrency rule in section 6.5.

## 7. Combined dispatch constraints and evaluation order

The future scheduler must evaluate a task against all of these dimensions:
1. Product belongs to the selected plan scope and retains its originating group demand. If the same product appears in two group plans, preserve both source allocations separately.
2. Workers are active and eligible in their actual employee departments. A shared product team may include workers from a second department only under the controlled cross-group rule in section 1.4.
3. Worker is not on a qualifying whole-day leave on that date.
4. Worker has positive availability on that date (normal shift and/or overtime record selected under the initial no-status-filter rule).
5. If the date is a company holiday, the worker has a matching overtime record for that date and receives only the 08:00 + SOGIO window.
6. Product T/J rules determine the required/allowed team size according to section 6.
7. Physical jig-set count O limits the number of independent teams concurrently using that product, not the number of people inside a team.
8. Skill, efficiency and workload ranking selects among eligible workers only; ranking must never override any hard constraint.
9. The task must be represented on the correct dated timeline, including normal breaks and overtime boundaries.
10. All imported product quantities must be accounted for; overload, missing metadata and unassigned work must remain visible.
11. Prefer one employee department per product. When sharing is necessary, use no more than two departments per product by default, record the cross-group decision, and reconcile output quantity separately against each source group.
11. Prefer keeping product work inside one group; permit a second group only as a controlled exception, with at most two groups per product by default.
12. If the total plan exceeds available capacity, keep the excess work unassigned and show a clear warning. Never drop it or force an over-capacity assignment silently.

Hard constraints (group eligibility, leave, holiday/overtime availability, available minutes and jig-resource capacity) take precedence over soft scoring (skill, workload balance and continuity).

## 8. Required tests for the scheduling extension

At minimum add deterministic automated tests for:
- App defaults to the configured local current date.
- Today/Tomorrow select exact calendar dates.
- Friday–Thursday week calculation, including dates on Friday and Thursday.
- Seven-day week indexing and correct date-to-timeline mapping.
- A Whole week file is treated as one weekly pool and distributed across all seven dates based on capacity.
- Daily import does not overwrite other dates.
- Re-importing the same group and same date/week replaces only that exact plan scope; it does not append or double quantities.
- One-group import does not overwrite other groups.
- The same product remains in one department when feasible; cross-department splitting uses no more than two departments by default and preserves each department's normalized contribution.
- Every imported quantity is either scheduled or reported as unassigned/overloaded.
- For each product, calculate the product total as `SUM(Số lượng / Số người làm)` across all employee rows and match it to the product summary sheet.
- For each product + employee department, calculate `SUM(Số lượng / Số người làm)` filtered by `Nhóm` and match it to the corresponding LR sheet.
- Sum LR1–LR4 department attributions and reconcile them to the product summary; retain fractional values and do not round intermediate results.
- Each worker is excluded only on dates covered by a leave record with `TRANGTHAI <> 7`.
- Leave exclusion blocks the whole date; partial-day fields do not shorten the exclusion in this phase.
- NULL leave status is excluded by SQL inequality semantics.
- Holiday with no matching overtime record => zero capacity.
- Holiday with a matching overtime record => capacity starts at 08:00 and equals SOGIO only.
- Normal day without overtime => regular shift capacity.
- Normal day with overtime => regular capacity plus SOGIO minutes starting at 17:00.
- The initial overtime query does not filter by `STATUS`, `QUANLYDUYET` or `GADUYET`.
- Overtime is mapped only to the matching employee/date; duplicate records are surfaced until a duplicate policy is approved.
- T conditions allow one or two workers and the scheduler does not introduce an extra hard C threshold.
- J thresholds at C=199, 200, 299, 300, 399, 400; N>=2 is a condition, not a headcount cap.
- O=1 allows a multi-worker team (for example four employees) to share one jig, but prevents a second independent team from using it concurrently.
- O=2 prevents more than two independent concurrent teams for the product.
- Holiday matching uses each `nghile.date` value once; no recurring holiday is generated.
- Missing/invalid jig fields cause the jig check to be skipped with a warning, not the entire product to be discarded.
- When weekly capacity is insufficient, remaining work is left unassigned and accompanied by a warning.
- Duplicate overtime rows for the same ID/date never increase duration; conflicting SOGIO values are surfaced.
- A worker's task timeline never overlaps their leave or another task, and a jig set is never double-booked.
- Manual reassignment cannot bypass group, leave, holiday/overtime or jig constraints.

Do not mark the feature complete until all business rules above are implemented and tests pass. Remaining technical/data-format questions are tracked in `KNOWN_GAPS.md`.

## 9. Plan persistence and Excel export

[OWNER REQUIREMENT — CURRENT PHASE]
- Do not implement database persistence or durable plan storage in this phase.
- The completed assignment result may remain in the application's current in-memory state.
- After dispatch/assignment is complete, the user must be able to export the resulting plan to an Excel file.
- Do not claim the plan is saved permanently. Closing/restarting the application may discard the in-memory assignment unless the user has exported it.
- The Excel export is an output artifact; it does not write back to the production database.
- Export tests cover preserving all assigned dates/workers and reporting unassigned or overloaded tasks when the approved export contract is implemented.

[REMAINING DESIGN DETAIL]
- The exact Excel export columns, worksheet layout, formatting and whether it includes unassigned/overloaded tasks still need an owner-approved specification before implementing the export.


## 10. Latest consolidated workbook — quantity normalization and group attribution

[REFERENCE SOURCE — LATEST OWNER WORKBOOK]
The latest workbook `file_ketqua_phanchia(2).xlsx` is the authoritative reference for this analysis. It contains six sheets:
1. `kết quả`
2. `số lượng mã sản phẩm cần làm`
3. `LR1`
4. `LR2`
5. `LR3`
6. `LR4`

This consolidated workbook supersedes the older standalone LR1–LR4 source files and earlier result workbook for values in this allocation example. Do not carry forward the earlier 542-unit group-input interpretation as the current normalized production total.

### 10.1 Observed structure

The `kết quả` sheet has 101 employee-assignment rows (excluding the header), 78 distinct employee IDs, 43 product codes and four employee departments:
- LR1: 21 assignment rows.
- LR2: 22 assignment rows.
- LR3: 35 assignment rows.
- LR4: 23 assignment rows.

Its columns are:
- `UserName`: employee ID.
- `FullName`: employee name.
- `Nhóm`: employee's department.
- `Status`: employee/status marker in the result.
- `Mã sản phẩm`: product code.
- `Số lượng`: the row's quantity numerator, associated with the assigned work; this value is divided by `Số người làm` to calculate this row's normalized contribution.
- `Số người làm`: denominator/headcount metadata used in the normalization.
- `số thứ tự nhóm`: sequence metadata. Preserve it; do not assume this column alone defines a globally unique team key.

The sheet `số lượng mã sản phẩm cần làm` contains 43 product codes and their normalized product totals. Each LR sheet has three columns: product code, employee department, and that department's normalized contribution for that product. The LR sheets contain 7, 13, 19 and 10 rows respectively (49 product/department entries in total).

### 10.2 Authoritative quantity formula

For every row in `kết quả`, calculate:

`rowContribution = Số lượng / Số người làm`

Use the following aggregation rules:

1. **Total for one product**:
   `ProductTotal(P) = SUM(rowContribution for all result rows where Mã sản phẩm = P)`.
   This must match the value for P in `số lượng mã sản phẩm cần làm`.

2. **Contribution attributed to one department and product**:
   `DepartmentProduct(G,P) = SUM(rowContribution for result rows where Nhóm = G and Mã sản phẩm = P)`.
   This must match the value for P in sheet LR1, LR2, LR3 or LR4 for department G.

3. **Department total**:
   `DepartmentTotal(G) = SUM(DepartmentProduct(G,P) across its product rows)`.

4. **Conservation across department sheets**:
   For each product P, sum its LR1–LR4 contributions; the result must equal ProductTotal(P), within a small numeric tolerance for decimal representation.
   Across all products, the sum of the four department totals must equal the product-summary total.

[IMPORTANT SEMANTICS]
- `Số lượng` must **not** be summed directly across all employee rows as if each row were an independent production quantity. A work quantity may be repeated across the employees who share it.
- Do not copy a row's full `Số lượng` to each employee as their independent production output.
- Do not multiply the normalized contribution by `Số người làm` again after computing `rowContribution`.
- The department tabs are proportional attributions of normalized work according to the departments of the employees participating in each result row. When workers from two departments share work, both department tabs can receive fractional values.
- Fractional values are valid business results. Never round each row or each department/product share before aggregation. Preserve sufficient decimal precision internally; round only for display/export formatting.
- If `Số người làm` is blank, zero, negative or non-numeric, do not divide. Flag the row as invalid quantity metadata and keep it visible for correction.
- `Số người làm` and `số thứ tự nhóm` are distinct metadata. Do not infer that a count of rows grouped by product + sequence must always equal `Số người làm`; retain the supplied values and ask for a team-key rule before building any behavior that depends on reconstructing team membership.

### 10.3 Verified totals from the latest workbook

| Sheet / metric | Entries | Total normalized quantity |
|---|---:|---:|
| `số lượng mã sản phẩm cần làm` | 43 products | 317.000000 |
| LR1 | 7 products | 42.833333 |
| LR2 | 13 products | 101.250000 |
| LR3 | 19 products | 101.000000 |
| LR4 | 10 products | 71.916667 |
| LR1 + LR2 + LR3 + LR4 | 49 product/department entries | 317.000000 (within floating-point tolerance) |

The four LR sheets have 49 product/department entries but 43 unique product codes because six products are shared across two departments.

The raw sum of the `Số lượng` column over the 101 result rows is 542. This is **not** the normalized product total: it is the numerator before division by `Số người làm`. The correct normalized total is 317. Do not report 542 as the total quantity to be completed based on this workbook.

### 10.4 Verified products shared across departments

The following six products have contributions in two department sheets. Their fractional values are intentional headcount-based attribution, not quantity errors.

| Product | Product total | LR1 | LR2 | LR3 | LR4 |
|---|---:|---:|---:|---:|---:|
| 14Y0646122-04 | 10 | 0 | 8.75 | 0 | 1.25 |
| 14Y0646141-04 | 9 | 4.5 | 4.5 | 0 | 0 |
| PS13E01168P2-01 | 5 | 1.666667 | 0 | 0 | 3.333333 |
| PX13E01197P1-01 | 15 | 6 | 0 | 0 | 9 |
| YT13E01438P4-02 | 30 | 12.666667 | 0 | 0 | 17.333333 |
| YT13E01726P1-02 | 9 | 8 | 0 | 0 | 1 |

For each row in this table, LR1+LR2+LR3+LR4 equals the product total. Do not round fractions to integers or move the fractional remainder to one department arbitrarily.

### 10.5 Examples of the formula

- A product/team row with `Số lượng = 5` and `Số người làm = 3` contributes 5/3 for each participating employee row. If two of the three employees belong to LR4 and one belongs to LR1, the department attribution is LR4 = 10/3 and LR1 = 5/3; together they equal 5.
- Product `14Y0646122-04` is 10 overall: LR2 contributes 8.75 and LR4 contributes 1.25.
- Product `PS13E01168P2-01` is 5 overall: LR1 contributes 1.666667 and LR4 contributes 3.333333.
- Product `YT13E01438P4-02` is 30 overall: LR1 contributes 12.666667 and LR4 contributes 17.333333.

### 10.6 Workbook formula state and team metadata

All six sheets were inspected; their used cells contain stored values rather than live Excel formulas. Therefore, if the `kết quả` sheet is edited, the product summary and LR1–LR4 sheets must be regenerated/recalculated by the application. Do not assume the workbook will recalculate derived sheets automatically.

The current workbook has 55 distinct product + `số thứ tự nhóm` pairs. The sequence/headcount metadata must be preserved, but the pair alone is not proven to be a complete unique team key. Do not automatically rewrite `Số người làm` based only on counting rows by product + sequence.

## 11. Per-employee efficiency target and system standard minutes

[OWNER REQUIREMENT — NEW DAILY TARGET]

Each employee has an efficiency percentage in `STOCKMANAGEMENT.dbo.HIEUSUATNHANVIEN`. Use that employee-specific efficiency to determine how much **system standard work** should be assigned for each calendar date. Do not distribute only by equal headcount or treat the percentage as an arbitrary ranking score.

### 11.1 Keep two time measures separate

The scheduler must keep these measures distinct:

- **Available clock/work minutes**: actual usable working time available to the employee on that date, after applicable breaks and availability rules. On a normal date this includes the normal shift plus valid overtime. On a holiday it includes only the valid overtime window.
- **System standard minutes**: the work value obtained from the plan's positive `GioCongDinhMuc` or the standard-time lookup chain. The Oracle adapter calls `ILayGioCongHeThong.LayGioCongHeThongAsync(productCode, "1523")` and reads `GioCongHT`; the code currently treats the returned numeric value as minutes.

For an employee and date:

```
DailyTargetStandardMinutes
    = DailyAvailableClockMinutes * (EmployeeEfficiencyPercent / 100)
```

For an individual Lot assigned to that employee:

```
TaskActualClockMinutes
    = TaskSystemStandardMinutes / (EmployeeEfficiencyPercent / 100)

DailyAssignedStandardMinutes
    = SUM(TaskSystemStandardMinutes for that employee/date)

DailyAssignedClockMinutes
    = SUM(TaskActualClockMinutes for that employee/date)
```

The target is expressed in **system standard minutes**, not clock minutes. Do not multiply each task's standard minutes by efficiency and then also divide by efficiency for timeline duration; that would apply the percentage twice or mix units.

### 11.2 Owner's numerical example

If the employee has 8 hours of usable normal work and 1.5 hours of usable overtime:

- Available clock minutes = `8 * 60 + 1.5 * 60 = 570`.
- Employee efficiency = 45%.
- Daily standard-work target = `570 * 45 / 100 = 256.5` standard minutes.

If a Lot has 45 system standard minutes, its expected clock duration for this employee is `45 / 0.45 = 100` minutes. Assigning 256.5 standard minutes at 45% efficiency corresponds to 570 clock minutes.

This example assumes the stated eight normal hours are **net usable work minutes after breaks**. Do not hardcode 570 as every employee's daily capacity; derive availability from that date's shift, breaks, overtime and holiday/leave rules.

### 11.3 Dispatch objective and hard limits

- Calculate a separate daily target for each employee/date using that employee's efficiency and that date's usable clock minutes. Overtime therefore increases both available clock minutes and the standard-work target for that date.
- While ranking feasible assignments, prefer the employee/date whose remaining standard-work target can accept the Lot and whose resulting total `DailyAssignedStandardMinutes` is closest to `DailyTargetStandardMinutes`.
- Treat the standard-work target and actual clock availability consistently: assigned standard minutes must not imply actual clock duration beyond the employee's usable minutes.
- The target is an allocation objective, not permission to overbook. Never assign more actual clock minutes than the employee has available; never silently exceed the date's target to make the result look full.
- If Lot tasks are indivisible, choose the closest feasible combination without exceeding available clock time. Do not split a Lot or invent a partial-Lot rule unless the owner explicitly approves it. If no remaining Lot fits, leave the remainder visible as unused capacity and keep unassigned work visible.
- In a Whole-week plan, calculate the target independently for each employee/date, then sum daily target values for reporting. Do not replace date-specific overtime/holiday availability with one uniform weekly target.
- Use the employee's percentage as a percentage value: `45` means 45%, and the calculation divides by 100. Preserve decimal precision; round only for display.
- When the performance value is missing or non-positive, preserve the current 20% fallback only as an explicit fallback and surface a warning that the target was calculated using fallback efficiency. Do not present it as a measured employee efficiency.
- Do not use the parser's legacy `ThoiGianThucTe = standardMinutes / 0.3` value as the employee-specific final duration. Recalculate actual clock duration using the assigned employee's efficiency. `GioCongDinhMuc` remains the standard-minute value for the target calculation.

### 11.4 Existing code versus required behavior

[VERIFIED-CODE]
- `SqlAssemblyEmployeeRepository` reads `HIEUSUATNHANVIEN.HIEUSUAT` for the employee ID, takes `TOP (1)` ordered by `No DESC`, parses it as a positive percentage and currently falls back to 20 when missing/non-positive.
- `JsonAssemblyEmployeeSource` also reads `HIEUSUATNHANVIEN.json`; its current implementation assigns values in file iteration order and does not explicitly sort by `No`.
- `OracleStandardTimeProvider` calls `LayGioCongHeThongAsync(productCode, "1523")` and parses `GioCongHT`. `StandardTimeService` uses the priority Oracle -> SQL Server `STOCKMANAGEMENT.dbo.SANLUONGCAMLR.GIOCONG` -> default 60 minutes.
- `WeeklyPlanParserService` gives a positive standard-time value in the uploaded plan precedence over the lookup chain; otherwise it queries `IStandardTimeService`.
- `AutoDispatchService.GetTaskDuration` already calculates an assigned Lot's clock duration as standard minutes divided by `HieuSuat / 100`.

[NOT YET IMPLEMENTED]
The current dispatch algorithm does not explicitly calculate or optimize `DailyTargetStandardMinutes` per employee/date. Its workload score and capacity tracking use `ThoiGianThucTe` (clock duration); it does not separately track the sum of system standard minutes against each person's efficiency target. The current six-day `ShiftConfig` also does not yet represent the full date-specific overtime/holiday model. Do not claim this new target is implemented until the model, dispatch algorithm and tests cover both time measures.

### 11.5 Unit and availability caution
The owner has clarified the product workload accounting: a product's total standard-minute workload is one conserved pool (quantity multiplied once by standard minutes per unit) and is allocated across the worker/team assignments. Do not copy the full product workload onto every team member. If several people physically collaborate on the same individual unit, the per-member attribution still requires an explicit allocation model; the current `LotTask` model has only one `AssignedWorkerId`.


The adapter's method/property names and model comments treat `GioCongHT` as a standard-minute value. The owner's current workload example defines the scheduling basis as minutes per one product unit. The external JtecShare implementation is outside this repository, so verify representative source values against production data; do not change the owner-defined per-unit calculation or multiply quantity more than once. The parser currently expands quantity into one `LotTask` per unit, so the product workload is the sum of those unit tasks.

The current `ShiftConfig` defaults to 07:30–17:00 with 80 minutes of breaks, which gives 490 net minutes before overtime. The example above uses 480 net normal minutes plus 90 overtime minutes = 570. Confirm whether the example's eight hours are the intended net daily capacity or whether the production shift configuration should supply a different value; use actual configured date availability rather than assuming either value globally.

### 11.6 Required tests

- Efficiency 45 with 480 net normal minutes and 90 overtime minutes produces a daily target of 256.5 standard minutes.
- A 45-standard-minute Lot at 45% efficiency consumes 100 actual clock minutes.
- Sum of assigned standard minutes and sum of actual clock minutes are tracked separately for each employee/date.
- A candidate cannot receive a Lot if its actual duration exceeds the remaining usable date capacity.
- Lot selection approaches the standard-minute target without exceeding actual clock capacity; indivisible remainder stays visible.
- Overtime increases the daily target by `SOGIO * 60 * efficiency / 100` on normal dates; on holidays, only overtime availability contributes.
- Missing/non-positive efficiency uses the explicit 20% fallback and emits a warning.
- A positive plan-level standard-minute value overrides standard-time lookup; otherwise Oracle -> SQL Server -> default priority is retained.
- The 30% parser placeholder is not used as final employee-specific duration during dispatch.
- Test 490 net normal minutes + 90 overtime minutes separately from the 480 + 90 example so shift-configuration changes cannot be hidden.

## 12. Product continuity, weekly completion, high-workload priority and manual rebalancing

[OWNER REQUIREMENT — NEW]

### 12.1 Weekly shipment completion

- The whole-week production plan is a shipment-bound workload: all imported product quantities are intended to be completed within the selected seven-day production week (Friday through Thursday).
- For each product P, calculate its total standard workload as:

```
ProductStandardWorkMinutes(P)
    = ProductQuantity(P) * StandardMinutesPerUnit(P)
```

- In the owner's example, product `YT13E01726P1-02` has quantity 50 and system standard time 263.26 minutes per unit:

```
50 * 263.26 = 13,163 standard minutes for the product
13,163 / 7 = 1,880.43 standard minutes/day on average
```

- Use the positive `GioCongDinhMuc` from the plan when supplied; otherwise preserve the existing Oracle -> SQL Server -> default source priority. For this planning rule, the owner's example treats the retrieved system standard time as minutes for one product unit. Do not multiply quantity a second time after the parser has already expanded a quantity into unit tasks.
- Daily demand is not required to be exactly one seventh on every date. Spread the product's work across actual employee/date capacity and operational constraints, while keeping the shipment-week completion deadline.
- Calculate workload/capacity using decimal precision. The assigned quantities across all employees/dates must sum to the imported product quantity exactly; no units may be duplicated or lost.

### 12.2 Estimate required staffing from each employee's actual efficiency

For every eligible employee/date, use the target from section 11:

```
WorkerDateStandardCapacity
    = AvailableClockMinutes * EmployeeEfficiencyPercent / 100

WeeklyStandardCapacity(worker)
    = SUM(WorkerDateStandardCapacity across the selected 7 dates)

AvailableStandardCapacity(product/team)
    = SUM(WeeklyStandardCapacity for all selected team members)
```

- Estimate required staffing from the total product standard workload divided by the actual per-person weekly standard capacity. Use the real employee roster, efficiency values, work calendar, overtime, leave and holidays; do not use a universal average when actual employee data is available.
- The owner's rough example uses approximately 250 standard minutes per person per day. One person then provides approximately 1,750 standard minutes over seven fully available dates. The product requires `13,163 / 1,750 = 7.52`, hence at least 8 equally productive, fully available employees by this rough estimate.
- If selected workers have lower efficiency or fewer available days, their summed capacity is lower; the required headcount may increase, for example to around 10 in the owner's estimate. Do not hardcode 8 or 10: calculate it from each person's actual availability and efficiency.
- Higher-efficiency and lower-efficiency employees contribute according to their individual capacities. Do not first average all percentages and apply that average to every worker. Adding a worker increases capacity only by that worker's own eligible available minutes multiplied by their own efficiency.
- The staffing estimate is a feasibility check, not a promise. If the available team's capacity is below the product workload, show the precise deficit and the additional capacity/headcount needed; do not silently mark the product as completable.
- Headcount/team sizing must also respect physical jig availability and the T/J metadata. The existing T/J rules provide worker-count targets (T: one or two; J: two/three/four under the documented C thresholds), but the owner has now supplied examples that can require 8–10 people for a high-workload product. Before implementation, determine whether T/J counts are baseline targets that may be increased for weekly feasibility, or strict bounds requiring multiple teams. Never exceed physical concurrent jig-set count O. See KNOWN_GAPS.md.

### 12.3 Stable product ownership and low changeover

[OWNER REQUIREMENT — NEW]
- Prefer to assign one stable employee/team to one product and keep that employee/team working on that product continuously across the required dates until its assigned quantity is complete.
- Minimize frequent switching between product codes for the same worker/team. Avoid alternating small portions of many unrelated products when a contiguous product block is feasible.
- For a product needing many workers, build the required crew/team(s) from the capacity estimate and keep their membership stable across dates whenever attendance permits.
- If the product's workload requires more capacity than the current crew can supply before the weekly deadline, expand that crew or add a separate team where the physical jig-set limit and other constraints permit. Do not split work across extra teams merely for cosmetic load balancing.
- A worker/team may change products after its assigned quantity is completed or when a genuine exception requires it (absence, capacity shortfall, urgent priority or an approved manual change).
- Keep same-product work continuity as a strong optimization objective, not an absolute restriction that can make the weekly shipment impossible. Weekly feasibility, actual availability, skills and jig capacity remain hard constraints.

### 12.4 Start high-workload products early and reserve recovery time

[OWNER REQUIREMENT — NEW]
- Calculate total standard workload per product (`quantity * standard minutes per unit`) and prioritize products with large total standard workload for earlier start and earlier planned completion.
- Large-workload products must not be postponed to the final days merely because their individual Lots are being balanced with smaller products. The reason is to leave time for inspection, correction and rework if defects are discovered; rework on a large-workload product may consume substantial time.
- When sequencing, prefer to start high-workload/long-lead products early, allocate enough stable capacity to finish their initial run early, and leave a visible recovery window before the shipment-week end.
- Do not invent a fixed number of buffer days or a fixed rework percentage. The owner has not specified a numeric reserve yet. Represent the reserve explicitly in the plan/risks and allow it to be configured once approved.
- High-workload priority is a scheduling preference. It must not override employee availability, T/J/jig constraints, skills/qualification or actual clock capacity.
- When rework is reported, treat the rework quantity/time as additional tracked workload for the affected product, do not silently overwrite the original planned quantity, and prioritize it according to the remaining time to shipment. The actual rework data contract still needs implementation.

### 12.5 Manual editing after automatic dispatch

[OWNER REQUIREMENT — NEW]
After automatic dispatch, the planner must be able to adjust the team and work assignments without having to discard and regenerate the entire plan:

- Reassign an employee or an unstarted Lot/product allocation to another eligible employee.
- Add one or more employees to an existing product team when the product is at risk of missing the weekly completion target.
- Remove an employee from a product team, or replace an employee who becomes unexpectedly absent.
- Change team composition while preserving the product's remaining total workload and shipment quantity.
- Recalculate affected employee/date capacities, each employee's standard-minute target and assigned standard minutes, actual clock duration, day timeline, product estimated completion and any overload/shortfall warnings after every accepted edit.
- Revalidate hard constraints on every manual edit: employee/group eligibility, skill requirements, date availability/leave, holiday/overtime windows, actual clock capacity and physical jig concurrency. A manual operation must not bypass a hard constraint.
- Mark owner-edited assignments as manual/pinned. Before a subsequent Auto Dispatch run, make the preserve-versus-reset behavior explicit to the user; never silently discard manual edits. Whether pinned edits are preserved by default or require an explicit preserve option remains to be confirmed.
- Keep the assignment and manual-change history in the current in-memory plan in this phase; export the final adjusted result to Excel. Do not add production-database writes or claim durable storage.

### 12.6 Unexpected absence and remaining work

[OWNER REQUIREMENT — NEW]
- If a worker becomes unavailable after dispatch, allow the planner to mark that employee unavailable for the affected date(s), then identify all future/unstarted work assigned to them.
- Reallocate only the remaining incomplete quantity/workload to qualified available workers; preserve already completed production and do not duplicate the same unit.
- Prefer the existing product team or replacement workers with relevant product experience to maintain continuity and reduce setup/changeover.
- Recalculate whether the product can still finish before the weekly shipment deadline. If not, show the capacity deficit and request an operational decision rather than overbooking.
- If a Lot is already partly in progress, only its remaining uncompleted quantity may be transferred. The current source model does not store actual completed quantity/progress, so progress capture must be designed before safe mid-Lot reassignment can be implemented.

### 12.7 Required tests for product load, continuity and editing

- Product total standard work equals quantity multiplied by standard minutes per unit; for `YT13E01726P1-02`, 50 * 263.26 = 13,163.
- Seven-day average for the same product is 13,163 / 7 = 1,880.43 standard minutes/day; daily scheduling may vary with actual employee capacity.
- With 250 standard minutes/person/day and seven fully available dates, 8 people provide 14,000 standard minutes, while 7 provide 12,250; the rough minimum is 8. Use actual individual capacities in production tests.
- A lower-efficiency or partially available roster produces a larger required headcount when its summed capacity is lower; do not use an ungrounded fixed headcount.
- The scheduler identifies a weekly capacity deficit before claiming the product can be completed.
- Product assignment favors a stable team and minimizes product switching when the seven-day completion constraints remain feasible.
- A high-total-standard-work product is started earlier and has a visible recovery window; no fixed buffer value is assumed until configured.
- A product's quantity and standard workload are conserved when split among workers/teams. Assigning more team members must not multiply the product quantity or its total 13,163 standard-minute work pool.
- Adding a worker changes team capacity by that worker's own per-date efficiency-adjusted availability.
- Removing/replacing an absent worker redistributes only remaining incomplete work and preserves completed output.
- A manual reassignment/add/remove operation updates all affected timelines, standard-minute balances, capacity checks and completion estimates.
- Manual/pinned choices survive Auto Dispatch reruns unless the user explicitly resets them.
- A manual edit cannot create duplicate unit assignments, lose work, exceed actual capacity or violate jig concurrency.
- A partially completed Lot cannot be reassigned safely until completed quantity/progress is represented; test this boundary explicitly.
