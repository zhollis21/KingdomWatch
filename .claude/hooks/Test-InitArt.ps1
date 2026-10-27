# Checks init-art.ps1 against a throwaway repository: a main checkout with
# an art submodule, and worktrees the hook must fill or leave alone. Run
# after changing the hook:
#   pwsh .claude/hooks/Test-InitArt.ps1
# Exits non-zero if any case gets the wrong answer.

$ErrorActionPreference = 'Stop'
$hook = Join-Path $PSScriptRoot 'init-art.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("init-art-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = 0

function Check([bool]$Ok, [string]$What) {
    if ($Ok) { Write-Host "ok    $What" }
    else { Write-Host "FAIL  $What"; $script:failures++ }
}

function Invoke-Git { & git @args 2>&1 | Out-Null; if ($LASTEXITCODE) { throw "git $args failed" } }

function Has-Art([string]$Worktree) {
    Test-Path (Join-Path $Worktree 'Game/Assets/Art/sprite.txt')
}

# Runs the hook the way the app does (#193): handed the main checkout, in
# the main checkout, whatever worktree the session is for. Its errors are
# kept with its output, so a stray git complaint fails a check too.
function Run-Hook([string]$Project) {
    $env:CLAUDE_PROJECT_DIR = $Project
    Push-Location $Project
    try { $out = (& pwsh -NoProfile -File $hook 2>&1 | Out-String).Trim(); $global:LASTEXITCODE = $LASTEXITCODE; $out }
    finally { Pop-Location }
}

# No system or global git config, for the hook as well as the setup: a
# global protocol.file.allow=always would hide the hook leaving it out.
$env:GIT_CONFIG_NOSYSTEM = '1'
$env:GIT_CONFIG_GLOBAL = Join-Path $scratch 'gitconfig'
New-Item -ItemType Directory $scratch | Out-Null
New-Item -ItemType File $env:GIT_CONFIG_GLOBAL | Out-Null
$env:GIT_AUTHOR_NAME = $env:GIT_COMMITTER_NAME = 'test'
$env:GIT_AUTHOR_EMAIL = $env:GIT_COMMITTER_EMAIL = 'test@example.com'

try {
    $artRepo = Join-Path $scratch 'art'
    $main = Join-Path $scratch 'main'
    New-Item -ItemType Directory $artRepo, $main | Out-Null

    Invoke-Git -C $artRepo init -q -b main
    Set-Content (Join-Path $artRepo 'sprite.txt') 'art'
    Invoke-Git -C $artRepo add .
    Invoke-Git -C $artRepo commit -q -m art

    Invoke-Git -C $main init -q -b main
    Set-Content (Join-Path $main 'README') 'game'
    Invoke-Git -C $main add .
    Invoke-Git -C $main commit -q -m 'before the art'
    Invoke-Git -C $main tag before-art
    Invoke-Git -C $main -c protocol.file.allow=always submodule add -q $artRepo 'Game/Assets/Art'
    Invoke-Git -C $main commit -q -m 'add the art'

    # Like the shared config #193 found: the URL names a folder that is gone.
    Invoke-Git -C $main config 'submodule.Game/Assets/Art.url' (Join-Path $scratch 'deleted-worktree/Game/Assets/Art')

    $filled = Join-Path $scratch 'wt-filled'
    $noSubmodule = Join-Path $scratch 'wt-no-submodule'
    $gone = Join-Path $scratch 'wt-gone'
    Invoke-Git -C $main worktree add -q -b filled $filled
    Invoke-Git -C $main worktree add -q --detach $noSubmodule before-art
    Invoke-Git -C $main worktree add -q -b gone $gone
    Remove-Item -Recurse -Force $gone

    Check (-not (Has-Art $filled)) 'a new worktree starts without the art'

    $before = [DateTime]::UtcNow
    $message = Run-Hook $main
    Check (Has-Art $filled) 'run from the main checkout, the hook checks out the art in a worktree'
    Check ($message -match 'wt-filled') "the message names the worktree it filled ($message)"
    Check ($message -notmatch 'wt-no-submodule|wt-gone') 'worktrees without the submodule, or gone, are not mentioned'
    Check (-not (Test-Path (Join-Path $noSubmodule 'Game/Assets/Art/sprite.txt'))) 'a worktree whose commit has no art submodule is left alone'
    Check ((& git -C $main config 'submodule.Game/Assets/Art.url') -like '*deleted-worktree*') 'the shared config is not changed'

    $logPath = Join-Path (& git -C $main rev-parse --path-format=absolute --git-common-dir).Trim() 'init-art.log'
    $stamp = [DateTime]::ParseExact((Get-Content $logPath -First 1).Substring(0, 20), 'yyyy-MM-dd HH:mm:ssZ', $null,
        [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
    Check ([Math]::Abs(($stamp - $before).TotalMinutes) -lt 2) "the log's time is UTC ($stamp against $before)"

    $again = Run-Hook $main
    Check ([string]::IsNullOrEmpty($again)) "a second run has nothing to do and says nothing ($again)"

    # Handed a worktree instead (the app did this on 2026-10-12): same result.
    $other = Join-Path $scratch 'wt-other'
    Invoke-Git -C $main worktree add -q -b other $other
    Run-Hook $other | Out-Null
    Check (Has-Art $other) 'handed a worktree, the hook checks out the art there too'

    # A clone that fails is reported, and the session still goes on.
    $mainArtGit = Join-Path $main 'Game/Assets/Art/.git'
    $gitFile = Get-Content $mainArtGit
    Set-Content $mainArtGit 'gitdir: nowhere'
    $broken = Join-Path $scratch 'wt-broken'
    Invoke-Git -C $main worktree add -q -b broken $broken
    $failed = Run-Hook $main
    $exit = $LASTEXITCODE
    Check ($failed -match 'wt-broken.*could not be checked out') "a failed clone is reported ($failed)"
    Check ($exit -eq 0) 'and the hook still exits 0'
    Set-Content $mainArtGit $gitFile
    Run-Hook $main | Out-Null
    Check (Has-Art $broken) 'the next run, with the source mended, fills it'

    # No art in the main checkout: nothing to copy from, and no download.
    Invoke-Git -C $main submodule deinit -q -f 'Game/Assets/Art'
    $bare = Join-Path $scratch 'wt-bare'
    Invoke-Git -C $main worktree add -q -b bare $bare
    $none = Run-Hook $main
    Check (-not (Has-Art $bare)) 'with no art in the main checkout, nothing is checked out'
    Check ([string]::IsNullOrEmpty($none)) "and nothing is said ($none)"
}
finally {
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}

if ($failures) { Write-Host "$failures failed"; exit 1 }
Write-Host 'all passed'
