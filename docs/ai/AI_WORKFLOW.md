# AI Workflow — ChatGPT, Gemini and GitHub

## 1. Recommended responsibility split

| Tool | Primary role in this project | Do not delegate blindly |
|---|---|---|
| ChatGPT | Requirements analysis, business-rule clarification, architecture options, test-case design, code review discussion, documentation | Do not assume it has the current repository unless files/repo access are actually provided |
| Gemini | Independent second opinion, long-context review of supplied source/docs, alternative algorithm analysis, research against official docs | Do not let it independently redefine approved business rules |
| GitHub | Source of truth for code, branches, commits, Issues, PRs, review history and Actions | Do not commit AI-generated changes directly to main without checks |
| GitHub Copilot (optional) | Inline completion and Visual Studio/VS Code agent implementation | Use only after reading AGENTS.md and approved task |
| OpenAI Codex (recommended addition) | Repository-level coding agent: inspect files, implement bounded tasks, run build/tests, inspect diffs | One writer per working tree; use branches/worktrees for parallel work |
| Claude Code (optional reviewer) | Independent read-only code review and multi-file investigation | Do not let reviewer write to the same files while another agent implements |

The minimum useful set is ChatGPT + Gemini + GitHub. If adding one coding tool, add GitHub Copilot for IDE assistance or Codex for repository-wide execution. Do not buy multiple overlapping tools before measuring the bottleneck.

## 2. Single source of truth

- GitHub branch/commit is the source of truth for code.
- `AGENTS.md` is the canonical agent policy.
- `docs/ai/*` is the shared project knowledge base.
- An approved issue/task brief defines the scope of one change.
- A PR contains the proposed change and evidence.
- A merged commit is the accepted implementation.

Never maintain separate conflicting prompts for each AI. Provider-specific files should point back to AGENTS.md.

## 3. Standard task lifecycle

### Step A — Owner defines outcome
Provide the user-visible problem, expected result, constraints, examples and acceptance criteria.

### Step B — ChatGPT requirement analyst (read-only)
Ask ChatGPT to:
- restate the business problem;
- identify ambiguity and edge cases;
- distinguish existing code behavior from target behavior;
- produce acceptance criteria and test scenarios;
- identify what owner must decide.

No code changes in this step.

### Step C — Codex/Copilot implementation plan (read-only)
On the target branch, inspect AGENTS.md/docs and trace actual flow/call sites.
Return exact files/classes/methods, current behavior and minimal plan.
Do not modify files until owner approves behavior/architecture/data-impacting plan.

### Step D — Gemini independent challenge (read-only)
Give Gemini:
- approved business requirement;
- investigation report;
- proposed plan and relevant sanitized code.
Ask it to find missed cases, contradictions, data-loss risks and test gaps. Require concrete file/method evidence. Do not ask it to invent a new business rule.

### Step E — One implementation agent
Choose one agent to edit a given working tree:
- Copilot in IDE for a small bounded feature;
- Codex for a multi-file repository task.
Implement only approved scope. No other agent writes to the same files concurrently.

### Step F — Automated validation
Run restore/build/test and task-specific tests. Add tests for new business behavior. Record failures and environment blockers.

### Step G — Independent review
Use a separate ChatGPT/Gemini/Claude review session in read-only mode. Review actual Git diff, not only implementation summary. Review correctness, regression, resource handling, security, architecture and tests.

### Step H — Owner approval and GitHub PR
Push a feature branch, open PR, review checks, merge only after owner acceptance. Update docs/ADR after behavior changes.

## 4. Parallel agent policy

Parallelize only independent read-only investigations, such as:
- one agent inspects import/parser;
- one inspects employee/skill data;
- one inspects dispatch algorithm/tests.

Do not run two writers against the same working tree/files. Use separate Git worktrees/branches and integrate sequentially. For algorithm or model changes, one agent owns the implementation and another reviewer remains read-only.

## 5. Safe access

- Give agents access only to the repository and test resources needed.
- Do not expose production credentials, employee data exports or internal network details in prompts.
- Use sanitized fixtures.
- DB-connected MCP tools should be read-only and point to test/staging only.
- Require human approval for file deletes, migrations, data writes, package upgrades, security changes and production deployments.

## 6. Definition of done

Use the checklist in AGENTS.md. A task is not done merely because an agent says it is done. Verify diff, build, tests and runtime behavior.


## 7. Workflow for the new dated scheduling feature

For work involving calendar scopes, group imports, overtime, holidays, leave or jigs:
1. ChatGPT converts the owner's explanation into acceptance criteria and a list of unresolved decisions. It must keep target requirements separate from current code.
2. Repository agent inspects `SCHEDULING_CALENDAR_AND_CONSTRAINTS.md`, current `KeHoachSX_View.razor`, `ShiftConfig`, `LotTask`, `AssemblyWorker` and `AutoDispatchService`.
3. Gemini performs an independent read-only challenge: check date boundary cases, SQL filtering, employee/date key matching, overtime approval, task timeline arithmetic and jig resource conflicts. It must cite concrete source fields and not invent approval codes.
4. One implementation agent produces a design first. The owner approves any new data contract, persistence model, database query, import layout and hard constraint.
5. Implement one vertical slice at a time (calendar/date model -> group import -> leave/holiday/overtime availability -> jig constraints), adding regression tests at each step.
6. A separate agent reviews the actual diff read-only. Validate the dated schedule with realistic anonymized fixtures for all seven dates and conflicting constraints.
