---
name: pr-feedback
description: "Pull open PR review comments for this repository, present an overview of each finding with whether it is valid and what the options are, then act on what the user chooses. Use when asked to address PR feedback, review open comments, or work through reviewer notes."
allowed-tools: Bash(pwsh tools/Get-OpenPrComments.ps1) Bash(pwsh tools/Resolve-PrThread.ps1:*) Bash(gh api repos/:*) Bash(git *) Bash(dotnet *) Read Glob Grep Edit Write AskUserQuestion
---

# PR Feedback

## PR Comments

!`pwsh tools/Get-OpenPrComments.ps1 && cat tools/pr-comments.md`

## Sync before you evaluate anything

The comments above came from GitHub. Your working tree did not. Evaluating remote
feedback against a stale local checkout is the fastest way to reach a confident
wrong answer — you tell the user a reviewer is mistaken, when the code they are
describing is real and simply isn't on your disk.

```bash
git fetch origin --quiet
# REST, not `gh pr view`: every `gh pr`/`gh issue` subcommand is GraphQL-backed and
# is refused from a Claude Code session. See AGENTS.md, "Calling the GitHub API
# from a Claude Code cloud session".
gh api repos/zhollis21/KingdomWatch/pulls/<pr> \
  --jq '{headRefName: .head.ref, headRefOid: .head.sha, baseRefName: .base.ref}'
git log --oneline -1 HEAD                  # compare against headRefOid above
git rev-list --count HEAD..origin/<headRefName>   # 0 = you have every pushed commit
```

Three ways this can bite:

- **Suggestions committed in the GitHub UI.** Accepting a reviewer's suggested
  change creates a commit directly on the PR branch. It exists on the remote and
  nowhere locally, so the fix looks unapplied and you "fix" it a second time.
- **A comment that is already addressed.** A later push can resolve feedback that
  the thread list still shows as open. Check whether the cited line still says
  what the comment quotes before deciding the concern is live.
- **A moved base.** Reasoning like "this conflicts with how `main` does it" is
  only as good as your `origin/main`. Fetch before making that argument.

If local is behind the PR branch, pull before evaluating. If you cannot — dirty
tree, mid-rebase — say so explicitly and treat every code claim as provisional
rather than quietly reasoning from the stale copy.

## Suppressed comments are not optional reading

Some review tools (e.g. Copilot) post some findings as inline threads and file
the rest under **Suppressed comments** inside the review body — findings they
generated but chose not to raise as threads. They appear nowhere in the PR's
comment list, so nothing marks them read, nothing resolves them, and they are
easy to never see at all.

They are often the sharpest ones, because the suppression is about the
reviewer's confidence, not the finding's importance.

`Get-OpenPrComments.ps1` unwraps the `<details>` block these live in, so they
land in the report under a `Suppressed comments` heading. Read that section with
the same seriousness as the inline threads.

Two things to know when acting on one:

- **There is no thread to resolve.** Suppressed comments have no `databaseId` and
  no `reviewThread`, so the reply/resolve flow in Step 3 does not apply. Address
  the finding in code, and say what you did in the PR description or a general PR
  comment instead — otherwise there is no record that it was considered.
- **They can be stale in a way threads are not.** A thread gets marked Outdated
  when its line moves; a suppressed comment never does. Check the cited code
  before assuming the concern is still live.

## Step 2: Evaluate Each Comment

**Present the whole picture before asking anything.** Write an overview in the
conversation first, one entry per comment, so the user reads every finding with
its evaluation before a single question lands. A question arriving cold asks the
user to re-derive the reviewer's point and your view of it before they can
answer; the same question after the overview is answerable in a word.

For each comment, the entry says:

1. **What the reviewer found**, in a sentence — not a paste of the comment.
2. **Whether it is valid**, and why. Reviewers can be wrong: check the cited
   line against the synced checkout, check whether a later push already
   addressed it, and check the claim against the code rather than accepting it.
   Say "not an issue — here's why" as readily as "valid".
3. **If valid, what to do about it.** Match the depth to the decision:
   - **Open-and-shut** — one obviously right fix and nothing to weigh (a missing
     guard, a stale comment, a test the rule plainly requires): state the fix
     in a line and move on. Do not manufacture alternatives for it.
   - **A real choice** — two or more fixes that lead to different code, or a
     fix with a cost worth knowing about: lay out the options with the pro and
     con of each, and say which you recommend.

Then ask. Use `AskUserQuestion` for the real choices, batched into one call;
for the open-and-shut ones and the non-issues, ask for a single go-ahead rather
than a question apiece. Wait for the answer before implementing anything — do
not silently fix a comment, and do not assume a comment is valid because a
reviewer posted it.

