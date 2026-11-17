# Claude Code instructions

Read `AGENTS.md` first. It is the canonical project instruction file.

Before any implementation, also read the relevant documents under `docs/ai/`, especially:
- `PROJECT_CONTEXT.md`
- `ARCHITECTURE.md`
- `BUSINESS_RULES.md`
- `DATA_SOURCES.md`
- `KNOWN_GAPS.md`

Do not duplicate or override rules from AGENTS.md here. If any instruction conflicts, report the conflict and follow the precedence defined in AGENTS.md.

Use Vietnamese for analysis summaries and user-facing reports. Preserve source identifiers.

For independent review, stay read-only: inspect the actual diff and relevant call sites; do not modify files. For implementation, change only the approved scope and run the validation commands required by AGENTS.md.
