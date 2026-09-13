---
name: pr-feedback
description: "Pull open PR review comments for this repository, present an overview of each finding with whether it is valid and what the options are, then act on what the user chooses. Use when asked to address PR feedback, review open comments, or work through reviewer notes."
allowed-tools: Bash(pwsh tools/Get-OpenPrComments.ps1) Bash(gh pr edit:*) Bash(gh pr view:*) Bash(gh pr comment:*) Bash(gh api graphql:*) Bash(gh api repos/:*) Read Glob Grep
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
gh pr view <pr> --json headRefName,headRefOid,baseRefName
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

## Step 3: Resolve threads and keep the PR current

Don't leave handled comments open — the open-comment list should only ever show feedback you haven't dealt with yet.

- **Non-issue (after the user agrees it isn't one):** reply explaining why, then resolve the thread.
- **Fixed:** once the fix is pushed, changing the line usually makes the bot auto-mark the thread Outdated/Resolved; if a thread is still open, resolve it explicitly.

> **Run each `gh` write as its own standalone command.** The permission system matches on the
> command prefix, so a single `gh api graphql …` / `gh pr comment …` call is auto-approved by the
> repo's allow rules. Wrapping calls in a `for … do … gh api … done` loop (or piping/`&&`-chaining
> them) makes the command start with `for`/another binary instead of `gh`, so it no longer matches
> the rule and gets bounced to the interactive classifier. Resolve threads one call at a time.

- **List unresolved threads** (get the thread ids and the comment id to reply to):
  ```
  gh api graphql -f query='query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){reviewThreads(first:100){nodes{id isResolved isOutdated comments(first:1){nodes{databaseId path body}}}}}}}' -F o=zhollis21 -F r=KingdomWatch -F n=<pr>
  ```
- **Reply to a review comment** (one standalone call; `<commentId>` is the `databaseId` above):
  ```
  gh api repos/zhollis21/KingdomWatch/pulls/<pr>/comments/<commentId>/replies -f body='<reply text>'
  ```
- **Resolve a thread** (one standalone call per thread id — `gh` has no direct command, so use the GraphQL mutation):
  ```
  gh api graphql -f query='mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{isResolved}}}' -F id=<threadId>
  ```

**Keep the PR description up to date as you go.** Whenever the branch changes meaningfully (a fix lands, scope shifts, a new behavior is added), edit the PR body with `gh pr edit <num> --body ...` so it always reflects what's actually in the PR. Reviewers and the merge record should never read a stale description.

## Rules

- Do not silently fix comments without presenting them to the user first
- Evaluate reviewer comments independently — reviewers can be wrong
- Skip threads already marked resolved unless the user asks to revisit them
- Resolve each thread as you finish with it (fixed or agreed non-issue), and keep the PR description current
- No AI attribution in replies, resolutions, or the PR description — see `AGENTS.md`
