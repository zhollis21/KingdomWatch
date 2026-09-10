---
name: self-review
description: "Run automatically after any code changes in this session. Iterate: fix issues found, then re-run this review, until the review passes clean. Present the summary only when the review is clean. After 5 passes without a clean result, stop and ask the user. Ask the user immediately at any genuine decision point or unexpected discovery before proceeding."
allowed-tools: Read Glob Grep
---

# Self-Review

If issues are found: fix them, then re-run this review from the top. Do not present the summary until the review passes with no issues. Maximum 5 passes — if not clean after 5, stop and ask the user. If at any point you reach a decision where the correct fix is unclear or has significant consequences, stop and ask the user before continuing.

Review all code written in this session against the checklist below. Fix every issue found before presenting. Do not surface the list of bugs found — present only the clean summary.

**Run this generatively, not confirmationally.** The failure mode is reading the code and tests you just wrote and asking "does this look right?" — that question can only confirm what is there. It cannot find what is missing, because what is missing is not in the list you are reading. For every checklist item, derive from the code what *should* exist, write that down, and diff it against what does. Items that are binary and greppable (a suppression, a stale reference, an unused using) survive the lazier reading; items needing enumeration — test coverage above all — do not.

**This checklist is intentionally thin right now.** KingdomWatch has almost no code yet — an M0 throwaway prototype in `Game/`, and `Core` holding the entity identity types, the keyed RNG and person storage so far. Grow this list as real conventions emerge over time. A checklist item earns its place by having actually caught something once; don't pre-invent items for patterns that don't exist yet.

## Checklist

**Determinism** (design doc §5 — mandatory once `Core` exists)

- Fixed timestep; simulation time never derived from `Time.deltaTime` or wall-clock
- Own seeded PRNG only — never `UnityEngine.Random` inside `Core`
- Randomness keyed to stable event/decision identity, not drawn from a mutable per-subsystem stream, wherever execution paths can differ by LOD (see §5 "Randomness must be keyed, not streamed")
- Each decision type has its own `RandomDomain` value rather than reusing a broad one — draws collide only within a domain, and a collision is silent (see `AGENTS.md`)
- Stable iteration order — never iterate a `Dictionary`/`HashSet` and act on the order
- No reentrant event handling with arbitrary subscriber order (§4)

**Core/Game boundary** (design doc §5)

- No game logic in MonoBehaviours
- Nothing in `Core` references a Unity API, directly or transitively
- `Core` stays single-targeted (`netstandard2.1`) — no `#if` splitting behavior between targets
- New storage fields go through the accessor layer (`PersonStore` or equivalent), not touched directly by systems

**Execution trace**

- Walk the happy path end-to-end
- Walk every failure path end-to-end

**Error handling**

- Failures are visible (logged, surfaced, or explicitly and deliberately swallowed with a comment saying why) rather than silently discarded

**Tests**

