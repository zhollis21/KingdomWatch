# SessionStart hook: a worktree the app makes has the art submodule
# (Game/Assets/Art, #121) registered but empty, and a Unity build there
# quietly draws plain markers instead of the art. When it is empty and the
# main checkout has the art, check it out from that copy: a local clone, no
# download, no credentials.
#
# Does nothing in the main checkout itself, or when the main checkout has no
# art either: that is a clone without access, or one that chose to go
# without, and asking the remote at every session would only fail or prompt.
# Never blocks the session: a failure is reported and the session goes on,
# since everything but the Unity view works without the art.

$ErrorActionPreference = 'Stop'
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$art = Join-Path $root 'Game/Assets/Art'

# Empty but for git's own pointer counts as empty: a checkout that failed
# partway can leave just that.
if (-not (Test-Path $art) -or (Get-ChildItem $art -Force | Where-Object Name -ne '.git' | Select-Object -First 1)) {
    exit 0
}

function Report([string]$Message) {
    @{ systemMessage = $Message } | ConvertTo-Json -Compress
}

try {
    # The main checkout is the folder holding the repository's shared .git.
    $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir).Trim()
    $source = Join-Path (Split-Path $common -Parent) 'Game/Assets/Art'

    if (-not (Test-Path (Join-Path $source '.git'))) {
        exit 0
    }

    # Never wait on a password prompt nobody can see.
    $env:GIT_TERMINAL_PROMPT = '0'
    $output = & git -C $root submodule update --init --reference $source 'Game/Assets/Art' 2>&1 | Out-String

    # Git can call a submodule whose files went missing up to date, so the
    # files are what say it worked.
    if ($LASTEXITCODE -eq 0 -and (Get-ChildItem $art -Force | Where-Object Name -ne '.git' | Select-Object -First 1)) {
        Report 'Checked out the art submodule (Game/Assets/Art) in this worktree.'
    }
    elseif ($LASTEXITCODE -eq 0) {
        Report "The art submodule (Game/Assets/Art) is registered but has no files; Unity builds here will draw plain markers. Run: git -C Game/Assets/Art checkout -f"
    }
    else {
        Report "The art submodule (Game/Assets/Art) is empty and could not be checked out; Unity builds here will draw plain markers.`n$($output.Trim())"
    }
}
catch {
    Report "The art submodule (Game/Assets/Art) is empty and could not be checked out; Unity builds here will draw plain markers.`n$_"
}

exit 0
