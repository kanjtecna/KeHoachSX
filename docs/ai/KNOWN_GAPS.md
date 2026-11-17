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

## 7. Project/branch/build concerns

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
