# Business Rules — Weekly Assembly Planning and Dispatch

## 1. Evidence labels

- **[CODE]**: behavior directly observed in source on baseline commit.
- **[NOTE]**: rule stated in `doccuments/data.txt`; it is a business note but may not be implemented.
- **[OPEN]**: requires owner confirmation.
- **[TARGET]**: desired rule to be implemented only after owner approval.

Never present NOTE/TARGET/OPEN as implemented code behavior.

## 2. Key concepts

| Term | Meaning evidenced in current model |
|---|---|
| Weekly plan | Uploaded CSV/Excel rows represented by `WeeklyPlanItem` |
| Product code | `MaSanPham` |
| Quantity | `SoLuong` from each plan row |
| Lot | Unit task represented by `LotTask`; parser expands quantity into tasks |
| Lot order | `TTLot` and display/name `SoLot` |
| Standard time | `GioCongDinhMuc`, treated as minutes in comments |
| Adjusted work duration | `ThoiGianThucTe`, adjusted by worker efficiency |
| Worker | `AssemblyWorker`, identified by `WorkerId` |
| Efficiency | `HieuSuat`, percentage |
| Skill count | Number of historical times worker performed product code |
| Jig/fixture | Mentioned in business notes; no structured jig property verified in current model |
| Worker leave | Mentioned in business notes; absence model/source not verified |

## 3. Employee eligibility [CODE]

SQL repository filters:
- `NHANVIEN.STATUS = '1'`.
- `GROUPBOPHAN` is LR1, LR2, LR3 or LR4 (case/space normalized).
- `LevelUser` is not CHIEF, LEADE or LEADER.
- The returned fields include UserName, Fullname, GROUPBOPHAN and latest usable efficiency.

JSON fallback applies the same active/group/level eligibility rules in Application service.

Do not broaden the eligible groups or include excluded levels without approval.

## 4. Efficiency [CODE]

- SQL query uses the latest record by descending `HIEUSUATNHANVIEN.[No]`.
- Non-positive/unparseable/missing efficiency falls back to 20.
- JSON employee fallback joins efficiency by employee ID and also falls back to 20.
- Dispatch duration formula:
  `adjustedMinutes = baseMinutes / (efficiencyPercent / 100)`.
- Example: base 30 minutes at 20% => 150 minutes; at 50% => 60 minutes.
- The current `DispatchOptions.DefaultEfficiency` property is 100, but the current dispatch calculation does not read this option and itself uses 20 when worker efficiency <= 0. Do not rely on this option as active behavior.

## 5. Weekly plan import [CODE]

- Supported file extensions in reader: `.xlsx`, `.xls`, otherwise CSV path.
- Excel uses the first worksheet and first used row as header.
- CSV detects comma or semicolon from header, parses quoted fields and normalizes Vietnamese header text.
- Header matching recognizes product, quantity, lot/sequence and standard-time fields; fallback columns are used when headers are not detected.
- Product code must not be blank; blank product rows are skipped.
- Invalid/missing quantity currently defaults to 1 in parser path; parser uses `Math.Max(1, quantity)`.
- Missing lot label defaults to `01` or `L01` depending on input path.
- Positive standard-time value from uploaded plan takes precedence over data source lookup.
- Exact supported column semantics and duplicate-row business treatment should be confirmed before changing the template.

## 6. Lot expansion and standard time [CODE]

For each valid plan item:
1. Determine standard minutes:
   - use positive value from uploaded plan;
   - otherwise query Oracle;
   - otherwise query SQL Server;
   - otherwise use 60 minutes.
2. If quantity <= 1 after normalization, create one LotTask.
3. If quantity > 1, create one LotTask per unit, with incremented lot label and sequential `TTLot`.
4. Each created LotTask has `SoLuong = 1`.
5. The parser initializes `ThoiGianThucTe = standardMinutes / 0.3`.
6. During auto-dispatch, duration is recalculated using the assigned worker's efficiency.

The unit “minutes” is inferred from model comments and calculations. If source spreadsheets use hours or another unit, confirm and normalize before changing.

## 7. Standard-time source priority [CODE]

This is a **read fallback order for one value (standard minutes)**, not permission to write to Oracle.

`WeeklyPlanParserService` first uses a positive standard-time value supplied in the uploaded plan. Only when that value is missing/non-positive does it call `StandardTimeService`.

