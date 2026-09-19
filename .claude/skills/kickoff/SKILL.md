---
name: kickoff
description: 'Start work on a GitHub issue the right way: pull the issue, verify it isn''t stale or already fixed, analyze solutions independently of whatever the issue proposes, brief the user on the issue, where it fits and the decisions it needs, ask clarifying questions, agree a plan, then build it. Use whenever the user names issue numbers to work on — "let''s do #12", "start issue 4", "pick up 9 and 14" — or asks to take something off the backlog. Also `/kickoff next` (or "what should I work on", "what''s next") for a brief on what is ready to pick up and which of it matters most, before any issue is named. Prefer this over jumping straight into implementation, even when the issue looks obvious.'
argument-hint: "<issue number> [more issue numbers] | next"
allowed-tools: Bash(gh *) Bash(git *) Bash(curl *) Bash(cp *) Bash(diff *) Bash(dotnet *) Bash(pwsh *) Read Grep Glob Edit Write AskUserQuestion EnterPlanMode ExitPlanMode Skill
---

# Kickoff

Kicking off: $ARGUMENTS

The point of this skill is to spend thinking time where it is cheap. A wrong
assumption costs seconds to fix while it is still a sentence in a plan, and hours
once it is code with tests and a PR built on top of it. So the order here is
deliberately: understand → verify → analyze → brief → ask → agree → _then_ branch
and build.

Nothing gets implemented before the plan is approved. Once it is, the approval is
the go-ahead: cut the branch and carry straight on into implementation.

It does write to GitHub, in two narrow places, because findings that live only in
a chat window get re-derived at full price by the next person to open the issue:

- **Step 2** posts verified facts it observed — issue state, content that exists
  at a cited line, the commit that changed something. Facts only, never
  conclusions.
- **Step 6** corrects statements in the issue that the approved plan just made
  untrue.

Both are deliberate. What it never does unattended is publish a judgement — that
an issue is stale, or should be closed, or that one approach beats another. Those
stay in the conversation where they can be argued with.

---

## `next` — what should I pick up?

When the argument is `next` (or the user asks what to work on without naming an
issue), the job is a brief, not a plan. Read the published graph — one request,
rather than the ~70 the generator makes:

```bash
curl -sS https://zhollis21.github.io/KingdomWatch/graph.json -o "<scratch>/graph.json"
```

Its `generated` field says when the workflow last built it. That run fires on
issue, milestone and pull-request events, nightly, and on demand, so the answer
is normally minutes old. A copy with no `generated` at all was published before
the stamp existed, which makes it old by definition. **Regenerate locally instead when it is not good
enough** — after re-wiring a relationship (those fire no webhook at all, which
is the one case the published copy is reliably wrong about), when `generated` is
older than something you know happened, or when the brief contradicts what you
just read on GitHub:

```bash
pwsh tools/Build-Roadmap.ps1   # writes the same file to docs/roadmap/, straight from GitHub
```

Then from the graph (`ready`, and each issue's `milestone`, `priority`,
`openBlocking`), write the brief — short enough to read on a phone:

1. **The ready list**, grouped by milestone in order, priority first within
   each. One line per issue: number, title, priority, and what closing it
   unblocks (`openBlocking` — dependents still open; `blocking` includes closed ones and overstates it), counted and named if few.
2. **A recommendation, with the reason.** Usually the ready issue in the
   earliest milestone whose `openBlocking` list is longest — it is the one holding
   the most other work back. Say when priority and fan-out disagree (a P0 that
   unblocks nothing versus a P1 that unblocks nine) rather than silently
   picking one.
3. **What is close to ready** — open issues whose only open blocker is on the
   ready list, so the user can see what a pick unlocks next. An issue with a
   non-empty `foreignBlockers` (a blocker in another repository) is never
   close: its blocker cannot be read, so it is treated as open.
4. **Warnings** from the graph, verbatim, if any: a cycle or a missing
   milestone is a decision waiting to be made.

Then ask which one to kick off — and when they answer, carry on from Step 1
with that number. The brief is derived from the same edges the roadmap draws,
so if the user disagrees with it, the fix is a relationship, not a rerun.

---

## Step 1 — Load the issues

