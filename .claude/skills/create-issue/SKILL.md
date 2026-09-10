---
name: create-issue
description: "File a GitHub issue that survives contact with the future: interrogate the user for what only they know, verify every claim against the actual repo before writing it down, state the problem rather than decree a fix, label it, and create it. Also audits existing issues against the same bar (`audit <N>` / `audit all`). Use whenever the user wants to file, open, raise, log, or write up an issue, ticket, bug report or piece of tech debt — and whenever a session turns up a problem that isn't going to get fixed right now, even if they didn't use the word 'issue'. Prefer this over calling `gh issue create` directly, always."
argument-hint: "<what's wrong> | audit <issue number|all>"
allowed-tools: Bash(gh *) Bash(git *) Bash(grep *) Bash(sed *) Bash(cp *) Bash(diff *) Bash(cat *) Bash(od *) Bash(head *) Read Grep Glob AskUserQuestion
---

# Create Issue

Request: $ARGUMENTS

An issue in this repo is not a reminder. It is a message to someone — usually the
same person, months later — who has lost all the context that made the problem
obvious. `/kickoff` will pick it up cold and has to be able to trust it.

That sets the bar: **every factual claim in the body must be one a reader can
check**, and every claim you write must be one _you_ checked first. A cited
`file:line` that drifted two refactors ago is worse than no citation, because
it costs the next reader time before it costs them trust.

Two things this deliberately does _not_ do:

- **It doesn't decide the fix.** Design happens in `/kickoff`, against the code,
  with the user present. An issue that arrives as a spec quietly skips that
  step. Options with tradeoffs, yes — a decree, no. See _Stating the problem_.
- **It doesn't file on a hunch.** If the dig contradicts the premise, it stops and
  says so rather than adding a wrong issue to the backlog.

It **does** file without a final approval prompt. The interrogation _is_ the
approval gate — which means the interrogation has to be good enough that the user
never wishes they'd had one more look. Two exceptions halt it anyway, both in
Step 3.

Modes: **create** (default) and **audit** (`audit 63`, `audit all`) — see the
last section.

