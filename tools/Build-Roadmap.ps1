<#
.SYNOPSIS
    Regenerate docs/roadmap/ from GitHub issues, milestones and relationships.

.DESCRIPTION
    Pulls every issue with its milestone, labels, native "blocked by"
    relationships, then writes:

      docs/roadmap/graph.json   the graph, for tools and agents
      docs/roadmap/README.md    ready-to-pick-up list + milestone overview
      docs/roadmap/next.md      the current milestone and the one after it
      docs/roadmap/M<n>.md      one chart per milestone
      docs/roadmap/index.html   a static viewer for the above (tools/roadmap-index.html)

    The output is git-ignored. The roadmap workflow publishes it to GitHub
    Pages at https://zhollis21.github.io/KingdomWatch/ ; locally, run this and
    open the markdown, or read graph.json directly.

    Readiness is derived, never stored: an open issue is ready when every
    issue it is blocked by is closed. Cross-milestone edges are drawn as
    dashed stub nodes so each chart stays readable on its own.

    The only edges are GitHub's native "blocked by" relationships (the issue
    sidebar, or `gh api .../issues/N/dependencies/blocked_by`).

    Prose such as "Depends on #4" is deliberately NOT parsed: the bodies also
    say things like "open question #7", which is a design-doc reference.

.PARAMETER OutDir
    Where to write. Defaults to docs/roadmap next to this script's parent.

.PARAMETER SyncLabels
    Also add or remove the `blocked` label on open issues so it matches the
    derived state. Off by default so a local run never writes to GitHub;
    the roadmap workflow passes it.

.PREREQUISITES
    - GitHub CLI: https://cli.github.com/ (authenticated via `gh auth login`,
      or GH_TOKEN in CI)

.NOTES
    The workflow (.github/workflows/roadmap.yml) runs this on issue and
    milestone events and nightly, then deploys the output to Pages. Nothing
    is committed, so no branch rule is involved. Relationship edits do not
    fire an event, so after re-wiring blocked-by links either wait for the
    nightly run or `gh workflow run roadmap.yml`.
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..' 'docs' 'roadmap'),
    [switch]$SyncLabels
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$OWNER = 'zhollis21'
$REPO_NAME = 'KingdomWatch'
$REPO_URL = "https://github.com/$OWNER/$REPO_NAME"

# Fill colours are state — done, ready, blocked — because that is the question
# the chart answers; priority is a label prefix (P0–P3) and, on the site, border weight.
$PRIORITIES = @('P0-blocker', 'P1-high', 'P2-normal', 'P3-someday')
$CLASSDEFS = @(
    'classDef done fill:#2da44e,color:#fff,stroke:#1a7f37'
    'classDef ready fill:#ddf4ff,color:#000,stroke:#0969da,stroke-width:2px'
    'classDef blocked fill:#eaeef2,color:#000,stroke:#8c959f'
    'classDef plain fill:#fff,color:#000,stroke:#999'
)
# Stubs for issues outside the chart keep their state fill and go dashed.
$EXT_STYLE = 'stroke-dasharray:4 4'

# ---------------------------------------------------------------------------
# Fetch
# ---------------------------------------------------------------------------
$gql = @'
query($owner:String!, $name:String!, $after:String) {
  repository(owner:$owner, name:$name) {
    milestones(first:50, states:[OPEN, CLOSED]) {
      pageInfo { hasNextPage }
      nodes { number title state description }
    }
    issues(first:100, after:$after, states:[OPEN, CLOSED],
           orderBy:{field:CREATED_AT, direction:ASC}) {
      pageInfo { hasNextPage endCursor }
      nodes {
        number title state url
        labels(first:20) { pageInfo { hasNextPage } nodes { name } }
        milestone { number }
        blockedBy(first:50) { pageInfo { hasNextPage } nodes { number repository { nameWithOwner } } }
      }
    }
  }
}
'@

