# Agent Instructions

## Session Startup

- Read `README.md` and `docs/design/kingdom-watch-plan-v7.1.md` (or its latest version) before making changes — the design doc explains what this game is and why.
- **Treat the design doc the way `/kickoff` treats an issue:** a well-informed hypothesis from someone who had context you may lack — worth taking seriously, not worth adopting unexamined. It is a loose, living plan rather than a specification, and it is expected to be iterated on as real code teaches us things. Verify its claims against the code that actually exists; where the two disagree, work out which one is wrong instead of assuming it is the code. When a decision supersedes something the doc says, update the doc in the same change.
- GitHub Issues and Milestones track the backlog (see `/milestones` — M0 through M8, matching the design doc's §19). The roadmap at https://zhollis21.github.io/KingdomWatch/ is the generated view of it: what is ready to pick up, the current milestone and the one after it (`#next.md`), one chart per milestone, and `graph.json` for tools. `graph.json` carries a `generated` stamp saying when the workflow last built it; `/kickoff` reads the published copy and only regenerates when that is not fresh enough. Locally, `pwsh tools/Build-Roadmap.ps1` writes the same files to `docs/roadmap/` (git-ignored) straight from GitHub — always current, and what to run after editing a relationship, since those fire no webhook. Do not hand-edit any of it.
- Keep `README.md` and this file up to date when repository-wide decisions are made.

## Issue dependencies

One kind of edge between issues, and the roadmap reads nothing else: **blocked by**, GitHub's native relationship (the *Relationships* box in the issue sidebar, or `gh api repos/zhollis21/KingdomWatch/issues/<N>/dependencies/blocked_by -F issue_id=<database id>` — `-F`, not `-f`, which sends the id as a string and is refused with a 422; and the database id, which is not the issue number). Record the *direct* dependency only — if #17 needs #52 and #52 needs #12, do not also link #17 to #12; readiness is computed transitively. An open issue is *ready* when everything it is blocked by is closed, and the `blocked` label is derived from that by the roadmap workflow, so never set it by hand.

Prose such as "Depends on #4" or "Related to #35" is fine for a reader but invisible to the roadmap, because the same bodies say "open question #7" about the design doc's list. `/create-issue` and `/kickoff` wire the relationships when they file or split issues. Relationship edits do not trigger the workflow; after re-wiring, run `gh api -X POST repos/zhollis21/KingdomWatch/actions/workflows/roadmap.yml/dispatches -f ref=main` or wait for the nightly run (`gh workflow run` itself resolves the default branch over GraphQL, so it is refused — see below).

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

When adding randomness, give each decision type its own `RandomDomain` value rather than reusing a broad one, and each call site its own `RandomSite`. Both are required — `rng.Key(domain, site)` is the only way to start a key, so a draw that declares no site will not compile. A collision is silent: it does not crash, fail a test, or disturb the cross-platform determinism hash. It surfaces much later as two things that should be independent moving in lockstep, which is close to undebuggable from the outside. Neither enum may be renumbered or reordered — doing so rewrites every roll derived from it. See the remarks on `RandomDomain` and `RandomSite`.

**Any new `IRandomDrawObserver` ships with a watched-versus-unwatched equivalence test.** An observer runs inside the draw itself and is non-null only in the harness and the tests, so anything it *does* — scheduling, writing to a store — happens on one side of the comparison the determinism strategy rests on and not the other. Both sides stay perfectly reproducible and quietly disagree, and the disagreement reads as an IL2CPP divergence rather than as diagnostic code. The contract on `IRandomDrawObserver` forbids it; nothing enforces it, so the test is the enforcement. `Core.Tests/Rng/RandomSiteTests.cs` is the template: run the same seed twice, once watched, and assert the worlds end up identical — with `An_observer_that_writes_to_the_world_is_caught_by_that_comparison` proving the comparison can actually fail. It compares on the canonical world hash (below), which is wider than the field-by-field digest it used to build by hand.

The same "give it its own value" rule applies to `ScheduledEventKind` and `DomainEventKind`, for the same reason: a kind reused across two unrelated occurrences makes them indistinguishable to the scheduler, to subscribers, to history and to the validator. `ReasonCode` is different in kind — a code is shared vocabulary for a contributing factor, and `FoodShortage` is *supposed* to recur across every decision it contributed to; only a repeat within one decision is rejected. What `ReasonCode` shares with the others is append-only numbering, and one rule of its own: it was seeded with §5's examples, and beyond those a code is added from the decision site that emits it rather than ahead of time, and never inferred after the fact from an "explain" pass — that drifts from the real calculation and lies to the player (§5).

**Never renumber or reorder an enum whose values are persisted or ordering-significant** — `EntityKind`, `RandomDomain`, `SimulationPhase`, `ScheduledEventKind`, `ResourceKind`, `DomainEventKind`, `ReasonCode`, `MemoryTier`, `AgeStage`, `Sex`, `TerrainKind`, `Transport`, `JobKind`. Append instead. Renumbering silently repoints every existing world's saved references, or reorders every event it ever dispatched, which rewrites its history. `ScheduledEventKind` is the sharpest case, because its numeric value *is* the priority that orders two events sharing an instant, phase and primary entity.

**A new system is folded into the world hash and reachable by the validator.** `Core/Validation/WorldHash.cs` is §5's canonical world-state hash, and `Harness/WorldValidator.cs` is its invariant checker; the seed sweep in `Core.Tests/Validation/` drives both. State the hash does not fold in is state whose cross-platform divergence nothing will report, and the failure is silent — the hash keeps returning a number, and the number keeps matching. So when a system gains durable state, give it a section in the hash (tagged and counted, sorted by durable id, never by container order) and whatever read-only surface the validator needs to walk it.

A periodic stream carries one more obligation. The "state names the event it booked" rule (#80) is only worth having if something checks it, so every stream hands its record out through `CopyBookingsTo(List<PendingBooking>)`, and the validator confirms each id is still in the queue while the hash folds it in. A booking the queue has forgotten is a stream that has silently stopped — the owner waits for a wake-up that never comes, and nothing else says so. The three on `PersonRecord` (`PregnancyDue`, `PendingMortalityCheck`, `PendingAgeStage`) are checked through the record instead, since people are enumerable and communities are not.

That surface follows one shape: `CopyXTo(List<T> into)`, which clears and fills a caller-supplied list — `SimulationClock.CopyPendingTo`, `Jobs.CopyTrackedTo`, `Jobs.CopyTaskWorkersTo`, `Hunger.CopyBookingsTo`. A method rather than an `IEnumerable` property on purpose: a lazy view held across an `AdvanceTo` reads a half-dispatched world, and a check taken once per simulated day over a long run should not allocate a fresh collection each time. Hand out durable ids wherever the caller could otherwise keep a `PersonHandle` past the point it resolves.

Two exclusions from the hash are load-bearing: storage handles (a slot index and a generation are representation, which is exactly what §5 says a canonical hash must not depend on) and any container's own iteration order. Nothing in `Core` uses `float`, `double` or `decimal` today, which is the other reason the hash is tractable at all — a floating-point field in simulation state is a determinism decision before it is a hashing one.

**The tick loop is allocation-checked and timed** (`Core.Tests/Performance/`). Section 18 commits to zero allocations in the tick loop, and `SchedulerSoakTests` holds the scheduler to exactly that: warm up, then `Allocations.Measure(...)` around a further span of `AdvanceTo` calls, asserting zero bytes. When you add a system that runs under `AdvanceTo`, give it the same test — a `foreach` over an interface, a captured closure, or a `params` call in a per-entity path is invisible in review and costs nothing until it is GC pauses on a phone. The same fixture bounds wall time loosely; the bound exists to catch an order-of-magnitude regression, not to police percentages, so if it ever flakes, loosen it once and say so rather than chase it. `dotnet run --project Harness -c Release` prints the current throughput (sim-years/s, events/s, peak pending); until #17 provides a world, the workload is `Harness/SchedulerSoak.cs`, which exercises the scheduler alone.

Test coverage, when you want to see what is untested:

```powershell
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName!~CoreAssemblyContractTests"
```

The filter is load-bearing. `coverlet` instruments `KingdomWatch.Core.dll` to insert hit tracking, and the rewritten assembly picks up `System.Runtime` and `System.Threading` references — so the zero-dependency contract test fails under coverage, correctly, because the instrumented assembly is not the one that ships. CI runs the full suite uninstrumented, so nothing is skipped there. Output lands in `Core.Tests/TestResults/` (git-ignored).

There is no coverage target and no gate. Coverage is a tool for finding untested branches, not a number to hit.

**Full coverage is not the same as tested.** Coverage measures which lines *ran*, not which inputs were *considered*, so a constructor can be at 100% line and branch coverage and still accept values nobody thought about. Three rounds of review on #56 found exactly that: every finding was an invariant that was documented but not enforced, in code that coverage reported as fully covered.

So write tests against the input space, not the line count. For each public entry point, ask what a caller can actually pass rather than what the docs say they should:

- **Enums are the classic trap.** An enum parameter looks like the type system pins it to the declared members, but an enum is an int with names and `(EntityKind)999` casts in silently. Any public API taking an enum must reject undefined values — see `EnumGuard`.
- **Sentinels and boundaries.** Zero, negative, `MaxValue`, the empty collection, the default struct — and any state where two "is this empty/none/valid?" predicates could disagree with each other.
- **Values that bypass the guards.** A collection handed out through a read-only interface can still be downcast and mutated unless it is genuinely read-only.

A null-forgiving `!` is allowed in `Core.Tests` for the single purpose of reaching an `ArgumentNullException` guard — `new SimulationClock(null!)` — and nowhere else. Nullable reference types make the call a compile error otherwise, so without it the guard ships untested. This does not extend to `Core`, `Harness`, or to silencing a nullable warning in test setup.

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

- Be terse. Skip pleasantries and preamble.

## Local Tools

The `tools/` directory (git-ignored except for the scripts themselves) contains local development scripts:

```powershell
# Pull all open PR comments into a structured Markdown report
pwsh tools/Get-OpenPrComments.ps1
# Output: tools/pr-comments.md

# Regenerate the roadmap into docs/roadmap/ (git-ignored; read-only against GitHub — the workflow adds -SyncLabels)
pwsh tools/Build-Roadmap.ps1
```

```powershell
# Every page of a REST collection, as one JSON array — for the skills, which
# cannot page in a shell loop (see below)
pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues?state=all'
```

`tools/GitHubApi.psm1` is not a script but the shared GitHub REST helpers the
above import; see [below](#calling-the-github-api-from-a-claude-code-cloud-session) for why they exist.

Requires `gh` CLI authenticated. See `.claude/skills/pr-feedback/SKILL.md` (`/pr-feedback`) for the full evaluation workflow.

### Calling the GitHub API from a Claude Code cloud session

Cloud sessions reach GitHub through a proxy that holds the real credentials
outside the container. Its restrictions change how tooling here has to be
written, and none of them is configurable — they apply regardless of the
credentials supplied, and independently of the environment's network access
level, so setting `GH_TOKEN` to a personal token does not lift any of them.

**The short version: `gh api repos/{owner}/{repo}/...` is the only GitHub
surface that works.** Every `gh issue`, `gh pr` and `gh label` subcommand is
GraphQL-backed and returns `403` — including plain `gh issue view` and the
write commands `gh issue comment`, `gh issue edit`, `gh pr comment` and
`gh pr edit`. `gh workflow list` is REST-backed and works, but `gh workflow run` resolves the
default branch over GraphQL and is refused; dispatch a workflow with
`gh api -X POST repos/{owner}/{repo}/actions/workflows/{file}/dispatches -f ref=main`.

**GraphQL is refused.** Only a pinned set of pull-request operations is served;
anything else on `/graphql` comes back `HTTP 403`. That takes with it
`gh api graphql`, the `gh` subcommands above, and anything GraphQL-only such as
Projects v2. Use REST. Review threads have no REST equivalent on
github.com, so the proxy adds its own routes — `GET  .../pulls/{n}/ccr/review_threads`
and `POST .../pulls/{n}/ccr/comments/{comment_id}/resolve` (also `/unresolve`,
`/auto_merge`, `/ready_for_review`, `/convert_to_draft`). Those are proxy-only
and key off a comment id rather than GraphQL's thread node id, so anything
built on them does not run off a normal machine — say so in a comment where
they are used.

**`gh --paginate` breaks past the first page.** It follows GitHub's
`Link: rel="next"`, which points at the numeric-ID form
(`/repositories/{id}/issues?...`), and the proxy rejects that form too. It
fails loudly (non-zero exit) rather than truncating silently, but it fails.
Walk pages by hand instead — `&per_page=100&page=N` until a short page arrives.

**Cross-repository endpoints are refused as well.** `search/issues` and friends
come back `403` with "sessions are bound to their configured repositories". Use
a repo-scoped endpoint and filter client-side.

The pagination restriction is the one that bites late: a collection that still
fits one page works, so tooling looks healthy right up until it does not.
`tools/GitHubApi.psm1` holds the two helpers (`Invoke-GhJson`, `Get-Paged`) so
the workaround has exactly one copy — import it rather than hand-rolling a
second. `Get-Paged` refuses a `PageSize` above 100, because GitHub silently
serves 100 for anything larger and the next short page would read as the end of
the collection.

**A skill cannot page in a shell loop.** The command-prefix allow rules those
skills are granted match the first word, so a `for … do … gh api … done` starts
with `for` and gets bounced to the interactive classifier — the same trap as
putting `-X POST` before the endpoint. `tools/Get-GhPages.ps1` exists for that:
it prints a whole collection as one JSON array, the command starts with `pwsh`,
and it throws rather than truncating when a collection outgrows its page cap.

`tools/` and `.claude/skills/` are clear of all of the above; keep them that
way. A `gh issue`/`gh pr` one-liner from memory is the likely way it creeps
back in.

Project slash commands (`/create-issue`, `/kickoff`, `/pr-feedback`, `/self-review`) live in `.claude/skills/`.
