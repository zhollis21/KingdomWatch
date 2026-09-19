<#
.SYNOPSIS
    Fetch and export all open pull request comments to a Markdown file.

.DESCRIPTION
    Retrieves all open pull requests for KingdomWatch and produces a structured
    Markdown report containing PR overviews, review summaries, inline code
    comments (with thread resolution status), and general PR comments.
    Output is written to tools/pr-comments.md.

.PREREQUISITES
    - GitHub CLI: https://cli.github.com/ (authenticated via `gh auth login`)

.NOTES
    - Thread resolution (resolved/outdated) comes from the proxy's ccr/ route,
      which is the only way to read it from a Claude Code session: the
      equivalent is GraphQL-only on github.com, and GraphQL is refused there.
      See AGENTS.md, "Calling the GitHub API from a Claude Code cloud session".
    - Output file is UTF-8 encoded
#>

# Ensure Unicode characters in API responses render correctly
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Import-Module (Join-Path $PSScriptRoot 'GitHubApi.psm1') -Force

$OWNER = "zhollis21"
$REPO_NAME = "KingdomWatch"
$REPO = "$OWNER/$REPO_NAME"
$REPO_PATH = "repos/$REPO"
$OUT = Join-Path $PSScriptRoot "pr-comments.md"

$lines = [System.Collections.Generic.List[string]]::new()

# ---------------------------------------------------------------------------
# Fetch all open PRs
#
# REST throughout. `gh pr list|view --json` and `gh api graphql` are all
# GraphQL-backed and are refused outright from a Claude Code session, which
# used to kill this script on its very first call.
# ---------------------------------------------------------------------------
$openPRs = @(Get-Paged "$REPO_PATH/pulls?state=open")

if (-not $openPRs.Count) {
    Write-Host "No open pull requests found."
    exit 0
}

$generated = Get-Date -Format "yyyy-MM-dd h:mm tt"
$lines.Add("# Open PR Comments")
$lines.Add("*$($openPRs.Count) open pull request(s) — generated $generated*`n")

