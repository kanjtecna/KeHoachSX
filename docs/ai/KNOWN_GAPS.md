# Known Gaps, Conflicts and Questions

This file prevents AI agents from turning assumptions into “facts”. Items below were observed during the documentation review of main commit `08cc1b68d30ddc067f25ade9e13b355b26a9535e`.

## 1. Business notes not fully enforced in current dispatch code

The notes in `doccuments/data.txt` mention:
- fixture/jig count constraints;
- small/large cut-wire thresholds;
- high-efficiency worker pairing/balancing;
- keeping one worker on unfinished product/SEQ across days;
- leave-aware replacement.

The inspected `ShiftConfig`, `LotTask`, `AssemblyWorker` and `AutoDispatchService` do not show corresponding explicit jig, cut-wire, absence/leave or cross-day commitment fields/constraints. Treat these as requirements awaiting design and validation, not current behavior.

## 2. DispatchOptions does not match active implementation

`DispatchOptions` exposes:
- `BalanceWorkload = true`
- `PrioritizeExperiencedWorkers = true`
- `KeepSameProductContinuous = true`
- `DefaultEfficiency = 100.0`

The inspected `AutoDispatchService` calculates skill/load/continuity unconditionally and does not read these flags. It uses 20% fallback directly for non-positive worker efficiency. This is a configuration-contract mismatch.

**Action:** Do not “fix” by selecting an arbitrary default. Propose one single source of truth, clarify whether options should enable/disable behaviors, add tests and obtain approval.

## 3. Time-unit / formula semantics need confirmation

Code comments describe standard time as minutes per product, but:
- parser initial duration = `standardMinutes / 0.3`;
- dispatch later sets duration = `baseDuration / (efficiency / 100)`.

Potentially the division by 0.3 represents a 30% baseline performance; however this should be confirmed. Do not change or duplicate this factor without owner approval. The displayed UI expression should also be checked for minute/hour conversion consistency.

## 4. Duration and capacity fallback behavior

Current dispatch can fall back to assigning the highest-ranked worker on the least-loaded day when no fit exists. This may exceed daily or weekly capacity. Chunk calculation can extend past shift end.

Owner must decide whether to:
- leave tasks unassigned and show overload;
- allow overtime with explicit overtime status;
- move remainder to next day;
- split tasks across workers/days;
- or apply another approved policy.

Do not silently change this behavior.

## 5. Import parser concerns to verify

- Reader treats extensions other than xlsx/xls as CSV; caller should validate allowed extensions.
- CSV and Excel have slightly different invalid/missing quantity handling.
- Excel tries culture-sensitive numeric parsing in one fallback path; CSV uses invariant culture.
- Negative/zero quantity behavior is normalized to at least one in Application parser, which may hide invalid input.
- Excel `.xls` support through ClosedXML should be verified (ClosedXML primarily targets modern OpenXML Excel formats); do not claim it works without a test.
- Duplicate product/lot row handling and repeated identifiers need defined semantics.
- CSV parser implementation is custom; test escaped quotes, commas/semicolons inside quoted strings and newline-containing fields.

## 6. Data-source concerns to verify

- SQL fallback to JSON occurs both on exception and on zero SQL rows. Confirm whether a valid empty SQL result should mean “no active employees” rather than “use stale JSON”.
- Some source files are searched in multiple legacy paths. Deployment priority and stale-file behavior require a clear policy.
- Skill matrix is loaded once and cached in memory; no reload after files change is visible.
- SQL query uses LTRIM/RTRIM in join/filter columns; index/performance impact should be measured before changing.
- The product-to-worker CSV's delimiter/format is assumed by custom parser. Validate source exports with representative real data (sanitized).

## 7. Oracle read-only verification

- KeHoachSX's own adapter calls external JtecShare service `ILayGioCongHeThong`; the external implementation is not included in this repository.
- Before declaring Oracle read-only end-to-end, inspect that service's implementation and confirm it only executes SELECT/read operations.
- Confirm the configured Oracle database principal has SELECT-only grants for the objects needed by the application, with no DML/DDL privileges.
- AI-generated code must not add any Oracle write path, even if a user story asks for one. Any DBA data/schema correction must remain a separately authorized manual operation.

## 8. Project/branch/build concerns

