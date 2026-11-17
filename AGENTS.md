# KeHoachSX — Instructions for AI Coding Agents

> This file is the mandatory entry point for ChatGPT, Gemini, GitHub Copilot, Codex, Claude Code and any other coding agent working in this repository.
> Language for discussion and documentation: Vietnamese. Keep existing C# identifiers and API names in English/Vietnamese as already used by the codebase.

## 1. Mission and authority

KeHoachSX is a production-planning and assembly-worker dispatching application. The current repository is the source of truth for implemented behavior. The documents under `docs/ai/` explain the observed architecture and business rules.

Priority when sources conflict:
1. Explicitly approved instruction from the project owner in the current task.
2. Actual code and tests on the target branch (describe behavior; do not silently change it).
3. `docs/ai/BUSINESS_RULES.md` and approved ADRs.
4. Existing `ARCHITECTURE.md`.
5. Historical notes and comments.
6. Agent assumptions (never treat as facts).

A discrepancy must be reported. Do not silently choose one source.

## 2. Mandatory workflow before changing code

1. Read this file and relevant files in `docs/ai/`.
2. Inspect the target branch, working tree, project references and all call sites of affected members.
3. Separate:
   - verified current implementation;
   - approved business requirement;
   - proposal;
   - unresolved question.
4. Explain the current flow and list files likely to change.
5. State risks, compatibility concerns and tests.
6. For behavior, algorithm, data contract, persistence, security, project-reference or database changes, propose a plan and wait for the owner's approval before implementation.
7. Keep changes within the approved scope. Do not opportunistically refactor unrelated code.

For a small, isolated typo or demonstrably behavior-preserving fix, implementation may proceed without a separate approval, but still inspect, test and summarize the diff.

## 3. Project-specific technical boundaries

Current solution projects:
- `KeHoachSX.Core`: current domain/data models.
- `KeHoachSX.Application`: use-case services and abstractions.
- `KeHoachSX.Infrastructure`: Dapper/SQL Server repositories, Oracle adapter, JSON/CSV/Excel readers and DI composition.
- `KeHoachSX.UI`: Blazor UI.
- `KeHoachSX.App`: Windows Forms + Blazor Hybrid desktop host.
- `KeHoachSX.Api`: API/module host integration point.
- `JtecShare.*`: external shared projects referenced by the solution; inspect their actual code when available. Do not assume they are part of this repository.

Intended dependency direction:
- Core has no dependency on application, infrastructure or UI.
- Application may depend on Core and owns application-facing abstractions.
- Infrastructure implements Application abstractions and may depend on Core.
- UI depends on Application/Core abstractions and must not access Dapper, SQL providers, Oracle providers or parse infrastructure files directly.
- App is the desktop composition root and may wire UI, Application, Infrastructure and JtecShare.
- Api is a host/composition point. Do not invent endpoints or claim the API is implemented without inspecting it.

Do not create a new layer, repository, manager, factory, mediator, generic framework or project unless a concrete requirement justifies it and the owner approves it.

## 4. Verified project conventions and business defaults

See:
- `docs/ai/ARCHITECTURE.md`
- `docs/ai/BUSINESS_RULES.md`
- `docs/ai/DATA_SOURCES.md`
- `docs/ai/KNOWN_GAPS.md`

Important currently observed behavior:
- Eligible assembly groups are LR1, LR2, LR3 and LR4.
- Employee status must be `1`; CHIEF, LEADE and LEADER are excluded.
- Missing/non-positive employee efficiency is treated as 20% by the current employee-loading/dispatch logic.
- Standard-time lookup priority is uploaded plan value, then Oracle, then SQL Server, then 60 minutes.
- Current shift defaults: 07:30–17:00, six days (Monday–Saturday); breaks 10:00–10:10, 12:00–13:00 and 15:00–15:10.
- Current parser expands quantity into unit LotTask records.
- Current parser sets initial planned work duration to standardMinutes / 0.3; dispatch recalculates it using worker efficiency.
- Current auto-dispatch candidate ranking combines three weighted components: skill score × 0.50, remaining-weekly-capacity score × 0.35, and same-product continuity bonus × 0.15. The continuity bonus value is currently 0.35 when the worker's last assigned product matches; its maximum contribution to the total score is therefore 0.35 × 0.15 = 0.0525. This is a ranking preference, not a hard assignment rule. See `docs/ai/BUSINESS_RULES.md`.
These are observations of the current code, not a claim that all desired business rules are implemented.

## 5. Non-negotiable engineering rules