$milestoneNodes = $null
$issueNodes = [System.Collections.Generic.List[object]]::new()
$after = $null
do {
    $ghArgs = @('api', 'graphql', '-f', "query=$gql", '-f', "owner=$OWNER", '-f', "name=$REPO_NAME")
    if ($after) { $ghArgs += @('-f', "after=$after") }
    $json = & gh @ghArgs
    if ($LASTEXITCODE) { throw "gh api graphql failed with exit code $LASTEXITCODE." }
    $page = ($json | ConvertFrom-Json).data.repository
    if ($page.milestones.pageInfo.hasNextPage) { throw "More than 50 milestones; raise the page size in the query." }
    if (-not $milestoneNodes) { $milestoneNodes = $page.milestones.nodes }
    foreach ($n in $page.issues.nodes) { $issueNodes.Add($n) }
    $after = $page.issues.pageInfo.endCursor
} while ($page.issues.pageInfo.hasNextPage)

if ($issueNodes.Count -eq 0) { throw 'No issues fetched; refusing to write an empty roadmap.' }
# The sub-connections are capped rather than paginated; an issue past a cap
# would silently lose labels or blockers, and a wrong "ready" is worse than no roadmap.
foreach ($n in $issueNodes) {
    if ($n.labels.pageInfo.hasNextPage) { throw "#$($n.number) has more than 20 labels; raise the page size in the query." }
    if ($n.blockedBy.pageInfo.hasNextPage) { throw "#$($n.number) is blocked by more than 50 issues; raise the page size in the query." }
}

# ---------------------------------------------------------------------------
# Build the graph
# ---------------------------------------------------------------------------
function Get-MilestoneOrder([string]$title) {
    # "M1 — Headless sim" sorts by 1, not by the string, so M10 lands after M9.
    if ($title -match '^M(\d+)') { [int]$Matches[1] } else { [int]::MaxValue }
}

$milestones = @($milestoneNodes | Sort-Object { Get-MilestoneOrder $_.title }, title | ForEach-Object {
    [ordered]@{
        number = [int]$_.number; title = $_.title; state = $_.state.ToLower()
        description = $_.description; issues = [System.Collections.Generic.List[int]]::new()
    }
})
$milestoneByNumber = [System.Collections.Generic.Dictionary[int, object]]::new()
foreach ($m in $milestones) { $milestoneByNumber[$m.number] = $m }

$issues = [System.Collections.Generic.Dictionary[int, object]]::new()
foreach ($n in $issueNodes) {
    $labels = @($n.labels.nodes.name)
    $number = [int]$n.number
    $priority = @($labels | Where-Object { $PRIORITIES -contains $_ })[0]
    $issues[$number] = [ordered]@{
        number = $number
        title = $n.title
        state = $n.state.ToLower()
        url = $n.url
        milestone = if ($n.milestone) { [int]$n.milestone.number } else { $null }
        priority = $priority
        labels = $labels
        blockedBy = @($n.blockedBy.nodes | Where-Object { $_.repository.nameWithOwner -eq "$OWNER/$REPO_NAME" } | ForEach-Object { [int]$_.number } | Sort-Object)
        foreignBlockers = @($n.blockedBy.nodes | Where-Object { $_.repository.nameWithOwner -ne "$OWNER/$REPO_NAME" } | ForEach-Object { "$($_.repository.nameWithOwner)#$($_.number)" })
        blocking = [System.Collections.Generic.List[int]]::new()
        openBlockers = @()
        ready = $false
        blocked = $false
    }
    if ($n.milestone -and $milestoneByNumber.ContainsKey([int]$n.milestone.number)) {
        $milestoneByNumber[[int]$n.milestone.number].issues.Add($number)
    }
}

$warnings = [System.Collections.Generic.List[string]]::new()

foreach ($i in $issues.Values) {
    foreach ($b in $i.blockedBy) {
        if ($issues.ContainsKey($b)) { $issues[$b].blocking.Add($i.number) }
        else { $warnings.Add("#$($i.number) is blocked by #$b, which was not fetched.") }
    }
    foreach ($x in $i.foreignBlockers) { $warnings.Add("#$($i.number) is blocked by $x in another repository; the roadmap ignores it.") }
    if (-not $i.milestone) { $warnings.Add("#$($i.number) has no milestone.") }
    if ($i.state -eq 'open' -and -not $i.priority) { $warnings.Add("#$($i.number) has no priority label.") }
}

foreach ($i in $issues.Values) {
    # A blocker that was not fetched counts as open: better a false "blocked" than a false "ready".
    $i.openBlockers = @($i.blockedBy | Where-Object { -not $issues.ContainsKey($_) -or $issues[$_].state -eq 'open' })
    $i.blocked = ($i.state -eq 'open') -and ($i.openBlockers.Count -gt 0)
    $i.ready = ($i.state -eq 'open') -and ($i.openBlockers.Count -eq 0)
    $i.blocking = @($i.blocking | Sort-Object)
}

