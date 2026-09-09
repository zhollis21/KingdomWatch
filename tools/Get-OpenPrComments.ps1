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
    - GraphQL is used to resolve review thread status (resolved/outdated)
    - Output file is UTF-8 encoded
#>

# Ensure Unicode characters in API responses render correctly
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$OWNER = "zhollis21"
$REPO_NAME = "KingdomWatch"
$REPO = "$OWNER/$REPO_NAME"
$OUT = Join-Path $PSScriptRoot "pr-comments.md"

$lines = [System.Collections.Generic.List[string]]::new()

# ---------------------------------------------------------------------------
# Fetch all open PRs
# ---------------------------------------------------------------------------
$openPRs = gh pr list --repo $REPO --state open --limit 200 --json 'number,title' | ConvertFrom-Json

if (-not $openPRs.Count) {
    Write-Host "No open pull requests found."
    exit 0
}

$generated = Get-Date -Format "yyyy-MM-dd h:mm tt"
$lines.Add("# Open PR Comments")
$lines.Add("*$($openPRs.Count) open pull request(s) — generated $generated*`n")

# GraphQL query for thread resolution (reused per PR)
$gql = 'query($owner:String!,$repo:String!,$pr:Int!){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100){nodes{isResolved isOutdated comments(first:1){nodes{databaseId}}}}}}}'

# ---------------------------------------------------------------------------
# Process each open PR
# ---------------------------------------------------------------------------
foreach ($prItem in $openPRs) {
    $PRNum = $prItem.number

    # ---- Thread resolution map ----
    $threadResult = gh api graphql `
        -f query=$gql `
        -f owner=$OWNER `
        -f repo=$REPO_NAME `
        -F pr=$PRNum | ConvertFrom-Json

    $threadMap = @{}
    foreach ($thread in $threadResult.data.repository.pullRequest.reviewThreads.nodes) {
        $firstId = $thread.comments.nodes[0].databaseId
        $threadMap[$firstId] = @{
            isResolved = $thread.isResolved
            isOutdated = $thread.isOutdated
        }
    }

    # ---- PR Overview ----
    $prData = gh pr view $PRNum --repo $REPO --json 'number,title,state,author,url,additions,deletions,labels,assignees,reviewRequests,autoMergeRequest' | ConvertFrom-Json

    $lines.Add("---`n")
    $lines.Add("<details open>")
    $lines.Add("<summary><strong>PR #$PRNum — $($prData.title)</strong></summary>`n")
    $lines.Add("## PR #$PRNum — $($prData.title)`n")
    $lines.Add("### Overview`n")
    $lines.Add("| | |")
    $lines.Add("|---|---|")
    $lines.Add("| **State** | $($prData.state) |")
    $lines.Add("| **Author** | $($prData.author.login) |")
    $lines.Add("| **URL** | $($prData.url) |")
    $lines.Add("| **Changes** | +$($prData.additions) / -$($prData.deletions) |")

    if ($prData.labels.Count) {
        $lines.Add("| **Labels** | $($prData.labels.name -join ', ') |")
    }
    if ($prData.assignees.Count) {
        $lines.Add("| **Assignees** | $($prData.assignees.login -join ', ') |")
    }
    if ($prData.reviewRequests.Count) {
        $lines.Add("| **Reviewers** | $($prData.reviewRequests.login -join ', ') |")
    }
    if ($prData.autoMergeRequest) {
        $lines.Add("| **Auto-merge** | enabled |")
    }

    $lines.Add("")

    # ---- Review Summaries ----
    $reviews = gh api "repos/$REPO/pulls/$PRNum/reviews" --paginate | ConvertFrom-Json
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
    $allComments = gh api "repos/$REPO/pulls/$PRNum/comments" --paginate | ConvertFrom-Json
    $topLevel = $allComments | Where-Object { -not $_.in_reply_to_id }
    $replies = $allComments | Where-Object { $_.in_reply_to_id }

    $replyMap = @{}
    foreach ($r in $replies) {
        $parentId = $r.in_reply_to_id
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
            $threadInfo = $threadMap[$c.id]
            if ($threadInfo) {
                if ($threadInfo.isResolved) { $tags.Add("✅ Resolved") }
                if ($threadInfo.isOutdated) { $tags.Add("⚠️ Outdated") }
            }
            elseif (-not $c.position) {
                $tags.Add("⚠️ Outdated")
            }
            $replyList = $replyMap[$c.id]
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
    $issueComments = gh api "repos/$REPO/issues/$PRNum/comments" --paginate | ConvertFrom-Json
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