### Data and infrastructure
- Use parameterized SQL. Never concatenate user input into SQL.
- Do not expose connection strings, passwords, tokens, employee personal data or production exports in logs, prompts, commits or documentation.
- Never connect to or modify production databases during analysis unless the owner explicitly authorizes a narrowly defined, read-only inspection. Prefer sanitized test data and schema-only exports.
- **Oracle is strictly read-only for this project.** AI agents must never design, generate, recommend as an implementation, execute, or add code that inserts, updates, deletes, merges, truncates, creates, alters, drops, grants, revokes, or otherwise changes Oracle data/schema/security. This prohibition applies even if a task prompt asks for it. If an Oracle write/change is requested, explain that it is outside the AI implementation scope and refer the owner to the authorized DBA/manual process.
- Oracle access from application code may only retrieve data using reviewed read-only SELECT operations through the existing adapter/service boundary. Do not use Dapper/ADO.NET write APIs, command execution for mutations, PL/SQL blocks, stored procedures/packages with possible side effects, SELECT ... FOR UPDATE, sequence NEXTVAL, or unreviewed functions with side effects. Do not create a new Oracle connection/query path when an existing shared read service is available.
- Require an Oracle database account whose grants enforce SELECT-only access to the approved objects. Application-level instructions are not a security boundary; the read-only account/permissions must prevent writes even if generated code is wrong.
- The existing Oracle adapter delegates to external JtecShare service ILayGioCongHeThong. Its implementation is outside this repository; do not assume its internals are read-only until inspected. Keep using the existing adapter, and report that external implementation/DB grants need verification.
- Oracle integration is optional through the existing adapter boundary; do not make it mandatory without approval.
- Preserve the two existing fallback policies unless the owner explicitly approves a behavior change:
  - Employee list: query SQL Server first; use the local JSON employee source if the SQL call fails or returns no rows.
  - Standard time: after a positive value from the uploaded plan is absent, try Oracle first; if Oracle is unavailable, fails, or returns no positive value, try SQL Server; if neither returns a positive value, use 60 minutes. The service's in-memory cache is checked before these lookups.
- Preserve the two CSV skill sources' distinct semantics: the detailed employee-to-product CSV is authoritative for detailed skill counts; the product-to-worker CSV supplements membership and must not fabricate detailed counts.
- Treat CSV/Excel input as untrusted. Validate columns, values, row counts and malformed data; report rejected rows rather than silently hiding important errors.
- Do not create, delete, rename or migrate tables without an approved schema change proposal and rollback plan.

### Business logic
- Never invent the meaning of fields or business rules.
- Do not infer that one worker may be assigned multiple jigs, or that a product's jig count is known, unless the source provides it.
- Do not claim continuity across days, leave substitution, jig constraints or high-efficiency pairing are enforced until code and tests prove it.
- Do not change lot expansion, standard-time units, efficiency formulas, worker eligibility or scheduling priority without explicit approval.
- Preserve a clear distinction between product quantity, Lot count, standard minutes and adjusted work minutes.
- An unscheduled task must remain visible and be reported; never drop work silently.
- A task must not be assigned twice. Any future parallel assignment algorithm must be deterministic and race-safe.

### C# and architecture
- Follow the target framework and nullable/implicit-using conventions of the affected project. Do not upgrade target frameworks or package versions as incidental cleanup.
- Keep UI event handlers focused on presentation/orchestration; place scheduling and data rules in Application.
- Keep SQL, Oracle access and file parsing in Infrastructure.
- Reuse existing contracts/interfaces and DI registrations.
- Use asynchronous APIs for I/O and propagate CancellationToken where existing contracts support it.
- Do not run concurrent operations on the same DbContext/connection unless the implementation explicitly supports it.
- Dispose streams, connections and other disposable resources.
- Use explicit, meaningful names; avoid magic numbers. A behavior-changing constant must be documented and tested.
- Do not introduce package dependencies without explaining why, alternatives, version compatibility and approval.

## 6. UI rules

- Preserve existing Blazor UI behavior and CSS isolation unless the task explicitly changes it.
- Verify the desktop host's `wwwroot/index.html`, static assets and linked content when changing UI resources.
- Do not move static assets between UI and App without checking how BlazorWebView resolves them.
- Avoid unnecessary rerenders and expensive recomputation in Razor components.
- Keep manual assignment behavior separate from automatic dispatch behavior and test both.
- Do not change labels, layout or styling outside the requested area.

## 7. Security-sensitive host behavior

- Treat certificate-validation bypasses, insecure browser flags and remote image host exceptions as security-sensitive. Never broaden them as a workaround.
- Do not log secrets or dump full employee records.
- Do not add permissive CORS, authentication bypasses or API endpoints without approval.
- Any API host work must define authentication, authorization, validation, error contracts and rate/abuse boundaries before production exposure.

## 8. Required validation

Run relevant checks from repository root when the environment supports them:
```powershell
dotnet restore .\KeHoachSX.sln
dotnet build .\KeHoachSX.sln -c Debug
dotnet build .\KeHoachSX.App\KeHoachSX.App.csproj -c Release
dotnet test .\KeHoachSX.sln
```

