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

[SAFE DATA ISOLATION]
- Every imported plan must be associated with a scope, date or production week, and group.
- Importing one group's file must not delete or overwrite another group's plan.
- Importing a daily plan must not overwrite other dates.
- The selected group must limit eligible workers to that group (current eligible group codes are LR1–LR4).
- A dispatch run for one group must not assign its work to another group unless the owner explicitly approves cross-group assignment.
- Do not infer the file's group or date from its filename. Use explicit UI selection or a verified column/mapping.
- Duplicate import/replacement policy within the exact same group/date or group/week remains an open decision (see `KNOWN_GAPS.md`).

[OPEN]
- The exact Excel/CSV layout for a whole-week import has not yet been supplied. Do not invent a date column or assume every row is a weekly pool until the owner confirms whether products are explicitly assigned to dates or the algorithm distributes a weekly pool across the seven dates.
- Confirm whether group is selected in UI or read from the file. If both exist, define mismatch handling.

## 2. Daily availability and capacity model

The scheduler must calculate availability **per employee, per date**, rather than assigning one fixed weekly capacity to every person.

For each production date:
1. Load eligible employees for the selected group.
2. Apply approved leave exclusion for that date.
3. Determine whether the date is a company holiday/non-working date.
4. Read the employee's valid overtime registration for that date.
5. Compute regular and overtime capacity according to the rules below.
6. Apply jig/fixture constraints before placing tasks.
7. Produce timeline chunks using the actual capacity windows, preserving breaks and overtime boundaries.

A person absent on one date can still be scheduled on other dates in the same week. Do not exclude an employee from the entire week because of a one-day leave or holiday.

## 3. Company holidays — `TA.dbo.nghile`

[OWNER REQUIREMENT]
- Company holiday/non-working dates are stored in SQL Server table `TA.dbo.nghile`.
- Relevant columns supplied by the owner: `id`, `repetition_type`, `date`, `createdAt`, `updatedAt`, `holiday_id`.
- The scheduler must use this source to identify which of the seven dates in the production week are company holidays.

[IMPLEMENTATION RULES]
- Access this SQL Server table through Infrastructure using parameterized, read-only queries.
- Do not write to or modify this table as part of dispatch.
- Do not compare the `date` NVARCHAR field lexicographically. Parse/normalize it to a date using the actual stored format.
- Interpret `repetition_type` and `holiday_id` only after inspecting actual sample rows and related table schema. Do not assume recurrence semantics.
- A date classified as a company holiday is not automatically a normal 490-minute shift.

[OPEN]
- Need representative rows and the actual formats/values of `date`, `repetition_type`, and related `holiday_id` data.
- Confirm whether recurring holidays are expanded annually/weekly, and whether the authoritative date comes from `nghile.date` or a related holiday master table.

## 4. Overtime — `TA.dbo.Overtime`

[OWNER REQUIREMENT]
Provided table:
- `TA.dbo.Overtime`
- `ID`: employee identifier.
- `date`: overtime date (NVARCHAR).
- `SOGIO`: number of overtime hours.
- Other columns include `STATUS`, `NGUOIDANGKY`, `QUANLYDUYET`, `GADUYET`, `NHOM`, `LYDO`, `createdAt`.

Only `SOGIO` contains the overtime duration according to the owner. Overtime is employee-specific and date-specific.

### 4.1 Normal working date

[OWNER REQUIREMENT]
- Start from the normal shift capacity.
- If the employee has valid overtime on that date, add `SOGIO × 60` minutes to that employee's available time.
- Extend the employee's timeline to display the overtime interval; do not merely increase a KPI number without extending the scheduling timeline.
- Overtime registered for one employee must not increase another employee's capacity.

Current normal-shift defaults observed in code are 07:30–17:00 with 80 minutes of breaks, giving 490 net working minutes. Preserve this only as the current default unless an approved shift configuration changes it.

### 4.2 Company holiday/non-working date

[OWNER REQUIREMENT]
- Only employees who have a valid overtime registration for that exact date may be scheduled on a company holiday.
- Employees without a valid overtime record have zero available working minutes on that holiday.
- For an eligible employee, available working duration on the holiday is `SOGIO × 60` minutes. Do not also add a normal shift on a company holiday unless the owner explicitly defines that date differently.
- Show the holiday overtime interval on the timeline and use it in task placement.

### 4.3 Validation and approval

