---
name: kickoff
description: 'Start work on a GitHub issue the right way: pull the issue, verify it isn''t stale or already fixed, analyze solutions independently of whatever the issue proposes, surface tradeoffs, ask clarifying questions, and agree a plan before any code exists. Use whenever the user names issue numbers to work on — "let''s do #12", "start issue 4", "pick up 9 and 14" — or asks to take something off the backlog. Prefer this over jumping straight into implementation, even when the issue looks obvious.'
argument-hint: "<issue number> [more issue numbers]"
allowed-tools: Bash(gh *) Bash(git *) Bash(cp *) Bash(diff *) Read Grep Glob AskUserQuestion ExitPlanMode
---

# Kickoff

Kicking off: $ARGUMENTS

The point of this skill is to spend thinking time where it is cheap. A wrong
assumption costs seconds to fix while it is still a sentence in a plan, and hours
once it is code with tests and a PR built on top of it. So the order here is
deliberately: understand → verify → analyze → ask → agree → _then_ branch.

Nothing gets implemented during this skill. It ends at an approved plan and a
branch to build it on.

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

## Step 1 — Load the issues

```bash
gh issue view <N> --json number,title,state,body,labels,createdAt,updatedAt,closedAt,comments \
  --template '#{{.number}} {{.title}} [{{.state}}] created={{.createdAt}} updated={{.updatedAt}} closed={{.closedAt}}{{"\n"}}labels:{{range .labels}} {{.name}}{{end}}{{"\n"}}--- body ---{{"\n"}}{{.body}}{{range .comments}}{{"\n"}}--- comment by {{.author.login}} @ {{.createdAt}} ---{{"\n"}}{{.body}}{{end}}'
```

That template deliberately renders every comment body, not just a count. Comments
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
gh pr list --state all --search "<keyword>" --limit 5 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
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
gh issue list --state all --search "<topic>" --limit 8 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
```

What you're looking for is not only exact duplicates but **dependency order**,
which is easier to miss and more expensive to get wrong — this repo's milestone
issues (M0–M8, see `gh issue list --milestone`) have real ordering dependencies
between them even when nothing says so explicitly (design doc §20's "before
M1/M3/M5/M7" lists are the fast way to check).

Report the relationship you actually found rather than rounding it to
"duplicate": _blocks_, _overlaps in part_, _supersedes_, and _duplicates_ lead to
different recommendations.

**The unstated assumption.** Most issues quietly assume some mechanism already
behaves a certain way, and the assumption is worth checking directly — it is
where the highest-value findings hide, precisely because nobody wrote it down to
be questioned.

**External blockers.** For anything labeled `blocked`, or that depends on an open
design question (design doc §21), confirm the blocker's current status rather
than assuming.

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
# --paginate is load-bearing: the endpoint returns 30 comments per page by
# default, so on a busy issue an unpaginated lookup misses the marker and
# silently posts a duplicate instead of updating.
gh api --paginate 'repos/zhollis21/KingdomWatch/issues/<N>/comments?per_page=100' \
  --jq '.[] | select(.body | contains("<!-- kickoff-findings -->")) | .id'

# Update it in place
gh api repos/zhollis21/KingdomWatch/issues/comments/<id> -X PATCH -f body='<!-- kickoff-findings -->
...'

# Or, if none exists, create it
gh issue comment <N> --body '<!-- kickoff-findings -->
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

## Step 4 — Ask clarifying questions

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

`gh issue edit --body-file` **replaces the entire body**, so never compose a
replacement from scratch — fetch what is there, change the contradicted claims in
place, and write the whole thing back.

```bash
# 1. Fetch the current body, and keep a pristine copy to diff against.
gh issue view <N> --json body --jq .body > "<scratch>/issue-<N>-body.md"
cp "<scratch>/issue-<N>-body.md" "<scratch>/issue-<N>-body.orig.md"

# 2. Edit issue-<N>-body.md, changing only the claims the plan contradicts.

# 3. Confirm the diff contains only deliberate changes.
diff "<scratch>/issue-<N>-body.orig.md" "<scratch>/issue-<N>-body.md"

# 4. Write the complete, modified body back.
gh issue edit <N> --body-file "<scratch>/issue-<N>-body.md"
```

If step 3 shows anything you did not deliberately change, do not run step 4.

Then leave a short comment recording what changed and why — the edit keeps the
issue truthful, the comment keeps the history.

This is safe to do without asking, because you are recording a decision the user
just approved rather than one you reached on your own. If the plan contradicts
nothing the issue states, change nothing.

---

## Step 7 — Cut the branch

Only after approval. Branch off `origin/main` — not local `main`, unless Step 2's
fetch confirmed the two are level — named `feature/<issue#>-<short-slug>`
(e.g. `feature/12-scheduler-phase-ordering`). For a batch that is genuinely one
unit of work, use the lowest issue number and mention the others in the branch
name or the eventual PR body.

Confirm the branch name before creating it; it is annoying to rename later.

Create the branch and stop there. Don't commit, don't push, and don't start
implementing — per `AGENTS.md`, those are separate explicit asks. Hand back with
a one-line summary of what was agreed and what the next step is.

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