## Step 2b: Hunt for the siblings of every valid finding

A reviewer's finding is one instance of a class. The reviewer cited the line
it happened to notice, not every line the same reasoning applies to — and a
fix that covers only the cited line leaves the rest of the class in place,
now with a false sense that it was looked at.

So for each finding you judge valid, before the overview goes to the user,
name the class it belongs to and search the branch for other members — the
siblings belong in the overview, so the one go-ahead covers them too:

- **Name the class in one line.** Not "the overflow on line 282" but "any
  arithmetic on a field the store accepts unbounded". Not "`BirthDue` doesn't
  check its id" but "any handler that acts on an event without checking the
  event is the one the state names". Not "the check runs before the crossing"
  but "any two wake-ups that can share an instant and entity, in the order the
  kind values put them".
- **Enumerate, don't skim.** For an ordering class, list every kind that can
  land on the same tick and walk each pair. For an overflow class, list every
  multiplication and subtraction on the fields involved. For a stale-identity
  class, list every handler and ask what a duplicate or stale event does to it.
  For a hard-coded-constant class, `grep` for the literal across `Core`,
  `Harness`, `Core.Tests` and the design doc, not just the file cited.
- **Include the code the PR did not write.** The class rarely stops at the
  diff. If a reviewer finds a year hard-coded in a file the PR touched, the
  harness that has always hard-coded it is the same finding.

Report the siblings alongside the original in the overview — "Copilot found X
at A; the same class also lives at B and C; D looked like a member and is not,
because …" — and fix them under the same go-ahead. A sibling that is a
judgment call rather than open-and-shut goes to the user like any other real
choice. Each fixed sibling gets its own test written first and seen to fail;
a sibling nobody tested is a sibling nobody proved.

This is where the review pays for itself. On #71, five of the six findings
were single instances that generalised: one ordering finding led to a
same-instant walk that found the postpartum gate reading the wrong child, and
one hard-coded 365 led to two more in the harness and the soak test. None of
those were in a comment.

## Step 3: Resolve threads and keep the PR current

Don't leave handled comments open — the open-comment list should only ever show feedback you haven't dealt with yet.

- **Non-issue (after the user agrees it isn't one):** reply explaining why, then resolve the thread.
- **Fixed:** once the fix is pushed, changing the line usually makes the bot auto-mark the thread Outdated/Resolved; if a thread is still open, resolve it explicitly.

> **Run each `gh` write as its own standalone command.** The permission system matches on the
> command prefix, so a single `gh api repos/… ` call is auto-approved by the
> repo's allow rules. Wrapping calls in a `for … do … gh api … done` loop (or piping/`&&`-chaining
> them) makes the command start with `for`/another binary instead of `gh`, so it no longer matches
> the rule and gets bounced to the interactive classifier. Resolve threads one call at a time.

Threads are addressed by **comment id**, not by a thread id: the report above
shows each thread under its opening comment, and that comment's id is what both
the reply and the resolve call take. Thread state has no REST endpoint on
github.com — the report and the resolve script get it from the proxy's `ccr/`
route in a cloud session and from GraphQL everywhere else (`Get-ReviewThreads`
and `Resolve-ReviewThread` in `tools/GitHubApi.psm1`), so the commands below
are the same on either.

- **Comment ids**: read them from
  `gh api repos/zhollis21/KingdomWatch/pulls/<pr>/comments` (the `id` of a
  comment with no `in_reply_to_id` is a thread's opening comment).
- **Reply to a review comment** (one standalone call):
  ```
  gh api repos/zhollis21/KingdomWatch/pulls/<pr>/comments/<commentId>/replies -f body='<reply text>'
  ```
- **Resolve a thread** (one standalone call per thread, keyed by that same comment id):
  ```
  pwsh tools/Resolve-PrThread.ps1 -PullNumber <pr> -CommentId <commentId>
  ```
  `-Unresolve` reopens one.

**Keep the PR description up to date as you go.** Whenever the branch changes meaningfully (a fix lands, scope shifts, a new behavior is added), edit the PR body with `gh api repos/zhollis21/KingdomWatch/pulls/<num> -X PATCH -F body=@<file>` so it always reflects what's actually in the PR. Reviewers and the merge record should never read a stale description.

## Rules

- Do not silently fix comments without presenting them to the user first
- Evaluate reviewer comments independently — reviewers can be wrong
- Every valid finding is a class, not a line: search the branch for its siblings before fixing it (Step 2b)
- Skip threads already marked resolved unless the user asks to revisit them
- Resolve each thread as you finish with it (fixed or agreed non-issue), and keep the PR description current