[OPEN — BLOCKING]
- `STATUS`, `QUANLYDUYET` and `GADUYET` exist, but the owner has not defined which combination means the overtime registration is approved/valid. Do not treat every row as approved. Do not invent status numbers or approval semantics.
- Confirm whether `Overtime.ID` always matches `NHANVIEN.UserName` / worker `ID`.
- Confirm whether `Overtime.NHOM` must match `NHANVIEN.GROUPBOPHAN`, and how mismatches are handled.
- Confirm the actual stored date format in `Overtime.date` and numeric format of `SOGIO` (integer/decimal, dot/comma).
- The table has no explicit overtime start time. Confirm whether normal-day overtime starts immediately after the regular shift end (currently 17:00), and what start time applies on a holiday.
- Confirm whether overtime includes separate break intervals or whether the full `SOGIO` is usable task time.
- Confirm whether duplicate registrations for the same employee/date can exist and whether to sum, choose latest, or reject them.

Until approval semantics and duplicate handling are confirmed, the implementation must not assign people to overtime-only windows based merely on the presence of an arbitrary row.

## 5. Leave/absence — `STOCKMANAGEMENT.dbo.thutuchanhchinh`

[OWNER REQUIREMENT]
The table contains employee leave/administrative request records. Relevant fields:
- `ID`: employee identifier.
- `STARTDATE`: leave start date/time.
- `ENDDATE`: leave end date/time.
- `TRANGTHAI`: request status.

Filter rule supplied by the owner:
- Consider records where `TRANGTHAI <> 7`.
- The leave interval is bounded by `STARTDATE` and `ENDDATE`.
- Match the employee using `ID`.

### 5.1 Scheduling behavior

- For a target date covered by a qualifying leave record, the employee must not receive work for that date.
- Apply leave per date, not to the entire week, unless the leave interval spans the entire week.
- Use inclusive date overlap as the proposed interpretation: `STARTDATE <= end-of-target-date` and `ENDDATE >= start-of-target-date`. Confirm how the application should treat timestamps and half-day records.
- A leave record must not be inferred from `NAME`, `LOAIPHEP` or free-text reason; use the specified ID/status/date fields.
- Query SQL Server read-only with parameters; do not write back or approve leave requests.

### 5.2 Partial-day leave

[OPEN]
The table also has `STARTTIME`, `ENDTIME`, `LOAITHOIGIAN`, `BUOINGHI` and related fields, but the supplied rule currently defines only STARTDATE/ENDDATE. Until the owner provides the half-day/hourly rules, treat date-overlapping records as unavailable for the whole date (conservative policy), or stop for owner confirmation if that would materially reduce capacity. Do not try to infer morning/afternoon from leave-type text.

[SQL NULL NOTE]
Because `TRANGTHAI` is NVARCHAR, SQL predicate `TRANGTHAI <> '7'` does not include NULL rows. Confirm whether NULL status should be excluded or treated as an active leave request. Do not silently change the supplied filter.

## 6. Jig/fixture constraints — `dulieubanga.xlsx`

### 6.1 Verified shape of the supplied workbook

The uploaded workbook `dulieubanga.xlsx` was inspected:
- One worksheet: `Sheet1`.
- Used range: `A1:W494` (494 rows, 23 columns).
- Row 1 contains a data record, **not a header row**.
- Column B contains 494 unique product codes in this supplied snapshot.
- Column L contains `T`, `J` or blank values.
- Columns N and O contain integer values in the observed range 1–6 where present, but some rows have missing values.

The current sample must therefore be read using the owner's positional column mapping. Do not skip row 1 as a header. Do not assume every future export has the same shape without validating it.

### 6.2 Column mapping supplied by the owner

| Excel column | Use in scheduling |
|---|---|
| B | Product code / `MaSanPham` |
| C | Numeric quantity/workload value used by the worker-count rules |
| L | Jig/fixture category flag: `T` or `J` |
| M | Fixture/jig description or identifier (observed sample; informational until defined) |
| N | Numeric condition used by the T/J rules; exact business meaning still needs confirmation |
| O | Number of physical jig/fixture sets available for the product |

Do not rename or reinterpret the columns without owner confirmation.

### 6.3 Category T rule [OWNER REQUIREMENT]

When all conditions hold:
- Column L = `T`;
- Column N is 2 or 3;
- Column C > 50;

then the product can be assigned to **one or two workers**.

[OPEN]
The owner has not yet specified the threshold/decision rule for choosing one worker versus two workers. Do not invent a quantity threshold or always choose two. Treat this as a configurable/pending decision and expose it for confirmation.

