<#
.SYNOPSIS
    Resolve (or reopen) one pull request review thread by its opening comment id.

.DESCRIPTION
    The thread-resolution write has no REST endpoint on github.com. This wraps
    Resolve-ReviewThread (GitHubApi.psm1), which uses the proxy's ccr/ route in a
    Claude Code cloud session and GraphQL's resolveReviewThread everywhere else,
    so /pr-feedback runs the same command on either. The comment id is the one
    Get-OpenPrComments.ps1 reports, and the one a reply is posted against.

.EXAMPLE
    pwsh tools/Resolve-PrThread.ps1 -PullNumber 92 -CommentId 4057554323
    pwsh tools/Resolve-PrThread.ps1 -PullNumber 92 -CommentId 4057554323 -Unresolve
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$PullNumber,
    [Parameter(Mandatory)][long]$CommentId,
    [switch]$Unresolve
)

Import-Module (Join-Path $PSScriptRoot 'GitHubApi.psm1') -Force

Resolve-ReviewThread -Owner 'zhollis21' -Repo 'KingdomWatch' -PullNumber $PullNumber -CommentId $CommentId -Unresolve:$Unresolve

$verb = if ($Unresolve) { 'Reopened' } else { 'Resolved' }
Write-Host "$verb thread opened by comment $CommentId on #$PullNumber."
