# Agent Instructions

## Session Startup

- Read `README.md` and `docs/design/kingdom-watch-plan-v7.1.md` (or its latest version) before making changes — the design doc is the source of truth for what this game is and why.
- GitHub Issues and Milestones track the backlog (see `/milestones` — M0 through M8, matching the design doc's §19).
- Keep `README.md` and this file up to date when repository-wide decisions are made.

## Repository Layout

```
Game/              Unity project (6.6 -> 6.7 LTS). The only Unity-dependent piece.
Core/              (not yet scaffolded) netstandard2.1, zero Unity dependencies
Core.Tests/        (not yet scaffolded) net10.0, references Core
Harness/           (not yet scaffolded) net10.0, references Core
docs/design/       full design and technical plan
```

See `docs/design/kingdom-watch-plan-v7.1.md` §5 for the architectural reasoning behind the split — Core must stay buildable and testable with zero Unity dependency, which is central to the determinism/debugging strategy.

## Working with AI Agents

### Commits and pushes

Do not commit or push unless explicitly asked (e.g. "commit this", "push it"). The exception is when asked to create a PR — that implies doing everything needed: branch, commits, push, and PR creation.

### Self-review

After writing code, run through the review checklist in `.claude/skills/self-review/SKILL.md` (`/self-review`). Fix all issues found before presenting. The review loop is internal — do not surface bugs as a list of things found.

### PR feedback review

When asked to pull PR feedback, use `.claude/skills/pr-feedback/SKILL.md` (`/pr-feedback`).

### Filing issues

Prefer `.claude/skills/create-issue/SKILL.md` (`/create-issue`) over calling `gh issue create` directly — it verifies claims against the code and interrogates for what's actually known before writing anything down.

### Starting work on an issue

Prefer `.claude/skills/kickoff/SKILL.md` (`/kickoff`) over jumping straight into implementation, even when an issue looks obvious — it verifies the issue is still live, analyzes solutions independently of whatever it proposes, and agrees a plan before any code exists.

### Presenting options

When presenting 2+ approaches to the user, list them clearly with tradeoffs for each. Do not just pick one and proceed without asking. If your client supports a popup/question UI (e.g. `AskUserQuestion`), use it instead of listing options in plain text.

### Output style

- **No AI attribution or agent branding anywhere in generated project artifacts.** This includes branch names, commit messages, PR titles, PR descriptions, issue bodies, comments, and release notes. Avoid labels such as "Claude", "Codex", "Copilot", "AI-generated", or similar unless the user explicitly asks for them.
- Be terse. Skip pleasantries and preamble.

## Local Tools

The `tools/` directory (git-ignored except for the scripts themselves) contains local development scripts:

```powershell
# Pull all open PR comments into a structured Markdown report
pwsh tools/Get-OpenPrComments.ps1
# Output: tools/pr-comments.md
```

Requires `gh` CLI authenticated. See `.claude/skills/pr-feedback/SKILL.md` (`/pr-feedback`) for the full evaluation workflow.

Project slash commands (`/create-issue`, `/kickoff`, `/pr-feedback`, `/self-review`) live in `.claude/skills/`.