Note on project state: this repo is early — the design is sketched out in
`docs/design/kingdom-watch-plan-v7.1.md` (a living plan rather than a spec;
treat its claims the way `/kickoff` treats an issue's) but most of `Core`/`Game` doesn't
exist yet. Many issues right now will be design/scoping issues rather than code
bugs. Step 2's "verify against the code" still applies — just apply it to the
design doc and whatever _does_ exist (the Unity prototype, `docs/`) rather than
assuming a mature codebase.

---

## Step 1 — Take stock before asking anything

Questions the user already answered are the fastest way to spend their patience.
Start by working out what you actually have, because the entry points differ
enormously in how much is already known:

- **Mid-session discovery** — you hit this while doing something else, and the
  conversation is full of evidence. Mine the transcript first: the file you were
  reading, the failing test output, the thing the user said was wrong. Most of the
  body is already sitting in the session. Expect one round of questions.
- **Cold one-liner** — `/create-issue the smithy never gates on skill tier`.
  Nothing but a sentence. The repo dig in Step 2 does the heavy lifting, and
  expect two or three rounds.
- **One finding, several problems** — see Step 4 before grilling anything.

Write down, for yourself, the list of things you don't yet know. That list is what
Step 5 asks about, and nothing else.

---

## Step 2 — Dig, and verify every claim you intend to make

The user reports a symptom or a gap. The issue needs a mechanism, and the
mechanism lives in the design doc and/or the code. This step is what makes the
difference between a two-sentence issue and one that names the file, the
section, and the specific thing that's wrong.

**Fetch before you dig.** Every command below reads local refs, and a stale local
`main` biases all of them the same way: it hides work that has already merged, so
the dig "confirms" a problem that is fixed and you file it anyway.

```bash
git fetch origin --quiet
git rev-list --count main..origin/main   # 0 = up to date; anything else, fast-forward first
```

**An empty search result is not evidence of absence.** It is usually evidence you
guessed the wrong word. Try at least two phrasings before believing a negative,
and if you end up writing "there is no X" in the body, say what you searched for
so a wrong guess is visible.

Work through as much of this as the claim requires:

```bash
# The mechanism: find the symbol/section, then read it, then cite.
grep -rn "<term>" docs/ Game/Assets/ Core/ 2>/dev/null

# When it changed, and what it looked like before.
git log --format='%h %ad %s' --date=short -S "<snippet>" -- <path>
git log --oneline -n 5 -- <path>

# Prior art — open AND closed, because "we tried that" lives in closed issues.
gh issue list --state all --search "<topic>" --limit 8 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
gh pr list --state all --search "<keyword>" --limit 5 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
```

Then, before any line number goes in the body, **open the file and confirm the
content is at that line right now.** Cite `path:line` only for something you just
read. If you're citing a range that might shift, quote the snippet in a fenced
block as well — a quoted snippet stays checkable even after the line moves.

Check the conventions that make problems in this repo different from problems in
general — `AGENTS.md` and the design doc are the reference. This list is
intentionally thin right now and should grow as `Core`/`Game` get built:

- **Determinism rules** (design doc §5) — fixed timestep, keyed RNG, stable
  iteration order, canonical world-state hash. A bug that only reproduces at one
  LOD/compression level, or diverges between desktop and Android, is nearly
  always one of these.
- **The Core/Game boundary** — `Core` must stay zero-Unity-dependency and
  single-targeted (`netstandard2.1`). A change that reaches for a Unity API
  inside `Core`, or multi-targets it, is a real problem, not a style nit.
- **Milestone scope** — check which milestone (M0–M8) the affected area belongs
  to and whether the issue is actually in-scope yet, or describes work that's
  intentionally deferred (design doc §19–21 lists what's deferred and why).

---

## Step 3 — Stop if the dig undercuts the premise

Two findings are worth more than a filed issue, and both halt the run. Show the
evidence, then ask what the user wants to do — don't decide for them.

**It's already fixed, or a duplicate.** Show the commit, PR, or existing issue,
and offer the real choices: file anyway (the overlap is partial), add the new
evidence as a comment on the existing issue, or drop it. Say which relationship
you actually observed — _duplicates_, _overlaps in part_, _blocks_, _supersedes_
— they lead to different answers.

**The fix is smaller than the issue describing it.** If Step 2 landed on a
one-line change with an obvious shape and no design question, say so and offer to
just do it. The user may still want it filed — a fix they can't verify today, or
one that belongs to a batch — and that's a fine answer. Ask rather than assume.

Nothing else halts the run. A thin issue, an uncertain priority, a problem you
can't fully explain — those are things to grill about, not reasons to stop.

---

## Step 4 — If it's several problems, agree the split first

One investigation often surfaces several genuinely separate problems, and the
split is a judgement the user should make before anyone spends questions on the
pieces. Getting it wrong in either direction is expensive: three issues that are
really one produce three half-plans, and one issue that is really three never gets
closed.

Show the proposed split as titles plus a one-line scope each, and let the user
merge, drop, or reshape. The test for "separate" is whether each could be fixed,
verified, and closed on its own — not whether they were found together.

Then grill each surviving issue in turn — scope, priority and constraints differ
per issue, and a shared interrogation flattens exactly the details that matter.
Cross-link them in `## Related` once the numbers exist.

If it's genuinely one problem, skip this entirely.

---

## Step 5 — Grill

Use `AskUserQuestion`, batching up to four questions per call rather than
trickling them out — it's far cheaper to answer in one pass. Ask only what's on
the Step 1 unknowns list.

**Never ask what you could have grepped.** A question spent on something in the
repo spends the user's attention badly, and makes the real questions easier to
skim past. Ground each question in what the dig found.

Offer a recommended option where there's a sensible default, so cheap questions
stay cheap.

The dimensions worth probing, in rough order of how often they turn out to
matter — take what applies and drop the rest:

- **Expected vs actual.** What should have happened. Surprisingly often unstated,
  and it's the whole difference between a bug and a preference.
- **Reproduction** (for anything observed running). Exact steps, build
  configuration, platform, which scene/prototype.
- **Frequency and blast radius.** Every time or once; one system or a pattern
  repeated. This is usually what sets priority.
- **Scope edges.** What is explicitly _not_ part of this. The cheapest sentence in
  any issue.
- **Constraints that must hold.** Contracts a fix can't break — determinism, the
  Core/Game boundary, or a decision recorded in §2 of the design doc.
- **Milestone.** Which milestone this belongs to, if it's not obvious.
- **Priority** — always. See Step 7.

Stop when the remaining unknowns wouldn't change what a future reader does. Depth
should scale with what you don't know, not with ceremony.

---

## Step 6 — Compose the body

### Title

A title is the only part most readers see. Make it a **statement of what is
wrong or missing**, not a topic:

- `Skill tier gate is missing from the smithy build check`
- `Worker/scheduler ordering is undefined when two events land in the same phase`

An area prefix (`Worldgen:`, `Scheduler:`) earns its place when it scopes; a type
prefix (`bug:`, `feat:`) does not — that's what the label is for.

### Spine

Two sections are always there. The rest appear only when they have real content —
an empty `## Constraint` is worse than no `## Constraint`, because it teaches the
reader to skim headings.

**Always:**

- **An opening line or two of context** — where this was found and what was being
  done.
- **`## Problem`** — the mechanism, with the verified evidence inline: cited
  `path:line`, fenced snippets, observed behaviour with dates and build
  configuration. If there are several distinct facets, number them (`## 1. …`).

**When they have content:**

- **`## Impact`** — what this costs, concretely. Vague severity claims are worth
  nothing.
- **`## Options`** — see below.
- **`## Constraint`** — contracts a fix must not break.
- **`## Related`** — other issues, with the relationship stated.

### Stating the problem, not the fix

This is the part most worth getting right, and the line is finer than
"never suggest anything."

Thinking you had at filing time is genuinely valuable — throwing it away means
paying for it twice. What's harmful is thinking that arrives looking like a
decision, because `/kickoff`'s entire job is to design against the real code,
and an issue phrased as a spec makes skipping that step the path of least
resistance.

So: **if you have fix ideas, present them as options with tradeoffs, and mark any
leading candidate as a hypothesis.** Explicitly, in the text — the marking is what
does the work.

```markdown
## Options

Not settled — these are the shapes the fix could take, recorded while the context
was fresh. Tradeoffs are the point; pick at kickoff time against the real code.

**A. …** Tradeoff.
**B. …** Tradeoff.

Leaning A first — reason. That's a starting point for kickoff, not a decision.
```

What that avoids is `## Proposal` and `**Fix:** …`, which read as settled — even
when the author meant them as suggestions. Same content, different contract with
the reader.

Two shapes stay out of the body entirely: acceptance-criteria checklists and
named APIs presented as the design. Those are plan artifacts, and an issue holding
a stale spec is worse than an issue holding none.

### Voice

Write it as the user's own repo notes. No agent branding, no "I found", no
attribution footers — see `AGENTS.md`.

---

## Step 7 — Labels

Check what actually exists before proposing anything — this repo is new and may
not have a type/priority taxonomy set up yet:

```bash
gh label list
```

If a reasonable set exists, propose the full set with a one-line justification
each, and confirm via `AskUserQuestion` in the same round as the last content
questions where possible. If nothing useful exists yet, propose creating a small
starter set (`bug`, `enhancement`, `feature`, `tech-debt`, `design-question` —
this repo has plenty of open design questions per docs/design §21 — and a
priority scale) and wait for an explicit yes before creating labels; a label set
grows once and gets pruned never, so the bar is high.

```bash
gh label create "<name>" --description "<description>" --color "<hex>"
```

**Every issue gets a priority** once a priority label exists. If the user
genuinely can't call it, the lowest priority with a note beats silence.

---

## Step 8 — File it

Compose the body in a scratch file and pass it with `--body-file`. This is not
style: shell-quoted issue bodies routinely get mangled — every backtick becomes
`\`, or quotes get doubled — because the body went through inline shell quoting. A
file round-trip has no such failure mode.

The same reasoning applies to _editing_ the file once it exists. Issue bodies are
full of backticks, backslashes, quotes and `$`, which is precisely the character
set that shell quoting mangles. Use Read + Edit with literal strings for anything
containing backslashes or nested quotes.

Keep the file outside the repo (your session scratchpad or any temp dir), so a
stray issue body can never end up in a commit:

```bash
# Write the body to a scratch file first, then:
gh issue create \
  --title "<title>" \
  --body-file "<scratch>/issue-body.md" \
  --label "<type>" --label "<priority>"
```

Then read it back and look at it, because a rendering problem is invisible in the
source and permanent in the issue:

```bash
gh issue view <N> | head -40
```

Report the URL and the labels set. If the body came out wrong, fix it with
`gh issue edit <N> --body-file` immediately — before the user has to notice.

For a split from Step 4, file all of them, then edit the `## Related` sections to
cross-link the real numbers once they exist.

---

## Audit mode

`audit <N>`, `audit <N> <M>`, or `audit all` (all open issues, oldest first —
that's where the rot is). This applies the same bar above to issues that already
exist, and doubles as the honest test of whether the bar is any good.

**Nothing is written without approval.** Creating a wrong issue wastes triage;
editing a real one destroys history that nobody kept a copy of. Report findings,
per issue, and let the user decide each — including doing nothing, which is very
often right for an old issue that's thin but still true.

Load the issue with its comments — decisions in this repo can live only in a
comment and never get folded into the body. Then check:

1. **Is the problem still real?** Run the Step 2 dig against today's repo. Partial
   completion is the common case and the easy one to miss.
2. **Are the claims still true?** Cited `path:line` drifts. Verify each one and
   note which moved.
3. **Is the body intact?** Shell-quoting damage takes many forms — confirm
   suspicious characters with `od -c` before assuming a substitution is safe.
4. **Labels.** Missing priority, missing type, a `blocked` that may have quietly
   unblocked.
5. **Named blockers that have since closed.** Distinct from "already fixed" —
   the deferral itself went stale even though the issue still looks accurate.
6. **Title.** Does it state the problem or just name a topic?
7. **Is a fix presented as settled?** Flag for reframing as options.
8. **Decisions stranded in comments.** If a comment settles something the body
   still contradicts, the body is actively misleading. This is usually the
   highest-value finding an audit produces.

Present per issue: what you verified, what's wrong, and a specific recommended
action — _edit the body_, _add labels_, _close as fixed by `<sha>`_, _merge into
#N_, _leave it_. Then act only on what the user approves.

When editing an approved body, `gh issue edit --body-file` **replaces the whole
body** — so fetch, edit in place, diff, and only then write back.

```bash
gh issue view <N> --json body --jq .body > "<scratch>/issue-<N>.md"
cp "<scratch>/issue-<N>.md" "<scratch>/issue-<N>.orig.md"
# edit issue-<N>.md, then:
diff "<scratch>/issue-<N>.orig.md" "<scratch>/issue-<N>.md"
gh issue edit <N> --body-file "<scratch>/issue-<N>.md"
```

If the diff shows anything you didn't deliberately change, don't write it back.

For `audit all`, work oldest-first and report in batches rather than one wall of
findings.