- Solution references JtecShare projects outside this repository; full build may fail if local relative paths are absent.
- App targets `net8.0-windows`; API/Infrastructure/Core target net8.0.
- API project exists but endpoint implementation was not verified.
- No test project appeared in the inspected repository tree. Confirm whether tests are in another repo/branch or need to be created.
- Branches observed include `refactor/clean-architecture-v2` and `refactor/data-access-separation`; main already contains a merged clean-architecture refactor. Always work from the intended current branch and inspect divergence before applying documentation or code changes.

## 8. Business questions requiring owner confirmation

1. “Số lượng” in uploaded plan is total product quantity, number of lots, or task units? Current parser creates one LotTask per quantity and sets task quantity to 1.
2. What is the authoritative unit for `GioCongDinhMuc`: minutes per unit, minutes per lot, or hours?
3. Why is the baseline divisor 0.3 used? Is it a fixed business rule, configurable baseline, or legacy compensation?
4. Is efficiency stored as percentage (e.g. 20) rather than ratio (0.2)? What range is valid?
5. Is a worker allowed to work on more than one product per day?
6. Does “keep the same worker tomorrow” apply to a whole product, a SEQ, a Lot, or remaining quantity?
7. What is the source/meaning of cut-wire quantity, and what thresholds define few versus many? Jig count for this scheduling requirement comes from column O in `dulieubanga.xlsx`.
8. What are the numeric thresholds for few/many wires?
9. When a worker is absent on a later date, should unfinished assigned work be transferred to a replacement worker, and how?
10. When an assigned worker is absent, can a replacement inherit the remaining task? Which skill/efficiency priorities apply?
11. What happens when the whole weekly plan exceeds total available capacity?
12. Must manual assignment survive rerunning Auto Dispatch?
13. Is the API intended for other clients now, or only a planned host integration?
14. Should JSON employee files be a fallback only, or should the application support offline operation from a versioned local snapshot?


## 9. Remaining calendar, import, overtime and jig decisions

The owner has confirmed the primary scheduling rules. Do not re-open these settled decisions unless the owner changes them:
- Whole-week file is one weekly demand pool for the selected group, automatically spread over Friday–Thursday.
- Overtime begins at 17:00 on normal dates and 08:00 on holidays; `SOGIO` provides the overtime duration.
- No approval-field filter is required in the first phase; do not filter by `STATUS`, `QUANLYDUYET` or `GADUYET` yet.
- `nghile.date` contains one-off, non-recurring holidays.
- `TRANGTHAI <> 7` means exclude the employee for the entire covered date; partial-day fields are ignored in this phase.
- Category T can use one or two workers chosen by scheduling fit.
- For category J, `N >= 2` is a precondition, not a headcount cap; C thresholds are 200/300/400 for 2/3/4 workers.
- Column O limits concurrent independent teams using physical jig sets; multiple workers within one team share one jig set.
- Plan persistence is deferred; assignment result is exported to Excel.
- Re-importing the same group and same date/week replaces that exact plan scope; it does not append.
- Missing jig metadata means skip the jig constraint for that product, continue scheduling under other constraints, and show an informational warning.
- If capacity is insufficient, leave remaining work unassigned and show a warning; do not force an over-capacity assignment.

The following items still need confirmation or technical inspection before the relevant feature is implemented:

1. **LR4 input group labels:** `LR4.xlsx` column B contains LR4, LR5, ..., LR13, while the target result's employee department values are LR1–LR4. What do LR5–LR13 represent, and how should they map (if at all) to employee department LR4?
2. **Overtime duplicate conflicts:** duplicates are keyed by employee `ID + date` and must contribute only one value, never be summed. If duplicate rows for the same employee/date contain different `SOGIO`, should we keep the greatest `No` (latest inserted row), or use another authoritative field/rule? Until confirmed, flag conflicting values.
3. **Overtime group field:** does `Overtime.NHOM` have to match `NHANVIEN.GROUPBOPHAN`, or is employee ID alone authoritative? ID mapping itself is confirmed.
4. **Jig reservation window:** does a physical set remain reserved during employee breaks and overnight/day boundaries, or can another team use it during those intervals?
5. **Team-size meaning in target workbook:** six product/team-sequence combinations have a mismatch between row count and `Số người làm`. Are those intentional partial rows, or is the target file missing employee rows / carrying an incorrect team-size value?
6. **Excel export contract:** should the final export mirror the eight columns from `file_ketqua_phanchia.xlsx` exactly, and should it also include daily timeline, unassigned/overload warnings and jig warnings?
7. **Plant time zone/configuration:** confirm the configured business time zone if the deployment can run outside Vietnam; do not derive a business date from machine UTC implicitly.

The full settled rules and current implementation gaps are in `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md`.