`StandardTimeService` then:
1. Checks its in-memory cache.
2. On cache miss, tries the optional Oracle service via JtecShare, called by the current adapter with operation code `1523`.
3. If Oracle is unavailable, throws, or returns no positive value, queries SQL Server `STOCKMANAGEMENT.dbo.SANLUONGCAMLR` for the latest positive `GIOCONG` by `TIME`.
4. If SQL Server also has no positive result or fails, uses 60 minutes.

Example: uploaded plan has no standard time -> Oracle returns 42 -> use 42 and cache it; Oracle has no value -> SQL Server returns 48 -> use 48; neither provides a positive value -> use 60 minutes.

**Oracle is read-only.** AI must never create Oracle data/schema modification logic. See `AGENTS.md` and `DATA_SOURCES.md`.

## 8. Shift and breaks [CODE]

Default `ShiftConfig`:
- Start: 07:30.
- End: 17:00.
- Number of days: 6, intended Monday–Saturday.
- Morning break: 10:00–10:10.
- Lunch: 12:00–13:00.
- Afternoon break: 15:00–15:10.

Calculated:
- Elapsed day = 570 minutes.
- Breaks = 80 minutes.
- Net daily capacity = 490 minutes.
- Weekly per-worker capacity = 2,940 minutes.

`CalculateChunks` splits task duration around break intervals. It may create a chunk extending beyond configured end time if remaining duration exceeds the shift; this behavior needs an explicit business decision and tests before changing.

## 9. Current auto-dispatch algorithm [CODE]

Current observed algorithm in `AutoDispatchService`:
1. Clones workers and tasks into a result.
2. Assigns a display color per product code (presentation metadata).
3. Sets each worker daily and weekly capacities from ShiftConfig.
4. Groups LotTasks by product and orders product groups descending by `lot count * first task ThoiGianThucTe`.
5. Orders lots within each product by ascending `TTLot`.
6. For each LotTask, computes base duration from positive `GioCongDinhMuc`, otherwise `ThoiGianThucTe`, otherwise 60.
7. Calculates candidate worker score:
   - skill score: exact product historical count, logarithmic formula capped at 1; if no exact count but same first-six-character product prefix exists, score 0.2;
   - workload score: remaining weekly capacity ratio, floor at 0;
   - continuity bonus: 0.35 if this worker's last assigned product is the current product.
8. Total score = skill * 0.50 + workload * 0.35 + continuityBonus * 0.15. The current `continuityBonus` is 0.35 when the worker's last assigned product equals the current product; the resulting maximum additive contribution is 0.0525 (not 15 percentage points).
9. If assignment would exceed worker weekly capacity, subtracts 2.0 from score.
10. Tries candidates by descending score and days from day 0 upward, fitting within daily net capacity.
11. If no candidate/day fits, assigns to highest-ranked candidate on that worker's least-loaded day, potentially exceeding capacity.
12. Uses `GetTaskDuration` based on worker efficiency and calculates time chunks around breaks.

Important limitations:
- `DispatchOptions` flags are not visibly used in the current algorithm in the inspected implementation.
- Current task ordering is LPT-like; it does not visibly implement Most Constrained First.
- The continuity bonus is a scoring bonus within the in-memory run, not a persisted cross-day commitment.
- No explicit leave/absence calendar is visible in the models inspected.
- No explicit jig-count or jig-capacity input is visible in the current domain models.
- No explicit “do not split the same product across workers” hard constraint is visible.
- The algorithm's fallback can exceed weekly/daily capacity. This must be surfaced, not hidden.

## 10. Additional business notes [NOTE — not fully implemented]

The repository's `doccuments/data.txt` states:
1. Pair workers with differing efficiencies in an alternating/balancing way for the same product; avoid putting two workers with similarly high efficiency on one product.
2. Consider the number of jig/fixture sets for a product. If there are two sets, work can be split to another group; otherwise move remaining work to another day.
3. If a worker starts a large SEQ/product quantity today, continue with the same worker tomorrow.
4. Do not insert another product between days while the current product remains unfinished, unless a justified exception exists.
5. If a person in a multi-person group or a worker continuing unfinished work will take leave the next day, reassign to another worker.
6. A product with three TV jigs but a small number of cut wires should be assigned to one worker (at most two).
7. A product with one jig and many cut wires should normally be assigned to one worker.
8. A product with one jig but a large output quantity may require more workers.
9. If efficiency is unavailable, use 20%.

