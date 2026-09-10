---
name: self-review
description: "Run automatically after any code changes in this session. Iterate: fix issues found, then re-run this review, until the review passes clean. Present the summary only when the review is clean. After 5 passes without a clean result, stop and ask the user. Ask the user immediately at any genuine decision point or unexpected discovery before proceeding."
allowed-tools: Read Glob Grep
---

# Self-Review

If issues are found: fix them, then re-run this review from the top. Do not present the summary until the review passes with no issues. Maximum 5 passes — if not clean after 5, stop and ask the user. If at any point you reach a decision where the correct fix is unclear or has significant consequences, stop and ask the user before continuing.

Review all code written in this session against the checklist below. Fix every issue found before presenting. Do not surface the list of bugs found — present only the clean summary.

**This checklist is intentionally thin right now.** KingdomWatch has almost no code yet — an M0 throwaway prototype in `Game/`, and `Core` holding only the entity identity types and the keyed RNG so far. Grow this list as real conventions emerge over time. A checklist item earns its place by having actually caught something once; don't pre-invent items for patterns that don't exist yet.

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

- New behavior has new tests; changed behavior has updated tests
- Bug fixes include a regression test **written first and seen to fail** — if the fix landed first, revert it, confirm red, restore (`AGENTS.md`, Engineering principles)
- Tests cover failure paths, not just the happy path
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
3. Residual concerns where the right approach is genuinely unclear

Do NOT list bugs found and fixed. Do NOT ask for approval on obvious decisions.