### 6.4 Category J rule [OWNER REQUIREMENT]

When:
- Column L = `J`;
- Column N >= 2;

the worker-count target depends on column C:
- C >= 400: 4 workers.
- C >= 300 and < 400: 3 workers.
- C >= 200 and < 300: 2 workers.

[OPEN]
- Worker count for C < 200 is not specified.
- It is not yet clear whether column N limits the computed worker count (for example N=2 but C>=400 implies 4 workers) or is only an eligibility condition.
- Do not extrapolate beyond the given thresholds.

### 6.5 Physical jig-set capacity — column O [OWNER REQUIREMENT]

- Column O is the number of physical jig sets available for the product.
- O=1 means only one jig set can be used at a time.
- At one time, only one worker **or one worker group/team** may use that single set.
- O is a limit on concurrently active jig groups/sets, not automatically a limit on the number of workers in one group.
- If O=2, the scheduling engine may consider at most two concurrently active groups for that product, subject to the other worker-count rules and actual resource availability.
- A jig set cannot be double-booked by overlapping tasks. The resource must be reserved for the relevant task time interval and released when the task finishes or pauses according to the approved process.

[OPEN]
- Confirm whether multiple workers may cooperate as one group using one jig set at the same time.
- Confirm whether O>1 allows parallel groups on the same product and how tasks/lots are divided among those groups.
- Confirm how to handle O=0, blank O or non-numeric values. Do not treat missing jig-set data as unlimited capacity.
- Confirm whether a jig set is reserved across break periods and overnight/day boundaries or released during those intervals.

### 6.6 Missing/invalid jig metadata

- Do not silently substitute N=1, O=1 or unlimited jig capacity when values are missing.
- Flag the product as `JigDataNeedsReview` (or an equivalent explicit validation result).
- Do not create multiple parallel groups without valid jig metadata.
- The owner must decide the conservative fallback for products with blank L/N/O before the feature is enabled in production.

## 7. Combined dispatch constraints and evaluation order

The future scheduler must evaluate a task against all of these dimensions:
1. Product belongs to the selected plan scope (date/week) and selected group.
2. Worker is active and eligible for the selected group.
3. Worker is not on qualifying leave on that date.
4. Worker has positive availability on that date (normal shift and/or valid overtime).
5. If the date is a company holiday, worker has valid approved overtime for that exact date.
6. Product's T/J rules determine a permitted worker/team target once all missing thresholds are approved.
7. Physical jig-set count O limits the number of concurrent jig groups using that product.
8. Skill, efficiency and workload ranking selects among eligible workers only; ranking must never override leave, holiday, group, time-capacity or jig hard constraints.
9. The task must be represented on the correct dated timeline, including breaks and approved overtime periods.
10. All input product quantities must be accounted for; overload, missing metadata and unassigned tasks must be visible.

Hard constraints (eligibility, leave, holiday/overtime authorization, available minutes, jig resource capacity) take precedence over soft scoring (skill, workload balance, continuity).

## 8. Required tests for the scheduling extension

At minimum add deterministic automated tests for:
- App defaults to the local current date.
- Today/Tomorrow select exact calendar dates.
- Friday–Thursday week calculation, including dates on Friday and Thursday.
- Seven-day week indexing and correct date-to-timeline mapping.
- Daily import does not overwrite other dates.
- One-group import does not overwrite other groups.
- Each worker is excluded only on leave dates, not the whole week.
- `TRANGTHAI = 7` does not exclude; `TRANGTHAI <> 7` follows the approved NULL policy.
- Holiday with no valid overtime registration => zero capacity.
- Holiday with valid overtime => capacity from SOGIO only.
- Normal day without overtime => regular shift capacity.
- Normal day with overtime => regular shift plus SOGIO minutes.
- Overtime belongs only to the matching employee/date/group.
- Overtime is not accepted when the registration is not approved.
- T category conditions and both worker-count choices after thresholds are defined.
- J thresholds at C=199, 200, 299, 300, 399, 400.
- O=1 prevents two concurrent jig groups; O=2 prevents more than two.
- Multiple workers inside one team do not accidentally consume multiple jig sets if one team shares one set.
- Missing/invalid L/N/O is flagged, never silently interpreted.
- A worker's task timeline never overlaps their leave or another task, and a jig set is never double-booked.
- Manual reassignment cannot bypass group, leave, holiday/overtime or jig constraints.

Do not mark the feature complete until the open business questions affecting these tests have been answered and accepted by the owner.