```bash
# REST, not `gh issue view`: every `gh issue`/`gh pr` subcommand is GraphQL-backed
# and is refused from a Claude Code session, so this is two calls rather than one.
# See AGENTS.md, "Calling the GitHub API from a Claude Code cloud session".
gh api repos/zhollis21/KingdomWatch/issues/<N> --jq '
  "#\(.number) \(.title) [\(.state)] created=\(.created_at) updated=\(.updated_at) closed=\(.closed_at)",
  "labels: \([.labels[].name] | join(" "))",
  "--- body ---", .body'

# One command, not a loop: `gh --paginate` follows a Link header the proxy
# rejects, and a `for` loop is a compound command that starts with `for`, so
# it falls outside this skill's command-prefix allow rules. The script throws
# rather than quietly truncating if a collection outgrows its page cap.
pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues/<N>/comments' \
  | jq -r '.[] | "--- comment by \(.user.login) @ \(.created_at) ---", .body'
```

That second call deliberately renders every comment body, not just a count. Comments
are where a decision that nobody folded back into the body tends to live, and can
be disproportionately likely to settle an issue on their own. A count tells you
nothing.

Issue bodies in this repo will vary in density — some may be little more than one
sentence pulled from the design doc's Open Questions (§21), others dense with
options and tradeoffs already thought through. Sparse bodies mean most of your
effort belongs in Step 4; dense ones mean pressure-test what's there rather than
generating from scratch — it's still a hypothesis, not a spec.

**Check `state` first.** A closed issue is the fastest possible staleness result.
If it is `CLOSED`, say so immediately with the close date, and ask whether they
meant a different number or want to reopen the topic. Don't plan work for it on
the assumption they know.

---

## Step 2 — Verify the issue is still real

This is the step that earns the skill its keep. Work through these deliberately
rather than trusting the issue's framing:

**Fetch first — your local `main` is probably stale.** Do this before any other
check in this step, because every `git log` and "does this exist yet" check below
reads from local refs, and a stale `main` makes all of them lie in the same
direction: work that has already merged looks unbuilt.

```bash
git fetch origin --quiet
git log --oneline -1 main
git log --oneline -1 origin/main
git rev-list --count main..origin/main   # 0 = up to date
```

If `main` is behind, fast-forward it before going further:

```bash
git merge-base --is-ancestor main origin/main   # confirm fast-forward is safe
git branch -f main origin/main                  # when not checked out on main
```

**Search broadly before concluding anything.** An empty search result is not
evidence of absence, it is usually evidence you guessed the wrong word. Try at
least two phrasings before believing a negative. When you do report something as
missing, say what you searched for, so the user can spot a wrong guess.

**Already fixed, wholly or partly.** Search for the terms, sections, or files the
issue names, and check history since it was filed:

