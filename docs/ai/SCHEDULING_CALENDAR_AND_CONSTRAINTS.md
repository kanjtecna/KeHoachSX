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
- Keep the source quantity attached to its originating group. When two groups share one product code, each group's allocated quantity must remain separately traceable; do not merge quantities and later redistribute them arbitrarily.
- The assignment result must identify each worker's actual group and preserve the per-group quantities.
- The sample workbooks contain six product codes planned in two groups. This is an approved example of controlled sharing, not permission to spread every product across groups.

[IMPLEMENTATION GUIDANCE]
- Treat "same product code" and "same worker team" as different concepts. A product may have separate teams in two groups; a team may also contain workers from more than one group only when the chosen allocation requires that and the result preserves each group's source quantity.
- Prefer a strong same-group-first optimization objective. Only introduce a second group when the one-group allocation cannot meet capacity/skill/workload constraints or a clear operational benefit is required.
- Record/communicate why cross-group sharing was needed so it can be reviewed by the production manager.

[OPEN — INPUT COLUMN SEMANTICS]
- In the supplied LR4 workbook, the second column contains values LR4 through LR13, while the other group files contain LR1, LR2 and LR3 in that column and the target result lists employee departments LR1–LR4. Confirm what LR5–LR13 represent (work-cell/team labels, a different group hierarchy, or invalid values) before mapping them to employee departments. Do not silently treat LR5–LR13 as LR4.

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
1. Product belongs to the selected plan scope (date/week) and selected group.
2. Worker is active and eligible for the selected group.
3. Worker is not on a qualifying whole-day leave on that date.
4. Worker has positive availability on that date (normal shift and/or overtime record selected under the initial no-status-filter rule).
5. If the date is a company holiday, the worker has a matching overtime record for that date and receives only the 08:00 + SOGIO window.
6. Product T/J rules determine the required/allowed team size according to section 6.
7. Physical jig-set count O limits the number of independent teams concurrently using that product, not the number of people inside a team.
8. Skill, efficiency and workload ranking selects among eligible workers only; ranking must never override any hard constraint.
9. The task must be represented on the correct dated timeline, including normal breaks and overtime boundaries.
10. All imported product quantities must be accounted for; overload, missing metadata and unassigned work must remain visible.
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
- The same product remains in one group when feasible; cross-group splitting uses no more than two groups by default and preserves each group's source quantity.
- Every imported quantity is either scheduled or reported as unassigned/overloaded.
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


## 10. Supplied import/result workbooks — reconciliation baseline

The following five uploaded workbooks were inspected as one input/output example. The workbooks themselves are not copied into repository documentation, and employee names/IDs are intentionally not reproduced here.

### 10.1 Input workbook structure observed

The four group input workbooks have no header row and use three columns:
- Column A: product code.
- Column B: group/work-cell label (the precise semantics of LR4's labels are unresolved).
- Column C: requested quantity.

Observed rows: LR1 = 7; LR2 = 13; LR3 = 19; LR4 = 10. Together they contain 49 product/group rows and 43 unique product codes.

### 10.2 Target result workbook structure observed

The target result sheet contains these headers:
- `UserName`
- `FullName`
- `Nhóm`
- `Status`
- `Mã sản phẩm`
- `Số lượng`
- `Số người làm`
- `số thứ tự nhóm`

The sample contains 101 employee-assignment rows, 78 unique employee IDs, 43 unique product codes and 55 distinct product/team-sequence combinations.

### 10.3 Reconciliation result

Summing output `Số lượng` by product and employee department matches the corresponding input product/group quantity in all 43 products and all source groups; total quantity is 542 in both input and expected result.

| Source group | Input quantity | Output quantity attributed to employee group |
|---|---:|---:|
| LR1 | 97 | 97 |
| LR2 | 154 | 154 |
| LR3 | 156 | 156 |
| LR4 | 135 | 135 |
| Total | 542 | 542 |

Six products occur in two source group files and are also represented across two employee departments in the target result. This validates controlled cross-group planning in the sample.

### 10.4 Target workbook anomalies to clarify

- Six product/team-sequence combinations do not have a row count equal to the value in `Số người làm`: five combinations show 2 workers but only one employee row; one combination shows 1 worker but two employee rows.
- Do not infer that `Số người làm` is merely descriptive or automatically repair the target data. Confirm the intended meaning and whether missing employee rows are intentional.
- In `LR4.xlsx`, column B contains LR4 and labels LR5 through LR13. This must be resolved before using that column as the canonical employee department/group field.
