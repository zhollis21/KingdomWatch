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
        # GitHub caps per_page at 100 and silently serves 100 for anything
        # larger. Without this, -PageSize 1000 would fetch 100, read that as a
        # short page, and stop one page into the collection - losing the rest
        # and reporting success. Refused rather than clamped, because a caller
        # who asked for 1000 has a wrong idea of the page size either way.
        [ValidateRange(1, 100)][int]$PageSize = 100,
        [ValidateRange(1, [int]::MaxValue)][int]$MaxPages = 50
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

function Invoke-GhGraphQL {
    <#
      One GraphQL call, parsed to its `data` node. Throws on transport failure
      and on a response carrying `errors`, since gh exits zero for the latter.
      Only reachable off the proxy - see Get-ReviewThreads for the split.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Query,
        [hashtable]$Variables = @{}
    )

    $ghArgs = @('api', 'graphql', '-f', "query=$Query")
    foreach ($kv in $Variables.GetEnumerator()) {
        # -F for numbers so GraphQL sees an Int; -f keeps everything a string.
        $flag = if ($kv.Value -is [int] -or $kv.Value -is [long]) { '-F' } else { '-f' }
        $ghArgs += @($flag, "$($kv.Key)=$($kv.Value)")
    }

    $output = & gh @ghArgs 2>&1
    if ($LASTEXITCODE) { throw "gh api graphql failed with exit code ${LASTEXITCODE}: $($output -join [Environment]::NewLine)" }
    $parsed = ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join [Environment]::NewLine | ConvertFrom-Json
    if ($parsed.errors) { throw "GraphQL returned errors: $(($parsed.errors | ForEach-Object { $_.message }) -join '; ')" }
    $parsed.data
}

function Test-ProxyOnlyRouteMissing {
    <#
      True when a failure from Invoke-GhJson is the 404 that github.com gives a
      proxy-only `ccr/` route. Anything else - 403, a network failure, a real
      bad request - is not a reason to change transports, and is rethrown by
      the caller.
    #>
    param([Parameter(Mandatory)][string]$Message)
    # gh appends `gh: Not Found (HTTP 404)` to the body it echoes, and the body
    # itself carries "status": "404" - quoted today, matched unquoted too so
    # the detector never rests on one spelling.
    $Message -match '\bHTTP 404\b' -or $Message -match '"status":\s*"?404\b'
}

function Get-ReviewThreads {
    <#
      Every review thread on a pull request as [pscustomobject]s shaped like
      the proxy's `ccr/review_threads` rows: `comment_ids` (first entry is the
      thread's opening comment), `resolved`, `outdated`.

      The proxy route is tried first because it is the only thing that works
      in a cloud session, where GraphQL is refused (module header). On a
      normal machine that route does not exist - github.com answers 404 - and
      the same data is read through GraphQL's reviewThreads. Both transports
      key the result off the opening comment's databaseId, which is what every
      caller here wants, so the caller never learns which one answered.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][int]$PullNumber
    )

    try {
        return @(Invoke-GhJson "repos/$Owner/$Repo/pulls/$PullNumber/ccr/review_threads")
    }
    catch {
        if (-not (Test-ProxyOnlyRouteMissing $_.Exception.Message)) { throw }
    }

    $query = @'
query($owner: String!, $repo: String!, $number: Int!, $after: String) {
  repository(owner: $owner, name: $repo) {
    pullRequest(number: $number) {
      reviewThreads(first: 100, after: $after) {
        pageInfo { hasNextPage endCursor }
        nodes {
          id isResolved isOutdated
          comments(first: 100) { nodes { databaseId } }
        }
      }
    }
  }
}
'@

    $threads = [System.Collections.Generic.List[object]]::new()
    $after = $null
    while ($true) {
        $vars = @{ owner = $Owner; repo = $Repo; number = $PullNumber }
        if ($after) { $vars.after = $after }
        $page = (Invoke-GhGraphQL $query $vars).repository.pullRequest.reviewThreads
        foreach ($node in $page.nodes) {
            $threads.Add([pscustomobject]@{
                thread_id   = $node.id
                comment_ids = @($node.comments.nodes | ForEach-Object { [long]$_.databaseId })
                resolved    = [bool]$node.isResolved
                outdated    = [bool]$node.isOutdated
            })
        }
        if (-not $page.pageInfo.hasNextPage) { break }
        $after = $page.pageInfo.endCursor
    }
    $threads
}

function Resolve-ReviewThread {
    <#
      Resolves (or, with -Unresolve, reopens) the thread whose opening comment
      has the given id. Same transport split as Get-ReviewThreads: the proxy's
      `ccr/comments/{id}/resolve` first, GraphQL's resolveReviewThread when
      that route is missing. Throws when no thread on the PR starts with that
      comment.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][int]$PullNumber,
        [Parameter(Mandatory)][long]$CommentId,
        [switch]$Unresolve
    )

    $action = if ($Unresolve) { 'unresolve' } else { 'resolve' }

    $output = & gh api -X POST "repos/$Owner/$Repo/pulls/$PullNumber/ccr/comments/$CommentId/$action" 2>&1
    if (-not $LASTEXITCODE) { return }
    $message = $output -join [Environment]::NewLine
    if (-not (Test-ProxyOnlyRouteMissing $message)) { throw "gh api ccr/$action failed: $message" }

    $thread = Get-ReviewThreads -Owner $Owner -Repo $Repo -PullNumber $PullNumber |
        Where-Object { @($_.comment_ids)[0] -eq $CommentId } |
        Select-Object -First 1
    if (-not $thread) { throw "No review thread on $Owner/$Repo#$PullNumber opens with comment $CommentId." }

    $mutation = if ($Unresolve) { 'unresolveReviewThread' } else { 'resolveReviewThread' }
    $query = "mutation(`$id: ID!) { $mutation(input: { threadId: `$id }) { thread { isResolved } } }"
    $null = Invoke-GhGraphQL $query @{ id = $thread.thread_id }
}

Export-ModuleMember -Function Invoke-GhJson, Invoke-GhGraphQL, Get-Paged, Get-ReviewThreads, Resolve-ReviewThread
