<#
.SYNOPSIS
    Shared GitHub REST helpers for the scripts in tools/.

.DESCRIPTION
    Two things about calling GitHub from a Claude Code cloud session force this
    shape, and both are silent traps rather than obvious errors, so they live in
    one place rather than being re-derived per script:

    1. GraphQL is refused. The proxy serves only a pinned set of pull-request
       operations and rejects everything else on /graphql with a 403, whatever
       credentials are supplied. That takes `gh api graphql` with it, and also
       `gh pr list|view --json` and `gh pr checks`, which are GraphQL-backed.
       Use REST, or the proxy's own `ccr/` routes where REST has no equivalent.

    2. `gh --paginate` breaks past the first page. It follows GitHub's
       `Link: rel="next"`, which points at the numeric-ID form
       (/repositories/{id}/...), and the proxy rejects that too. It fails loudly
       rather than truncating, but it fails — and only once a collection
       outgrows a single page, so it looks fine until it suddenly is not.

    See AGENTS.md, "Calling the GitHub API from a Claude Code cloud session".

.NOTES
    Import with: Import-Module (Join-Path $PSScriptRoot 'GitHubApi.psm1')
#>

function Invoke-GhJson {
    <#
      One `gh api` call, parsed. Throws with the API's own error body on failure
      rather than returning something half-formed for the caller to trip over.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    # gh prints the API's error body to stderr and exits non-zero, so stderr is
    # captured to put the reason in the exception rather than losing it. It can
    # also write to stderr on a *successful* call (deprecation and rate-limit
    # notices), and those lines would break the parse, so they are dropped by
    # type instead: merged stderr arrives as ErrorRecord, stdout as string.
    $output = & gh api -H 'Accept: application/vnd.github+json' $Path 2>&1
    if ($LASTEXITCODE) { throw "gh api $Path failed with exit code ${LASTEXITCODE}: $($output -join [Environment]::NewLine)" }
    ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join [Environment]::NewLine | ConvertFrom-Json
}

function Get-Paged {
    <#
      Every page of a collection endpoint, walking ?page=N by hand. See the
      module header for why `gh --paginate` cannot be used here.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$PageSize = 100,
        [int]$MaxPages = 50
    )

    $all = [System.Collections.Generic.List[object]]::new()
    $sep = if ($Path.Contains('?')) { '&' } else { '?' }
    $page = 1
    while ($true) {
        $batch = @(Invoke-GhJson "$Path${sep}per_page=$PageSize&page=$page")
        foreach ($item in $batch) { $all.Add($item) }
        # A short page is the last page; GitHub returns an empty one after it.
        if ($batch.Count -lt $PageSize) { break }
        # An endpoint that ignored ?page would hand back a full page forever.
        # Refusing to loop keeps that a loud failure rather than a hang.
        if ($page -ge $MaxPages) { throw "$Path returned $MaxPages full pages; raise -MaxPages or check that the endpoint honours ?page." }
        $page++
    }
    $all
}

Export-ModuleMember -Function Invoke-GhJson, Get-Paged