Judge coverage from the API surface, never from the test list. Before deciding the tests are thorough, **build the matrix**: every public entry point down one axis, every class of input a caller can actually pass across the other — valid, sentinel (`default`, `None`), boundary (zero, one, one-past-the-last, the min and max of the field's own type), malformed, and stale. Tick the cells that have a real test. Then name the empty cells out loud and decide deliberately which to fill and why the rest can stay empty. An empty cell nobody named is an oversight; an empty cell with a stated reason is a decision.

- New behavior has new tests; changed behavior has updated tests
- **Mutation-check every assertion that protects an invariant that matters.** Break the invariant on purpose — weaken the comparison, delete the guard clause, drop half a compound condition — confirm the test goes red, then restore. This applies to new code, not only to bug fixes: a test that has never been seen to fail may be passing for a reason unrelated to what it claims, and full coverage will not reveal that, since the lines still run. Say in the summary which invariants were mutation-checked and what the mutation was.
- Bug fixes include a regression test **written first and seen to fail** — if the fix landed first, revert it, confirm red, restore (`AGENTS.md`, Engineering principles)
- Tests cover failure paths, not just the happy path — and *every* failure path the code can take, not one representative sample. Four rejection branches want four cases, not one that happens to hit the first
- **Destructive operations get their own invalid-input tests, separate from the read operations.** A stale or malformed reference that reads the wrong data is a bug; one that writes, removes or deletes is a worse bug, it runs through different code, and it is the one that looks like legitimate behavior afterwards. Testing the read path does not cover the write path
- **Where two branches interact, test the crossing in a single sequence** — a free list that drains and then falls back to allocating, a cache that fills and then evicts, a buffer that grows after being partly recycled. Each branch tested alone can pass while the handover between them is broken
- **Invariants that hand-maintained bookkeeping can violate get asserted directly** — a count kept alongside a collection, a parallel index, a reverse map. Drive a mixed sequence of operations, then assert the two agree, rather than trusting each operation to have maintained it
- Values round-trip at the extremes of their own type, not just at comfortable mid-range values — this is what catches a narrowing cast or a silent clamp
- Tests probe the input space, not just the happy path — for every public entry point, what a caller *can* pass rather than what the docs say they should. Enums accept any cast int; structs have a `default`; sentinels and boundaries need their own cases (`AGENTS.md`, Building)
- Any public API taking an enum rejects undefined values (`EnumGuard`) — full coverage will not catch this, since the lines still run
- Determinism has tests of its own where it is a requirement: repeat the same read twice and assert the order did not vary; assert that a sequence of operations lands in a fixed, stated arrangement rather than merely a valid one
- A guard that cannot be reached, and cannot be reached in a test either, is unverifiable code — prefer deleting it over shipping it, unless a seam that exists for real reasons already makes it testable (see `IdAllocator`'s exhaustion guard, reachable through the `ResumeFrom` that save/load needs anyway)
- Anything in `Core`/`Harness` should be tested there — off-device, no Unity Editor required. Anything that can only be tested from inside the Unity Editor or on-device is a real cost; ask whether the logic actually needs to live there.

**Build & test health**

- Build passes with zero warnings, across every project touched
- Tests green locally before presenting
- No `#pragma warning disable`, `#nullable disable`, or null-suppressions (`!`) without explicit approval — a justifying comment is not enough; ask first

**Dead code**

- After refactors: removed usings, unreferenced private members, unused parameters, orphaned files
- No commented-out code left behind

**Scope discipline**

- Change stays within what the task required — no opportunistic renames, reorders, or unrelated fixups bundled in. If unrelated issues are spotted during the work, note them and consider filing an issue (`/create-issue`) rather than fixing them in the same change
- No half-finished work (stubbed methods, `throw new NotImplementedException()`, empty branches) unless tracked by an issue referenced in a comment

**Conventions**

- New code matches surrounding style (naming, file organization, access modifiers, async patterns)
- New abstractions follow existing patterns rather than introducing parallel ones
- Simplest implementation that does the job — no bit-packing, caching, or hand-tuning without a specific identified need (`AGENTS.md`, Engineering principles)
- **Verify claimed precedent instead of recalling it.** Before a decision rests on "the neighbouring code does this" — go read that code. Precedent recalled from memory is usually right about the pattern and wrong about the detail, and the detail is what settles the question: whether that guard is *tested*, whether that field is *validated*, whether that suppression was actually *approved*. One grep, before the justification, not after someone challenges it

**TODOs**

- No new `TODO` / `FIXME` / `HACK` left behind unless tracked in an issue (reference the issue number)

**Commit-readiness**

- If multiple commits are planned, each builds and tests green on its own

**Documentation**

- Check repo docs for sections that need updating based on the changes: `README.md`, `AGENTS.md`, `CLAUDE.md`, and any relevant skill files under `.claude/skills/`
- Look for stale references to architecture, services, conventions, build steps, or patterns touched in this session
- If updates are needed, make them before presenting the summary

## Summary Format

Present:

1. What was done and why (brief)
2. Architectural tradeoffs or non-obvious decisions
3. How the work was verified — which invariants were mutation-checked and what the mutation was, plus any matrix cell left deliberately untested and why
4. Residual concerns where the right approach is genuinely unclear

Do NOT list bugs found and fixed. Do NOT ask for approval on obvious decisions.
