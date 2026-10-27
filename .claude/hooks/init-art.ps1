# SessionStart hook: a worktree the app makes has the art submodule
# (Game/Assets/Art, #121) registered but empty, and a Unity build there
# quietly draws plain markers instead of the art. Check it out from the
# main checkout's copy: a local clone, no download, no credentials.
#
# Every worktree of the repository, not just the folder the hook is handed:
# the app runs it against the main checkout even when the session is in a
# new worktree (#193), so the worktree that needs the art is never the one
# named. A worktree whose commit has no art submodule, or whose folder is
# gone, is left alone.
#
# Does nothing when the main checkout has no art either: that is a clone
# without access, or one that chose to go without, and asking the remote at
# every session would only fail or prompt. Never blocks the session: a
# failure is reported and the session goes on, since everything but the
# Unity view works without the art.

$ErrorActionPreference = 'Stop'
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$submodule = 'Game/Assets/Art'

# Every run leaves a line in the shared .git folder (never committed, and
# the same file from every worktree): when (UTC), the folder it was handed,
# the folder it ran in, and what it decided. This is what told "never ran"
# (#98) from "ran against the wrong folder" (#193).
function Write-RunLog([string]$Decision) {
    try {
        # From the folder the hook was handed, not the one it runs in: a run
        # from outside the checkout is exactly what this is here to catch.
        $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null)
        if ($common) {
            $line = '{0:u}  project={1}  cwd={2}  {3}' -f (Get-Date).ToUniversalTime(), $env:CLAUDE_PROJECT_DIR, (Get-Location).Path, $Decision
            Add-Content -Path (Join-Path $common.Trim() 'init-art.log') -Value $line
        }
    }
    catch {
        # The log is a diagnosis aid; it never stops the hook.
    }
}

# Art there already: nothing to do. A missing folder counts as empty - the
# checkout recreates it - and so does one holding only git's own pointer,
# which a checkout that failed partway can leave.
function Test-Art([string]$Worktree) {
    $art = Join-Path $Worktree $submodule
    (Test-Path $art) -and (Get-ChildItem $art -Force | Where-Object Name -ne '.git' | Select-Object -First 1)
}

function Get-FullPath([string]$Path) { [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }

$messages = @()
try {
    # The main checkout is the folder holding the repository's shared .git.
    $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir).Trim()
    $main = Get-FullPath (Split-Path $common -Parent)
    $source = Join-Path $main $submodule

    if (-not (Test-Path (Join-Path $source '.git'))) {
        Write-RunLog "no art at $source to check out from"
        exit 0
    }

    # `git worktree list --porcelain` opens each worktree's block with its
    # path. One whose folder is gone is still listed until pruned.
    $worktrees = & git -C $root worktree list --porcelain | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9) }

    # Never wait on a password prompt nobody can see.
    $env:GIT_TERMINAL_PROMPT = '0'
    $acted = $false
    foreach ($worktree in $worktrees) {
        $path = Get-FullPath $worktree
        if ($path -eq $main -or -not (Test-Path $path) -or (Test-Art $path)) { continue }

        # A worktree on a commit from before the submodule has nothing to fill.
        # (As one string: with no output, a comparison against the empty
        # result is empty too, and reads as false.)
        $entry = "$(& git -C $path ls-files --stage -- $submodule)"
        if ($entry -notlike '160000 *') { continue }

        $acted = $true
        # Cloned from the main checkout's art, named here rather than taken
        # from the config: the shared config's URL can name another worktree
        # that is long gone (#193). Git refuses a local clone for a
        # submodule unless told it is allowed.
        $output = & git -C $path -c protocol.file.allow=always -c "submodule.$submodule.url=$source" `
            submodule update --init $submodule 2>&1 | Out-String

        # Git can call a submodule whose files went missing up to date, so
        # the files are what say it worked.
        if ($LASTEXITCODE -eq 0 -and (Test-Art $path)) {
            $message = "Checked out the art submodule ($submodule) in $path."
        }
        elseif ($LASTEXITCODE -eq 0) {
            $message = "The art submodule ($submodule) in $path is registered but has no files; Unity builds there will draw plain markers. Run: git -C $path/$submodule checkout -f"
        }
        else {
            $message = "The art submodule ($submodule) in $path is empty and could not be checked out; Unity builds there will draw plain markers.`n$($output.Trim())"
        }
        Write-RunLog $message
        $messages += $message
    }

    if (-not $acted) { Write-RunLog 'every worktree has the art, or has no art submodule' }
}
catch {
    $message = "The art submodule ($submodule) could not be checked out; Unity builds in a worktree without it will draw plain markers.`n$_"
    Write-RunLog $message
    $messages += $message
}

if ($messages) { @{ systemMessage = $messages -join "`n" } | ConvertTo-Json -Compress }
exit 0