# ---------------------------------------------------------------------------
# Process each open PR
# ---------------------------------------------------------------------------
foreach ($prItem in $openPRs) {
    $PRNum = $prItem.number

    # ---- Thread resolution map ----
    # The ccr/ route is proxy-only and has no equivalent on github.com, where
    # this is GraphQL's reviewThreads. It keys threads by comment id rather than
    # by GraphQL's thread node id, which happens to be exactly what this report
    # wants: comment_ids[0] is the thread's first comment, same as the old
    # comments(first:1){databaseId}.
    $threads = @(Invoke-GhJson "$REPO_PATH/pulls/$PRNum/ccr/review_threads")

    $threadMap = @{}
    foreach ($thread in $threads) {
        $firstId = @($thread.comment_ids)[0]
        if ($null -eq $firstId) { continue }
        $threadMap[[long]$firstId] = @{
            isResolved = $thread.resolved
            isOutdated = $thread.outdated
        }
    }

    # ---- PR Overview ----
    $prData = Invoke-GhJson "$REPO_PATH/pulls/$PRNum"

    $lines.Add("---`n")
    $lines.Add("<details open>")
    $lines.Add("<summary><strong>PR #$PRNum — $($prData.title)</strong></summary>`n")
    $lines.Add("## PR #$PRNum — $($prData.title)`n")
    $lines.Add("### Overview`n")
    $lines.Add("| | |")
    $lines.Add("|---|---|")
    # REST spells these differently from the `gh pr view --json` names this used
    # to read: user/html_url/requested_reviewers/auto_merge, and a lower-case
    # state. Upper-casing keeps the report's wording unchanged.
    $lines.Add("| **State** | $($prData.state.ToUpper()) |")
    $lines.Add("| **Author** | $($prData.user.login) |")
    $lines.Add("| **URL** | $($prData.html_url) |")
    $lines.Add("| **Changes** | +$($prData.additions) / -$($prData.deletions) |")

    if ($prData.labels.Count) {
        $lines.Add("| **Labels** | $($prData.labels.name -join ', ') |")
    }
    if ($prData.assignees.Count) {
        $lines.Add("| **Assignees** | $($prData.assignees.login -join ', ') |")
    }
    if ($prData.requested_reviewers.Count) {
        $lines.Add("| **Reviewers** | $($prData.requested_reviewers.login -join ', ') |")
    }
    if ($prData.auto_merge) {
        $lines.Add("| **Auto-merge** | enabled |")
    }

    $lines.Add("")

    # ---- Review Summaries ----
    $reviews = @(Get-Paged "$REPO_PATH/pulls/$PRNum/reviews")
    $reviewLines = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $reviews) {
        $body = $r.body.Trim()
        if (-not $body) { continue }

        # Unwrap <details> rather than skipping bodies that contain it.
        #
        # A body-wide skip on any <details>|<summary>|<a href match throws away the whole
        # review — and some reviewers (e.g. Copilot) put two useful things there: the
        # headline finding above the fold, and a "Suppressed comments" list inside it.
        # Those are findings the reviewer generated but chose NOT to post as inline
        # threads, so they appear nowhere else in this report and nowhere in the PR's
        # comment list. They are frequently the sharpest ones.
        #
        # <summary> becomes a bold lead-in so the section keeps its label once the tags are gone.
        $body = $body -replace '(?s)<summary>\s*(.*?)\s*</summary>', "**`$1**`n"
        $body = $body -replace '</?details[^>]*>', ''
        $body = ($body -replace '(?m)^\s*$\n{2,}', "`n").Trim()
        if (-not $body) { continue }

        $dt = ([datetime]$r.submitted_at).ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
        $reviewLines.Add("#### $($r.user.login) — $($r.state) — $dt")
        $reviewLines.Add($body)
        $reviewLines.Add("")
    }
    if ($reviewLines.Count) {
        $lines.Add("### Review Summaries`n")
        $lines.AddRange($reviewLines)
    }

    # ---- Inline Code Comments ----
    $allComments = @(Get-Paged "$REPO_PATH/pulls/$PRNum/comments")
    $topLevel = $allComments | Where-Object { -not $_.in_reply_to_id }
    $replies = $allComments | Where-Object { $_.in_reply_to_id }

    $replyMap = @{}
    foreach ($r in $replies) {
        $parentId = [long]$r.in_reply_to_id
        if (-not $replyMap.ContainsKey($parentId)) { $replyMap[$parentId] = [System.Collections.Generic.List[object]]::new() }
        $replyMap[$parentId].Add($r)
    }

    if ($topLevel.Count) {
        $lines.Add("### Inline Code Comments`n")

        foreach ($c in $topLevel) {
            $file = $c.path
            $line = if ($c.line) { $c.line } else { $c.original_line }
            $dt = ([datetime]$c.created_at).ToLocalTime().ToString("yyyy-MM-dd h:mm tt")

            $tags = [System.Collections.Generic.List[string]]::new()
            $threadInfo = $threadMap[[long]$c.id]
            if ($threadInfo) {
                if ($threadInfo.isResolved) { $tags.Add("✅ Resolved") }
                if ($threadInfo.isOutdated) { $tags.Add("⚠️ Outdated") }
            }
            elseif (-not $c.position) {
                $tags.Add("⚠️ Outdated")
            }
            $replyList = $replyMap[[long]$c.id]
            if ($replyList) {
                $word = if ($replyList.Count -eq 1) { "reply" } else { "replies" }
                $tags.Add("💬 $($replyList.Count) $word")
            }

            $tagStr = if ($tags.Count) { " | " + ($tags -join " | ") } else { "" }

            $lines.Add("#### $($c.user.login) on ``$file`` line $line")
            $lines.Add("*$dt$tagStr*")
            $lines.Add("")
            $lines.Add($c.body.Trim())
            $lines.Add("")

            if ($replyList) {
                foreach ($r in $replyList) {
                    $rdt = ([datetime]$r.created_at).ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
                    $rbody = $r.body.Trim() -replace "`n", "`n> "
                    $lines.Add("> **↳ $($r.user.login)** — $rdt")
                    $lines.Add("> $rbody")
                    $lines.Add("")
                }
            }
        }
    }

    # ---- General PR Comments ----
    $issueComments = @(Get-Paged "$REPO_PATH/issues/$PRNum/comments")
    $generalLines = [System.Collections.Generic.List[string]]::new()
    foreach ($c in $issueComments) {
        $body = $c.body.Trim()
        if (-not $body) { continue }

        # Unwrap <details> rather than skipping bodies that contain it — same
        # reasoning as Review Summaries above. A body-wide skip throws away
        # legitimate comments containing a plain link (`<a href`) or a
        # collapsed section, not just bot-generated ones.
        $body = $body -replace '(?s)<summary>\s*(.*?)\s*</summary>', "**`$1**`n"
        $body = $body -replace '</?details[^>]*>', ''
        $body = ($body -replace '(?m)^\s*$\n{2,}', "`n").Trim()
        if (-not $body) { continue }

        $dt = ([datetime]$c.created_at).ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
        $generalLines.Add("#### $($c.user.login) — $dt")
        $generalLines.Add($body)
        $generalLines.Add("")
    }
    if ($generalLines.Count) {
        $lines.Add("### General Comments`n")
        $lines.AddRange($generalLines)
    }

    $lines.Add("</details>`n")
}

# Write all at once with UTF-8 encoding
$lines | Set-Content -Path $OUT -Encoding UTF8

Write-Host "Done — $($openPRs.Count) PR(s) written to $OUT"