# Cycle detection over blocked-by. A cycle makes readiness meaningless for
# its members, so report it loudly rather than silently marking them blocked.
$colour = [System.Collections.Generic.Dictionary[int, int]]::new()
$stack = [System.Collections.Generic.List[int]]::new()
function Find-Cycle([int]$n) {
    $colour[$n] = 1; $stack.Add($n)
    foreach ($b in $issues[$n].blockedBy) {
        if (-not $issues.ContainsKey($b)) { continue }
        if (-not $colour.ContainsKey($b)) { Find-Cycle $b }
        elseif ($colour[$b] -eq 1) {
            $cycle = @($stack[$stack.IndexOf($b)..($stack.Count - 1)] | ForEach-Object { "#$_" }) + "#$b"
            $warnings.Add("Dependency cycle: " + ($cycle -join ' -> '))
        }
    }
    $colour[$n] = 2; $stack.RemoveAt($stack.Count - 1)
}
foreach ($n in $issues.Keys) { if (-not $colour.ContainsKey($n)) { Find-Cycle $n } }

foreach ($w in $warnings) { Write-Warning $w }

# ---------------------------------------------------------------------------
# Label sync — before the write, and mirrored into memory, because a label
# change made with the workflow token fires no event to regenerate again.
# ---------------------------------------------------------------------------
if ($SyncLabels) {
    $changed = 0
    foreach ($i in $issues.Values) {
        if ($i.state -ne 'open') { continue }
        $has = $i.labels -contains 'blocked'
        if ($i.blocked -and -not $has) {
            gh issue edit $i.number --repo "$OWNER/$REPO_NAME" --add-label blocked | Out-Null
            if ($LASTEXITCODE) { throw "Adding the blocked label to #$($i.number) failed with exit code $LASTEXITCODE." }
            $i.labels = @($i.labels) + 'blocked'; $changed++
        }
        elseif (-not $i.blocked -and $has) {
            gh issue edit $i.number --repo "$OWNER/$REPO_NAME" --remove-label blocked | Out-Null
            if ($LASTEXITCODE) { throw "Removing the blocked label from #$($i.number) failed with exit code $LASTEXITCODE." }
            $i.labels = @($i.labels | Where-Object { $_ -ne 'blocked' }); $changed++
        }
    }
    Write-Host "Label sync: $changed issue(s) changed."
}

# ---------------------------------------------------------------------------
# Derived views
# ---------------------------------------------------------------------------
function Get-PriorityRank($p) { $r = $PRIORITIES.IndexOf($p); if ($r -lt 0) { 99 } else { $r } }
function Get-MilestoneRank($i) { if ($i.milestone) { $milestones.IndexOf($milestoneByNumber[$i.milestone]) } else { 999 } }

$ready = @($issues.Values | Where-Object ready | Sort-Object { Get-MilestoneRank $_ }, { Get-PriorityRank $_.priority }, number)
$current = $milestones | Where-Object { @($_.issues | Where-Object { $issues[$_].state -eq 'open' }).Count -gt 0 } | Select-Object -First 1
$next = if ($current) { $milestones[$milestones.IndexOf($current) + 1] } else { $null }

# ---------------------------------------------------------------------------
# Mermaid
# ---------------------------------------------------------------------------
function Format-Label([object]$i) {
    # Wrap long titles; mermaid renders <br/> inside quoted labels, so the
    # title is escaped before the breaks go in.
    $safeTitle = $i.title -replace '&', '#amp;' -replace '"', '#quot;' -replace '<', '#lt;' -replace '>', '#gt;'
    $words = $safeTitle -split '\s+'
    $lines = [System.Collections.Generic.List[string]]::new(); $line = ''
    foreach ($w in $words) {
        if ($line.Length -gt 0 -and ($line.Length + 1 + $w.Length) -gt 28) { $lines.Add($line); $line = $w }
        else { $line = if ($line) { "$line $w" } else { $w } }
    }
    if ($line) { $lines.Add($line) }
    $text = $lines -join '<br/>'
    $tick = if ($i.state -eq 'closed') { '✓ ' } else { '' }
    $prio = if ($i.priority) { ($i.priority -split '-')[0] + ' · ' } else { '' }
    "$prio$tick#$($i.number) $text"
}

