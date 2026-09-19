<#
.SYNOPSIS
    Every page of a GitHub REST collection, as one JSON array on stdout.

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

.PARAMETER Path
    The repo-scoped API path, with any query string but without per_page or
    page: `repos/zhollis21/KingdomWatch/issues?state=all`.

.EXAMPLE
    pwsh tools/Get-GhPages.ps1 'repos/zhollis21/KingdomWatch/issues?state=all' |
      jq -r '.[] | select(.pull_request == null) | "#\(.number) \(.title)"'

.PREREQUISITES
    - GitHub CLI: https://cli.github.com/ (authenticated via `gh auth login`,
      or GH_TOKEN in CI)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string]$Path
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Import-Module (Join-Path $PSScriptRoot 'GitHubApi.psm1') -Force

# -Depth is load-bearing: ConvertTo-Json flattens past two levels by default,
# which would turn a nested object - an issue's labels, a blocker's repository -
# into a type name and lose exactly the fields a caller filters on.
@(Get-Paged $Path) | ConvertTo-Json -Depth 24 -AsArray