```bash
git log --oneline --since=<issue createdAt> --grep=<keyword> -i
git log --format='%h %ad %s' --date=short -S "<snippet>" -- <path>
# Repo-scoped, then filtered here: the search/ endpoints are refused as well
# ("sessions are bound to their configured repositories").
# The term is matched literally, not as a regex: jq's test() would read
# `Found(` as an unterminated group and fail outright, and `a.b` would
# quietly match `axb`. --arg also keeps the shell out of the quoting.
# One command, not a loop: `gh --paginate` follows a Link header the proxy
# rejects, and a `for` loop is a compound command that starts with `for`, so
# it falls outside this skill's command-prefix allow rules. The script throws
# rather than quietly truncating if a collection outgrows its page cap.
pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/pulls?state=all' \
  | jq -r --arg keyword '<keyword>' '.[] | select((.title + " " + (.body // "")) | ascii_downcase | contains($keyword | ascii_downcase))
           | "#\(.number) [\(.state)] \(.title)"' | head -5
```

`git log -S` is the more reliable of the two log commands — it finds the commit
where a string actually entered or left the content, regardless of what the
commit message claimed.

Partial completion is the common case and the easy one to miss — half the issue
shipped, the remainder is still valid but the framing no longer matches reality.

**Superseded by a documented decision — but date it before you believe it.**
The design doc explains why the game is the way it is, which looks like a
decision to keep it that way, and often isn't. Compare when a relevant section
was written against when the issue was filed:

```bash
git log --format='%h %ad %s' --date=short -S "<phrase from the doc>" -- docs/design/kingdom-watch-plan-v7.1.md
```

Documentation written _after_ an issue, by someone who knew about it, is a
decision. Documentation written _alongside_ the very work the issue is following
up on is just a description of the gap.

**Duplicate or overlapping open issues.** Search the whole issue list, not just
open ones:

```bash
# The term is matched literally, not as a regex: jq's test() would read
# `Found(` as an unterminated group and fail outright, and `a.b` would
# quietly match `axb`. --arg also keeps the shell out of the quoting.
# One command, not a loop: `gh --paginate` follows a Link header the proxy
# rejects, and a `for` loop is a compound command that starts with `for`, so
# it falls outside this skill's command-prefix allow rules. The script throws
# rather than quietly truncating if a collection outgrows its page cap.
pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues?state=all' \
  | jq -r --arg topic '<topic>' '.[] | select(.pull_request == null)
           | select((.title + " " + (.body // "")) | ascii_downcase | contains($topic | ascii_downcase))
           | "#\(.number) [\(.state)] \(.title)"' | head -8
```

What you're looking for is not only exact duplicates but **dependency order**,
which is easier to miss and more expensive to get wrong. The recorded order is
in the graph — each issue's `blockedBy`, `blocking`, `openBlocking` and `ready`.
Read the published copy as above, or regenerate with
`pwsh tools/Build-Roadmap.ps1` (read-only against GitHub, writes the git-ignored
`docs/roadmap/`) when this run has edited a relationship. Read it
before the search, then treat the search as a check on it: an issue the dig
says this one depends on, or unblocks, that the graph does not list is a
missing relationship, and fixing it is part of kickoff (see
`AGENTS.md` § Issue dependencies for how). Design doc §20's "before
M1/M3/M5/M7" lists are the independent cross-check.

If `ready` is false, stop and say so before anything else: the issue has an
open blocker, and starting it anyway is a decision for the user, not a default.

Report the relationship you actually found rather than rounding it to
"duplicate": _blocks_, _overlaps in part_, _supersedes_, and _duplicates_ lead to
different recommendations.

**The unstated assumption.** Most issues quietly assume some mechanism already
behaves a certain way, and the assumption is worth checking directly — it is
where the highest-value findings hide, precisely because nobody wrote it down to
be questioned.

**External blockers.** For anything with an open blocker in the graph, or that
depends on an open design question (design doc §21), confirm the blocker's
current status rather than assuming. The `blocked` label is derived from the
relationships by the roadmap workflow; it is a symptom, never the thing to fix.

**Report before planning.** When you find staleness, stop and lay it out: what
specifically changed, the commit / PR / doc that changed it, how much of the
issue survives, and your recommendation (close it, narrow it, merge it with
another, or proceed as written). Then ask what they want to do. Cite evidence
rather than impressions.

**Record verified facts on the issue.** Findings die in the chat window
otherwise, and the next person to open the issue pays the same verification cost
again. Post them without asking — but only facts, and only with citations,
because an unattended write to a public issue is the one place a wrong claim does
lasting damage.

**Post automatically only what a command proved.** Each line must be checkable by
someone reading it, and carry the evidence that makes it falsifiable:

- issue state and dates
- content that exists now, cited as `path:line`
- a commit or PR that changed something, cited by SHA or number
- another issue covering related scope, cited by number — state the overlap you
  observed; _blocks_ and _supersedes_ are conclusions rather than observations,
  so they stay in the chat

**Never post automatically:** that an issue is stale, that it should be closed or
narrowed, which approach is better, or anything else you concluded rather than
observed. Those belong in the chat, where the user can correct them.

**Update your previous comment instead of adding another.** Kickoff runs repeat,
and an issue accumulating near-identical findings comments is worse than no
findings at all. Mark the comment and look for that marker first:

```bash
# Find a previous findings comment (numeric id, or empty if this is the first).
# Reading every page is load-bearing: missing the marker silently posts a
# duplicate instead of updating. But `gh --paginate` cannot be used to do it —
# it follows GitHub's Link header, which points at the /repositories/{id}/...
# form that the proxy in front of Claude Code sessions rejects with a 403, so
# it breaks the moment an issue outgrows one page. Walk the pages by hand.
# One command, not a loop: `gh --paginate` follows a Link header the proxy
# rejects, and a `for` loop is a compound command that starts with `for`, so
# it falls outside this skill's command-prefix allow rules. The script throws
# rather than quietly truncating if a collection outgrows its page cap.
pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues/<N>/comments' \
  | jq -r '.[] | select(.body | contains("<!-- kickoff-findings -->")) | .id'

# Update it in place
gh api repos/zhollis21/KingdomWatch/issues/comments/<id> -X PATCH -f body='<!-- kickoff-findings -->
...'

# Or, if none exists, create it
gh api repos/zhollis21/KingdomWatch/issues/<N>/comments -f body='<!-- kickoff-findings -->
...'
```

Write it in the user's own voice as plain repo notes — no agent branding, per
`AGENTS.md`. One consolidated comment per issue, not one per finding. If
verification turned up nothing worth persisting, post nothing; silence is a
perfectly good result.

---

## Step 3 — Work out the right solution

If the issue proposes a solution, treat it as a well-informed hypothesis from
someone who had context you may lack — worth taking seriously, not worth
adopting unexamined.

Come up with at least one genuine alternative before settling. If the issue's
option really is best, saying _why_ it beat the alternative is far more useful
than saying it was the only thing considered.

Check candidate approaches against the conventions that actually bite here (see
`AGENTS.md` and the design doc):

- **Determinism** (design doc §5) — fixed timestep, keyed RNG (never
  `UnityEngine.Random`), stable iteration order, no reentrant event handling.
  Getting this wrong is invisible until it silently diverges at a different LOD
  or on a different platform.
- **The Core/Game boundary** — `Core` stays `netstandard2.1`, zero Unity
  dependencies, single-targeted. An approach that reaches for a Unity API inside
  `Core` is a real tradeoff to name, not a detail.
- **Milestone scope** — check design doc §19–21 for what's explicitly deferred.
  An approach that quietly pulls in deferred scope (sieges, explicit tech trees,
  disease) is a real tradeoff to name.
- **Testability** — anything in `Core`/`Harness` is unit-testable off-device;
  anything Unity-only is not without the Editor or a device.

Name the tradeoffs plainly, including the ones that argue against your own
recommendation. Flag anything that is hard to reverse or changes on-device
behavior you cannot verify from the desktop.

---

## Step 4 — Brief, then ask

**Brief first, questions second.** Before the first `AskUserQuestion`, write a
short overview in the conversation so the user is answering from the same picture
you are. A question arriving cold — "refuse or queue nested publishes?" — asks the
user to reconstruct the whole problem in their head before they can answer it;
the same question after two paragraphs of context is answerable in a sentence.

The brief covers, in this order and briefly:

- **What the issue is** — the problem in your own words, reflecting what Step 2
  verified rather than the issue's title. Say what already exists that this
  builds on, with file paths.
- **Where it fits** — which milestone, what depends on it, what it depends on,
  and which neighbouring issues will consume or feed it (the dependency order
  from Step 2). Say what closing this unblocks — that is usually the best
  argument for doing it now, or for not.
- **The decisions to make** — each one named, with the options and the tradeoff
  in a line or two apiece, and your recommendation. This is the list Step 3
  produced. If a decision has an obvious default and no real alternative, say
  so and do not put it to the user.

Write it in plain language. The user may not have the design doc open, and a
decision explained as "the clock already refuses reentrant dispatch, so the bus
should too" lands better than a paragraph of tradeoffs that assumes they remember
why. Keep it to what the questions need — this is a brief, not the plan; the plan
is Step 5.

Then ask.

The user's explicit goal is to fix it right the first time, which means questions
are welcome — but their value comes from being answerable and consequential, not
from their quantity.

Ask about anything where two readings lead to genuinely different code. Skip
anything you can settle yourself by reading the repo/design doc; burning a
question on something greppable spends the user's attention badly.

Worth probing on most issues:

- Scope edges — what is explicitly _not_ in this change
- What "done" looks like, concretely enough to verify
- Existing behavior that may be relied on and shouldn't change
- Which milestone this belongs to, if ambiguous

Use `AskUserQuestion` for these rather than listing them in prose. Batch related
questions into a single call instead of trickling them out one at a time. If a
question has an obvious sensible default, offer it as the recommended option.

---

## Step 5 — Present the plan and get approval

Present the plan in the conversation and end at an explicit approval gate
(`ExitPlanMode`) so nothing gets built before the user has agreed. Include:

- **What we're solving** — restated in your own words, reflecting what you
  verified in Step 2, not just the issue's title
- **Approach chosen, and what it beat** — with the reasoning
- **Steps** — ordered, each concrete enough to act on, naming the files involved
- **Tradeoffs and risks** — what could go wrong, what is hard to undo
- **Verification** — how we'll confirm it works (harness/NUnit for `Core`, the
  Unity Editor / on-device for anything in `Game`)
- **Out of scope** — what we're deliberately not doing

If the answers in Step 4 changed your thinking, say so and say how.

---

## Step 6 — Correct what the plan just made untrue

Planning frequently supersedes something the issue states outright. Fixing it
costs seconds here and is the cheapest moment it will ever cost.

Compare the approved plan against the issue's **specific claims** — acceptance
criteria, checklists, counts, named files, proposed APIs — and change only what
the plan actually contradicts. This is narrow work: correcting statements that
stopped being true in the last ten minutes, not rewriting or tidying the issue.

`gh api … -X PATCH -F body=@<file>` **replaces the entire body**, so never compose a
replacement from scratch — fetch what is there, change the contradicted claims in
place, and write the whole thing back.

```bash
# 1. Fetch the current body, and keep a pristine copy to diff against.
gh api repos/zhollis21/KingdomWatch/issues/<N> --jq .body > "<scratch>/issue-<N>-body.md"
cp "<scratch>/issue-<N>-body.md" "<scratch>/issue-<N>-body.orig.md"

# 2. Edit issue-<N>-body.md, changing only the claims the plan contradicts.

# 3. Confirm the diff contains only deliberate changes.
diff "<scratch>/issue-<N>-body.orig.md" "<scratch>/issue-<N>-body.md"

# 4. Write the complete, modified body back.
gh api repos/zhollis21/KingdomWatch/issues/<N> -X PATCH -F body=@"<scratch>/issue-<N>-body.md"
```

If step 3 shows anything you did not deliberately change, do not run step 4.

Then leave a short comment recording what changed and why — the edit keeps the
issue truthful, the comment keeps the history.

This is safe to do without asking, because you are recording a decision the user
just approved rather than one you reached on your own. If the plan contradicts
nothing the issue states, change nothing.

The same applies to the dependency graph. If the plan narrowed scope and pushed
work into new issues (`/create-issue` files them and wires their
relationships), or Step 2 found a dependency the graph lacks, record it now as a
native *blocked by* relationship — direct edges only, per `AGENTS.md` § Issue
dependencies — and run `gh api -X POST repos/zhollis21/KingdomWatch/actions/workflows/roadmap.yml/dispatches -f ref=main`, since relationship edits
do not trigger the regeneration on their own.

---

## Step 7 — Cut the branch

Only after approval. Branch off `origin/main` — not local `main`, unless Step 2's
fetch confirmed the two are level — named `feature/<issue#>-<short-slug>`
(e.g. `feature/12-scheduler-phase-ordering`). For a batch that is genuinely one
unit of work, use the lowest issue number and mention the others in the branch
name or the eventual PR body.

Confirm the branch name before creating it; it is annoying to rename later.

Create the branch and carry on: the approved plan is the instruction to
implement, so start building without asking again. Commits, pushes and the PR
remain separate explicit asks per `AGENTS.md`; run `/self-review` before
presenting the result.

---

## Handling several issues at once

When given multiple numbers, work out whether they are one unit of work or
separate threads before planning anything — the answer changes everything
downstream.

They are **one unit** when they share a root cause, when one is a precondition
for another, or when doing them separately would mean touching the same code
twice. Plan them together, one branch, and be explicit about the ordering
between them.

They are **separate** when they merely share a milestone or a label. Say so, and
ask whether to take them one at a time or plan both now.

Either way, run Step 2 on each issue independently. Staleness is per-issue, and a
batch is exactly where a closed or superseded one hides.

---

## Related skills

- `/create-issue` — file a new issue with the same evidence bar
- `/pr-feedback` — the review pass to run once implementation lands and a PR is open
- `/self-review` — self-review checklist to run after writing code
