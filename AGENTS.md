# Agent Instructions

## Session Startup

- Read `README.md` and `docs/design/kingdom-watch-plan-v7.1.md` (or its latest version) before making changes — the design doc explains what this game is and why.
- **Treat the design doc the way `/kickoff` treats an issue:** a well-informed hypothesis from someone who had context you may lack — worth taking seriously, not worth adopting unexamined. It is a loose, living plan rather than a specification, and it is expected to be iterated on as real code teaches us things. Verify its claims against the code that actually exists; where the two disagree, work out which one is wrong instead of assuming it is the code. When a decision supersedes something the doc says, update the doc in the same change.
- GitHub Issues and Milestones track the backlog (see `/milestones` — M0 through M8, matching the design doc's §19).
- Keep `README.md` and this file up to date when repository-wide decisions are made.

## Repository Layout

```
Game/              Unity project (6.6 -> 6.7 LTS). The only Unity-dependent piece.
Core/              netstandard2.1, zero Unity dependencies, single-target
Core.Tests/        net10.0, NUnit, references Core
Harness/           net10.0 console, references Core
docs/design/       full design and technical plan
```

See `docs/design/kingdom-watch-plan-v7.1.md` §5 for the architectural reasoning behind the split — Core must stay buildable and testable with zero Unity dependency, which is central to the determinism/debugging strategy.

### Building

```powershell
dotnet build KingdomWatch.sln
dotnet test KingdomWatch.sln
dotnet run --project Harness
```

The SDK is pinned in `global.json`. Shared compiler settings live in the root `Directory.Build.props`; `Game/Directory.Build.props` is intentionally empty and stops those settings reaching the `.csproj` files Unity regenerates on import — don't delete it.

Three constraints on `Core/` are enforced by tests in `Core.Tests/CoreAssemblyContractTests.cs` rather than by convention: it targets `netstandard2.1`, it is **single-targeted**, and it references nothing but the `netstandard` facade. Adding a package reference or a Unity type to Core will fail the build, by design.

When adding randomness, give each decision type its own `RandomDomain` value rather than reusing a broad one. Two draws can only collide when they share a domain, and a collision is silent — it does not crash, fail a test, or disturb the cross-platform determinism hash. It surfaces much later as two things that should be independent moving in lockstep, which is close to undebuggable from the outside. See the remarks on `RandomDomain`.

Test coverage, when you want to see what is untested:

```powershell
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName!~CoreAssemblyContractTests"
```

The filter is load-bearing. `coverlet` instruments `KingdomWatch.Core.dll` to insert hit tracking, and the rewritten assembly picks up `System.Runtime` and `System.Threading` references — so the zero-dependency contract test fails under coverage, correctly, because the instrumented assembly is not the one that ships. CI runs the full suite uninstrumented, so nothing is skipped there. Output lands in `Core.Tests/TestResults/` (git-ignored).

There is no coverage target and no gate. Coverage is a tool for finding untested branches, not a number to hit.

## Engineering principles

**Write the cleanest, most understandable, maintainable, and testable version by default.** Reach for a more complicated or lower-level implementation only when there is a *specific, identified* need — a measured performance problem, a platform constraint — never a suspected future one.

Concretely: two explicit fields beat bit-packing into one; a plain class beats a hand-rolled store; a straightforward algorithm beats a clever one. When a real constraint does force something awkward, say in a comment what the constraint was, so the next person can tell whether it still applies.

Determinism (design doc §5) is the standing exception. It is a correctness requirement rather than an optimization, so it outranks convenience — integer-only arithmetic in branching code is not premature cleverness, it is the rule.

**When fixing a bug or closing a hole, write the failing test first.** Watch it go red, then make it green. A test written after the fix proves only that the code does what it does — it can pass for the wrong reason, or assert something the bug never violated. A test that has actually been seen to fail is the only kind that will catch the bug coming back.

If the fix gets written first anyway, the cheap recovery is to revert it, confirm the test goes red, and restore. That is worth the two minutes.

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