Important:
- Desktop target is currently `net8.0-windows`; build it on Windows with the required .NET SDK, WebView2 prerequisites and the external JtecShare project paths.
- The solution references JtecShare projects outside this repository. If they are absent, state that full solution verification is blocked; do not report a successful build.
- If there is no test project or tests are not available, say so and propose focused tests. Do not invent test results.
- For scheduling changes, add deterministic tests for day capacity, breaks, efficiency, skill ranking, load balancing, product continuity, lot ordering and unassigned work.
- For parser changes, test Excel and CSV, supported delimiters, headers, missing fields, invalid quantities, decimal formats, duplicate product rows and large inputs.
- For performance changes, capture a baseline and compare measurements; do not claim faster code without evidence.

## 9. Definition of done

A task is complete only when:
- actual behavior matches the approved acceptance criteria;
- no unrelated files were changed;
- build/test outcomes are reported truthfully;
- relevant documentation/ADR is updated;
- security and data risks are addressed;
- remaining limitations and manual verification steps are stated;
- the final response lists changed files and the exact validation commands/results.

## 10. Required response format

Every coding task report must include:
1. **Hiện trạng đã kiểm tra** — concrete files/classes/methods.
2. **Phương án** — minimal implementation and alternatives if material.
3. **Phạm vi thay đổi** — exact files and layers.
4. **Quy tắc nghiệp vụ bị ảnh hưởng** — explicit list.
5. **Kiểm thử** — commands, results, and what was not run.
6. **Rủi ro/cần xác nhận** — unresolved items.
7. **Kết luận** — done / partial / blocked.

Never say “optimized”, “fixed”, “secure” or “fully tested” without evidence.


## 11. Mandatory rules for dated/group scheduling

Before changing any calendar, import, dispatch, overtime, leave, holiday or jig logic, read `docs/ai/SCHEDULING_CALENDAR_AND_CONSTRAINTS.md`.

The owner-defined target behavior is:
- Default application scope is the local current date (Today); the user can choose Tomorrow or Whole week.
- Production week is seven consecutive dates, Friday through the following Thursday. Never use the old Monday–Saturday six-day model for the new production calendar.
- A Whole week import is one weekly demand-pool file for the explicitly selected/identified group; distribute its product/Lot work across all seven dates using employee/date capacity. Do not require date assignment in each weekly row.
- Plan data is partitioned by group and date/week. Re-importing the same group and exact date/week replaces that scope; it does not append. Other groups/dates remain untouched.
- Read the production group from the specified import-file column and validate it. The supplied LR4 workbook has LR4-LR13 values in its second column; do not map these labels to LR4 without owner confirmation.
- Eligible employees normally come from the plan's group. Keep product work in one group by default; permit a second group only when necessary, preserve each group's source quantity, and use no more than two groups per product unless explicitly approved.
- Worker availability is evaluated per employee per date, not as one uniform weekly capacity.
- On normal dates, use overtime records without a new status/approval filter in the first phase; overtime starts at 17:00 and adds SOGIO hours to normal capacity.
- On a company holiday, only employees with a matching overtime record may work; overtime starts at 08:00 and SOGIO hours are the entire available capacity (no normal shift). Do not filter STATUS/QUANLYDUYET/GADUYET in the initial phase.
- `Overtime.ID` always matches `NHANVIEN.UserName`. Deduplicate by ID + date and never sum duplicates; flag conflicting SOGIO values until a tie-breaker is approved.
- Each `TA.dbo.nghile.date` is one non-recurring holiday date.
- A leave row from `STOCKMANAGEMENT.dbo.thutuchanhchinh` with `TRANGTHAI <> 7` excludes the matching employee for the whole covered date. Ignore partial-day fields in the first phase; SQL NULL does not satisfy the inequality.
- Jig column O counts physical sets and limits simultaneously active independent teams/work streams. Multiple employees cooperating in one team share one jig set; O does not cap team size. If jig metadata is missing, skip that product's jig check but show a warning; do not drop the product.
- For T: L=T, N in {2,3}, C>50 permits one or two workers; the scheduler chooses based on capacity/fit without inventing another threshold.
- For J: L=J and N>=2 enables the rule; N is not a headcount cap. C>=400 => 4, C>=300 => 3, C>=200 => 2; C<200 has no defined J-specific rule.
- Do not add plan persistence in the first phase. Export the completed in-memory assignment result to Excel, using the supplied target result columns as the starting format.
- If seven-day capacity is insufficient, leave remaining work unassigned and show a warning; never force assignments beyond capacity silently.
- Ranking scores (skill, workload, continuity) may rank only workers who already satisfy hard constraints. They must never override group eligibility, leave, holiday access, hours or jig capacity.

Current repository code does not yet implement the complete dated calendar, weekly-pool distribution, group-specific import, holiday/overtime, leave or jig-resource model, nor the new Excel export. Do not describe these target rules as implemented until code and tests prove them. Remaining decisions are listed in `docs/ai/KNOWN_GAPS.md`; stop and ask the owner before making behavior-changing assumptions.