function Get-NodeClass([object]$i) {
    if ($i.state -eq 'closed') { 'done' } elseif ($i.ready) { 'ready' } else { 'blocked' }
}

function Write-Chart {
    <#
      Emits a flowchart for a set of "groups" (milestone title -> issue
      numbers). Issues outside every group that an in-scope edge touches are
      drawn once as dashed stubs, labelled with their milestone — but only
      what the group waits on. What it unblocks elsewhere is
      left to the tables: #16 alone fans out to nine later issues, and
      drawing that spreads the chart until nothing is readable.
    #>
    param([System.Collections.Specialized.OrderedDictionary]$Groups, [string]$Direction = 'TD')
    $out = [System.Collections.Generic.List[string]]::new()
    $out.Add('```mermaid')
    $out.Add("flowchart $Direction")
    $inScope = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($g in $Groups.Values) { foreach ($n in $g) { [void]$inScope.Add([int]$n) } }

    foreach ($title in $Groups.Keys) {
        $safe = ($title -replace '"', '#quot;')
        $id = 'MS' + ($title -replace '[^A-Za-z0-9]', '')
        $out.Add("  subgraph $id[`"$safe`"]")
        $out.Add("    direction $Direction")
        $members = @($Groups[$title] | Sort-Object)
        foreach ($n in $members) { $out.Add("    I$n[`"$(Format-Label $issues[$n])`"]:::$(Get-NodeClass $issues[$n])") }
        $out.Add('  end')
    }

    $edges = [System.Collections.Generic.List[string]]::new()
    $stubs = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($n in $inScope) {
        $i = $issues[$n]
        foreach ($b in $i.blockedBy) {
            if (-not $issues.ContainsKey($b)) { continue }
            if (-not $inScope.Contains($b)) { [void]$stubs.Add($b) }
            $edges.Add("  I$b --> I$n")
        }
    }
    foreach ($s in ($stubs | Sort-Object)) {
        $i = $issues[$s]
        $ms = if ($i.milestone) { ($milestoneByNumber[$i.milestone].title -split ' ')[0] } else { 'unscheduled' }
        $out.Add("  I$s[`"$(Format-Label $i)<br/><i>$ms</i>`"]:::$(Get-NodeClass $i)")
    }
    foreach ($e in $edges) { $out.Add($e) }
    foreach ($c in $CLASSDEFS) { $out.Add("  $c") }
    foreach ($s in ($stubs | Sort-Object)) { $out.Add("  style I$s $EXT_STYLE") }
    $out.Add('```')
    return $out
}


function Format-IssueLink($n) { if ($issues.ContainsKey($n)) { "[#$n]($($issues[$n].url))" } else { "#$n" } }
function Format-Title($i) { $i.title -replace '\|', '\|' }   # a pipe in a title would split the table cell
function Format-Refs($list) {
    # Closed issues get a tick so a "blocked by" cell shows which blockers still matter.
    if (-not $list.Count) { return "—" }
    ($list | ForEach-Object { if ($issues.ContainsKey($_) -and $issues[$_].state -eq "closed") { "✓$(Format-IssueLink $_)" } else { Format-IssueLink $_ } }) -join ", "
}

function Write-IssueTable([int[]]$Numbers) {
    $out = [System.Collections.Generic.List[string]]::new()
    $out.Add('| Issue | Title | Priority | Status | Blocked by | Blocks |')
    $out.Add('|---|---|---|---|---|---|')
    foreach ($n in ($Numbers | Sort-Object { Get-PriorityRank $issues[$_].priority }, { $_ })) {
        $i = $issues[$n]
        $status = if ($i.state -eq 'closed') { '✓ done' } elseif ($i.ready) { '**ready**' } else { 'blocked' }
        $out.Add("| $(Format-IssueLink $n) | $(Format-Title $i) | $($i.priority ?? '—') | $status | $(Format-Refs $i.blockedBy) | $(Format-Refs $i.blocking) |")
    }
    return $out
}

$legend = @(
    '```mermaid'
    'flowchart LR'
    '  L0["✓ done"]:::done --> L1["ready to pick up"]:::ready --> L2["blocked"]:::blocked'
    '  L3["other milestone<br/><i>M9</i>"]:::blocked'
    '  L5["solid: blocked by"]:::plain --> L6[" "]:::plain'
) + ($CLASSDEFS | ForEach-Object { "  $_" }) + @("  style L3 $EXT_STYLE", '```')

$stamp = "_Generated by ``tools/Build-Roadmap.ps1`` from GitHub issues. Do not edit by hand._"

# ---------------------------------------------------------------------------
# Write
# ---------------------------------------------------------------------------
$OutDir = (New-Item -ItemType Directory -Force $OutDir).FullName   # absolute: [IO.File] resolves relative paths against the process CWD, not the PowerShell location
function Save([string]$name, [string[]]$lines) {
    [IO.File]::WriteAllText((Join-Path $OutDir $name), (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

# graph.json
$graph = [ordered]@{
    repository = "$OWNER/$REPO_NAME"
    currentMilestone = if ($current) { $current.number } else { $null }
    ready = @($ready.number)
    warnings = @($warnings)
    milestones = @($milestones | ForEach-Object {
        [ordered]@{
            number = $_.number; title = $_.title; state = $_.state
            open = @($_.issues | Where-Object { $issues[$_].state -eq 'open' }).Count
            closed = @($_.issues | Where-Object { $issues[$_].state -eq 'closed' }).Count
            issues = @($_.issues | Sort-Object)
        }
    })
    issues = @($issues.Keys | Sort-Object | ForEach-Object { $issues[$_] })
}
Save 'graph.json' @(($graph | ConvertTo-Json -Depth 6))

# README.md — the front page
$md = [System.Collections.Generic.List[string]]::new()
$md.Add('# Roadmap')
$md.Add('')
$md.Add($stamp)
$md.Add('')
$md.Add("Every issue, in its milestone, with what it waits on. Arrows are GitHub's native *blocked by* relationships. An open issue is **ready** when everything it is blocked by is closed. See [next.md](next.md) for the two milestones that matter right now, or a milestone page for the full chart.")
$md.Add('')
$md.Add('## Ready to pick up')
$md.Add('')
if ($ready.Count) {
    $md.Add('| Issue | Title | Milestone | Priority | Unblocks |')
    $md.Add('|---|---|---|---|---|')
    foreach ($i in $ready) {
        $ms = if ($i.milestone) { $milestoneByNumber[$i.milestone].title } else { '—' }
        $md.Add("| $(Format-IssueLink $i.number) | $(Format-Title $i) | $ms | $($i.priority ?? '—') | $(Format-Refs $i.blocking) |")
    }
} else { $md.Add('Nothing is ready: every open issue is blocked. Check the warnings below for a cycle.') }
$md.Add('')
$md.Add('## Milestones')
$md.Add('')
$md.Add('```mermaid')
$md.Add('flowchart LR')
$prev = $null
foreach ($m in $milestones) {
    $open = @($m.issues | Where-Object { $issues[$_].state -eq 'open' }).Count
    $closed = @($m.issues | Where-Object { $issues[$_].state -eq 'closed' }).Count
    $id = 'MS' + (Get-MilestoneOrder $m.title)
    $label = "$($m.title -replace '"', '#quot;')<br/>$closed done · $open open"
    $class = if ($m.issues.Count -gt 0 -and $open -eq 0) { 'done' } elseif ($m -eq $current) { 'ready' } else { 'plain' }
    $md.Add("  $id[`"$label`"]:::$class")
    if ($prev) { $md.Add("  $prev --> $id") }
    $prev = $id
}
foreach ($c in $CLASSDEFS) { $md.Add("  $c") }
$md.Add('```')
$md.Add('')
$md.Add('| Milestone | Done | Open | Ready | Chart |')
$md.Add('|---|---|---|---|---|')
foreach ($m in $milestones) {
    $open = @($m.issues | Where-Object { $issues[$_].state -eq 'open' }).Count
    $closed = @($m.issues | Where-Object { $issues[$_].state -eq 'closed' }).Count
    $rdy = @($m.issues | Where-Object { $issues[$_].ready }).Count
    $md.Add("| [$($m.title)]($REPO_URL/milestone/$($m.number)) | $closed | $open | $rdy | [M$(Get-MilestoneOrder $m.title).md](M$(Get-MilestoneOrder $m.title).md) |")
}
$unscheduled = @($issues.Values | Where-Object { -not $_.milestone })
if ($unscheduled.Count) {
    $md.Add('')
    $md.Add("**Unscheduled:** $(Format-Refs $unscheduled.number)")
}
$md.Add('')
$md.Add('## Legend')
$md.Add('')
foreach ($l in $legend) { $md.Add($l) }
$md.Add('')
$md.Add('## Everything')
$md.Add('')
$md.Add('[The whole picture](all.md) — every milestone on one chart.')
if ($warnings.Count) {
    $md.Add('')
    $md.Add('## Warnings')
    $md.Add('')
    foreach ($w in $warnings) { $md.Add("- $w") }
}
Save 'README.md' $md

# The static viewer: fetches the markdown above and renders it with mermaid.
Copy-Item (Join-Path $PSScriptRoot 'roadmap-index.html') (Join-Path $OutDir 'index.html') -Force

# next.md — current + next milestone
$md = [System.Collections.Generic.List[string]]::new()
$md.Add('# What is next')
$md.Add('')
$md.Add($stamp)
$md.Add('')
if ($current) {
    $groups = [ordered]@{ $current.title = @($current.issues) }
    if ($next) { $groups[$next.title] = @($next.issues) }
    $md.Add("The earliest milestone with open work is **$($current.title)**" + $(if ($next) { ", followed by **$($next.title)**." } else { '.' }) + ' Issues from other milestones that these wait on appear as dashed stubs; what they unblock is in the tables. Green = done, blue = ready to pick up, grey = blocked; the P-number is the priority label.')
    $md.Add('')
    foreach ($l in (Write-Chart $groups 'TD')) { $md.Add($l) }
    foreach ($title in $groups.Keys) {
        $md.Add('')
        $md.Add("## $title")
        $md.Add('')
        foreach ($l in (Write-IssueTable $groups[$title])) { $md.Add($l) }
    }
} else {
    $md.Add('No milestone has open work.')
    $open = @($unscheduled | Where-Object { $_.state -eq 'open' })
    if ($open.Count) { $md.Add(''); $md.Add("Open issues with no milestone: $(Format-Refs $open.number)") }
}
Save 'next.md' $md

# all.md — every milestone on one chart
$md = [System.Collections.Generic.List[string]]::new()
$md.Add('# The whole picture')
$md.Add('')
$md.Add($stamp)
$md.Add('')
$md.Add('Every milestone, every issue, every dependency. Green = done, blue = ready to pick up, grey = blocked; the P-number is the priority label. Milestone pages have the tables.')
$md.Add('')
$groups = [ordered]@{}
foreach ($m in $milestones) { if ($m.issues.Count) { $groups[$m.title] = @($m.issues) } }
if ($groups.Count) { foreach ($l in (Write-Chart $groups 'TD')) { $md.Add($l) } } else { $md.Add('No issues yet.') }
Save 'all.md' $md

# M<n>.md — one per milestone
foreach ($m in $milestones) {
    $md = [System.Collections.Generic.List[string]]::new()
    $md.Add("# $($m.title)")
    $md.Add('')
    $md.Add($stamp)
    $md.Add('')
    if ($m.description) { $md.Add($m.description); $md.Add('') }
    $md.Add("[Milestone on GitHub]($REPO_URL/milestone/$($m.number)) · [Roadmap](README.md)")
    $md.Add('')
    if ($m.issues.Count) {
        foreach ($l in (Write-Chart ([ordered]@{ $m.title = @($m.issues) }) 'TD')) { $md.Add($l) }
        $md.Add('')
        foreach ($l in (Write-IssueTable $m.issues)) { $md.Add($l) }
    } else { $md.Add('No issues yet.') }
    Save "M$(Get-MilestoneOrder $m.title).md" $md
}

# Remove pages for milestones that no longer exist.
$keep = @('README.md', 'next.md', 'all.md', 'graph.json', 'index.html') + @($milestones | ForEach-Object { "M$(Get-MilestoneOrder $_.title).md" })
Get-ChildItem $OutDir -File | Where-Object { $_.Name -match "^M[0-9]+[.]md$" -and $keep -notcontains $_.Name } | Remove-Item

Write-Host "Wrote $($keep.Count) files to $OutDir ($($issues.Count) issues, $($ready.Count) ready, $($warnings.Count) warnings)."
