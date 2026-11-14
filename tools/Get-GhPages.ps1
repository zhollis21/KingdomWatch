<#
.SYNOPSIS
    Every page of a GitHub REST collection, as one JSON array on stdout - or,
    with -Format, as one line per item, filtered without jq.

.DESCRIPTION
    The shell-side counterpart to <see cref="Get-Paged"/>, for the skills in
    .claude/skills/ that need a whole collection rather than its first hundred
    items. It exists for two reasons, both of which a hand-rolled loop gets
    wrong:

    1. `gh --paginate` cannot be used at all from a Claude Code session. It
       follows GitHub's Link header, which points at the numeric-id form
       (/repositories/{id}/...), and the proxy rejects that. See AGENTS.md.

    2. A `for p in $(seq ...)` loop in a skill is a compound command that
       starts with `for`, not `gh`, so it falls outside the command-prefix
       allow rules those skills are granted and gets bounced to the interactive
       classifier. A script does not: the command starts with `pwsh`.

    Paging stops at a short page, and a run that hits the page cap throws
    rather than returning a truncated collection quietly - a partial answer
    that looks complete is how a duplicate issue gets filed or a prior-art
    check reports nothing.

    The filtering parameters exist because jq is not installed everywhere the
    skills run (it is absent from the Windows dev machine), and `gh api --jq`
    only filters one response, not a collection assembled from several pages.

.PARAMETER Path
    The repo-scoped API path, with any query string but without per_page or
    page: `repos/zhollis21/KingdomWatch/issues?state=all`.

.PARAMETER Contains
    Keep only items whose title and body, joined, contain this text. Matched
    literally and case-insensitively - never as a regex, so `Found(` or `a.b`
    mean exactly what they say. Items with neither field (labels) never match.

.PARAMETER IssuesOnly
    Drop pull requests, which the issues endpoint returns alongside issues.

.PARAMETER Format
    Print one string per item instead of JSON. `{field}` and `{a.b}` are
    replaced by that property's value; a property that is an array of objects
    (`{labels.name}`) is joined with spaces, and null prints as nothing. `\n`
    is a newline. A field the item does not have throws rather than printing
    blank, so a typo cannot pass for an empty value.

.PARAMETER First
    Stop after this many items (after filtering).

.EXAMPLE
    pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues?state=all' -IssuesOnly -Contains 'notification' -Format '#{number} [{state}] {title}' -First 8

.EXAMPLE
    pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues/42/comments' -Format '--- comment by {user.login} @ {created_at} ---\n{body}'

.PREREQUISITES
    - GitHub CLI: https://cli.github.com/ (authenticated via `gh auth login`,
      or GH_TOKEN in CI)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string]$Path,
    [string]$Contains,
    [switch]$IssuesOnly,
    [string]$Format,
    [ValidateRange(1, [int]::MaxValue)][int]$First
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Import-Module (Join-Path $PSScriptRoot 'GitHubApi.psm1') -Force

function Get-FieldValue([object]$Item, [string]$FieldPath) {
    $current = $Item
    foreach ($name in $FieldPath.Split('.')) {
        if ($null -eq $current) { return '' }
        $targets = @($current)
        foreach ($t in $targets) {
            if ($null -ne $t -and -not $t.PSObject.Properties[$name]) {
                throw "-Format names '{$FieldPath}', but an item from $Path has no '$name'."
            }
        }
        $current = if ($current -is [array]) { @($current | ForEach-Object { $_.$name }) } else { $current.$name }
    }
    if ($current -is [array]) { return ($current | Where-Object { $null -ne $_ }) -join ' ' }
    if ($null -eq $current) { return '' }
    # ConvertFrom-Json turns ISO timestamps into DateTime, which would print in
    # the machine's locale; print them back the way GitHub sent them.
    if ($current -is [datetime]) { return $current.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    "$current"
}

$items = @(Get-Paged $Path)

if ($IssuesOnly) {
    $items = @($items | Where-Object { -not $_.PSObject.Properties['pull_request'] -or $null -eq $_.pull_request })
}
if ($PSBoundParameters.ContainsKey('Contains')) {
    $items = @($items | Where-Object {
        $text = "$($_.title) $($_.body)"
        $text.IndexOf($Contains, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
}
if ($First) { $items = @($items | Select-Object -First $First) }

if (-not $Format) {
    # -Depth is load-bearing: ConvertTo-Json flattens past two levels by default,
    # which would turn a nested object - an issue's labels, a blocker's repository -
    # into a type name and lose exactly the fields a caller filters on.
    # -InputObject, not the pipeline: an empty pipeline prints nothing at all,
    # and a filter that matched nothing must still print a parseable [].
    ConvertTo-Json -InputObject $items -Depth 24
    return
}

$template = $Format.Replace('\n', "`n")
foreach ($item in $items) {
    [regex]::Replace($template, '\{([A-Za-z0-9_.]+)\}', { param($m) Get-FieldValue $item $m.Groups[1].Value })
}
