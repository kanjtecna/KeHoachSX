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
7. What data source defines number of jigs per product and cut-wire quantity?
8. What are the numeric thresholds for few/many wires?
9. How are leave days and partial-day absence represented?
10. When an assigned worker is absent, can a replacement inherit the remaining task? Which skill/efficiency priorities apply?
11. What happens when the whole weekly plan exceeds total available capacity?
12. Must manual assignment survive rerunning Auto Dispatch?
13. Is the API intended for other clients now, or only a planned host integration?
14. Should JSON employee files be a fallback only, or should the application support offline operation from a versioned local snapshot?


## 9. Calendar, overtime, leave and jig planning questions

The full new rule set is in `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md`. The following points are blocking before production implementation:

1. **Week anchor:** when viewing a week, should it always be the Friday–Thursday week containing the currently selected date? (The written rule assumes yes.)
2. **Weekly file format:** does a whole-week file contain an explicit date/day per product row, or is it one weekly pool for the scheduler to spread across seven days?
3. **Group import:** is the group explicitly selected in the UI, derived from a file column, or both? If the file says another group, should import reject it?
4. **Import replacement:** when re-importing the same group/date or group/week, should the app replace that exact scope, append, or detect duplicate product rows and ask?
5. **Overtime approval:** which values/combinations of `STATUS`, `QUANLYDUYET`, `GADUYET` mean an approved overtime record that can be scheduled?
6. **Overtime identity/group:** does `Overtime.ID` always equal `NHANVIEN.UserName`? Must `Overtime.NHOM` match `NHANVIEN.GROUPBOPHAN`?
7. **Overtime formats/duplicates:** what are actual examples of `Overtime.date` and `SOGIO`? Can an employee have multiple records for one date; sum them, choose latest, or reject duplicates?
8. **Overtime timeline:** on normal days, does overtime start immediately after 17:00? What start time applies on holidays? Are there overtime breaks that reduce usable SOGIO?
9. **Holiday recurrence:** what real values appear in `nghile.repetition_type` and `date`? Does `holiday_id` reference another holiday master table?
10. **Leave NULL status:** the literal SQL condition `TRANGTHAI <> '7'` excludes NULL rows. Should NULL be ignored or treated as a pending/active leave request?
11. **Leave partial day:** should the first implementation block the full date based on STARTDATE/ENDDATE, or honor STARTTIME/ENDTIME/LOAITHOIGIAN/BUOINGHI?
12. **T category:** for L=T, N in (2,3), C>50, what rule chooses one worker versus two?
13. **J category:** for L=J and N>=2, what is the target worker count when C<200?
14. **N meaning/cap:** does N place a maximum on worker count? What if N=2 and C>=400 implies four workers?
15. **Jig/team model:** can multiple workers cooperate as one group using one jig set? Does O=2 permit two groups to work in parallel on one product?
16. **Missing jig values:** what should happen when L, N or O is blank/zero/invalid? Current sample contains missing values.
17. **Current workbook layout:** confirm production exports will continue to have no header row and keep fixed positional columns B/C/L/M/N/O.
18. **Calendar time zone:** confirm the plant's business time zone and normal/holiday shift start/end. Current code defaults to 07:30–17:00 with 80 minutes breaks, but holiday overtime start time is not represented.

Do not make these decisions by guess. Use conservative diagnostics and ask the owner before enabling ambiguous scheduling behavior.
