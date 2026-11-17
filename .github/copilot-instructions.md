# GitHub Copilot instructions for KeHoachSX

The canonical rules are in the repository-root `AGENTS.md`. Read it before making changes.

Before touching a business flow, read relevant `docs/ai/` documents:
- PROJECT_CONTEXT.md
- ARCHITECTURE.md
- BUSINESS_RULES.md
- DATA_SOURCES.md
- KNOWN_GAPS.md

Key constraints:
- Keep the existing project/layer boundaries.
- UI must not contain SQL/Oracle access or CSV/Excel parsing.
- Application owns use cases and abstractions; Infrastructure implements data access.
- Preserve verified behavior unless the task explicitly approves a behavior change.
- Do not invent jig/leave/continuity rules as already implemented.
- No production database operations, schema changes, package additions or framework upgrades without approval.
- Inspect call sites and Git diff; avoid unrelated refactoring.
- Build/test affected projects and report commands/results honestly.
- Respond in Vietnamese unless asked otherwise.

See AGENTS.md for the complete source-of-truth precedence, safety, test and definition-of-done requirements.