These are business notes, not verified algorithm constraints. Before implementing them, owner must clarify:
- Definition/units for “số dây cắt”, threshold for “ít/nhiều”, and whether quantity is per day, lot, or weekly total.
- The jig count source for this requirement is column O in `dulieubanga.xlsx`. One team may contain multiple workers and share one jig set; O limits the number of independent teams using that product concurrently.
- Whether and how a physical jig set is released during breaks and overnight/day boundaries.
- Whether assignment is per product, per SEQ, per lot, or per operation/day.
- Priority between skill expertise, efficiency balance, workload and jig availability.
- What qualifies as “same worker continues tomorrow” and exceptions when absent.
- Whether a replacement worker continues the same product task from remaining quantity or restarts a lot.
- How unfinished work is handed over to a replacement worker when the originally assigned worker is absent on a later date.

## 11. Required invariants [TARGET — approve before implementing]

Recommended invariant candidates:
- Every valid imported quantity is accounted for exactly once; no Lot silently disappears or duplicates.
- Each generated Lot has stable identity within the plan and unique order/label within the defined product/plan scope.
- Assigned duration is non-negative and based on a clearly defined standard-time unit and efficiency.
- A worker is never scheduled during configured breaks.
- No worker is assigned while unavailable/absent.
- Jig/fixture capacity is never exceeded once jig semantics are defined.
- Daily/weekly over-capacity is explicitly flagged and not silently treated as a normal schedule.
- Re-running dispatch with identical normalized inputs and configuration yields deterministic output.
- Manual overrides are not silently overwritten by a subsequent automatic dispatch (persistence/override semantics need confirmation).
- Total input quantity equals total output quantity after normalization, with rejected rows separately reported.

These invariants are targets to be approved; do not impose them if they conflict with the owner's operational policy.


## 12. Date-, group-, attendance- and jig-aware scheduling [OWNER TARGET]

The approved target behavior is documented in full in `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md`. Core decisions confirmed by the owner:

- Calendar scopes: Today, Tomorrow and Whole week; production week is seven dates from Friday through the following Thursday.
- A Whole week import is one weekly demand-pool file for the selected group. Its product/Lot work is distributed by the scheduler across all seven dates based on date-specific capacity; rows are not pre-assigned to weekdays.
- Source plan column C is the total quantity required for a product in the source group. The result's `Số lượng` is each employee's individual allocation. The sum of employee allocations for product + department must exactly equal the source quantity.
- Employee allocations may differ; preserve any remainder and do not copy the entire group quantity to every employee. `Số người làm` and `số thứ tự nhóm` are separate grouping metadata, not a reason to multiply or divide the result quantity during source reconciliation.
- The corrected example's normalized summary is `SUM(Số lượng / Số người làm)`; it is distinct from the raw production total and must not replace it.
- On normal dates, overtime starts at 17:00 and adds `SOGIO` hours to the normal shift. On holidays, overtime starts at 08:00 and `SOGIO` is the only available duration.
- In the initial phase, do not filter overtime rows by `STATUS`, `QUANLYDUYET` or `GADUYET`; approval filtering will be defined later.
- Each `TA.dbo.nghile.date` value is one non-recurring holiday date.
- A leave row with `TRANGTHAI <> 7` excludes the matching employee for the entire covered date. Partial-day fields are ignored in this phase; SQL NULL does not satisfy the inequality.
- For category T (L=T, N in 2 or 3, C>50), the scheduler may choose one or two workers according to capacity and allocation fit, without inventing another C threshold.
- For category J (L=J, N>=2), N is only the rule condition, not a worker-count cap: C>=400 => 4 workers; C>=300 => 3; C>=200 => 2. C<200 remains undefined.
- Column O counts physical jig sets and limits concurrent independent teams/work streams for a product. Multiple workers cooperating as one team share one set; O is not a worker cap.
- Do not implement plan persistence in this phase. Keep assignment state in memory and provide Excel export after dispatch; use the corrected eight-column target result as the baseline export contract. Preserve `Số người làm` and `số thứ tự nhóm`; their exact grouping relation needs a separate rule, not automatic correction.
- Re-importing the same group and same date/week replaces only that exact scope; never append quantities.
- Keep product work in one group by default. When necessary, allow at most two groups for one product, preserve each group's source quantity and make cross-group use an exception.
- Missing jig metadata means skip that product's jig check with a warning, not drop the product.
- If weekly capacity is insufficient, keep excess work unassigned and warn.

These are owner-approved target requirements, **not evidence that current code already implements them**. Current `AutoDispatchService` remains a six-day in-memory algorithm and does not yet apply the complete dated calendar, overtime, leave or jig-resource constraints.
