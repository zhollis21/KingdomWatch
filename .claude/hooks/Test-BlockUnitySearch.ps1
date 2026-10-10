# Checks block-unity-search.ps1 against the searches it must refuse and the
# commands it must let through. Run after changing the hook:
#   pwsh .claude/hooks/Test-BlockUnitySearch.ps1
# Exits non-zero if any case gets the wrong answer.

$hook = Join-Path $PSScriptRoot 'block-unity-search.ps1'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path -replace '\\', '/'
$env:CLAUDE_PROJECT_DIR = $root

# Expected exit (2 refuses, 0 allows), tool, input, and what the case is.
$cases = @(
    @(2, 'Grep', @{ pattern = 'x'; path = 'Game/Library' }, 'Grep into Game/Library'),
    @(2, 'Grep', @{ pattern = 'x'; path = 'Library' }, 'Grep into a relative Library'),
    @(2, 'Grep', @{ pattern = 'x'; glob = 'Game/obj/**' }, 'Grep glob into Game/obj'),
    @(2, 'Grep', @{ pattern = 'x'; path = 'Core/obj/Release' }, 'Grep into Core/obj'),
    @(2, 'Glob', @{ pattern = 'Game/Temp/**/*.cs' }, 'Glob into Game/Temp'),
    @(0, 'Grep', @{ pattern = 'Library'; path = 'Core' }, 'Grep for the word Library in Core'),
    @(0, 'Grep', @{ pattern = 'error'; path = 'Game/Logs' }, 'Grep in Unity''s Logs'),
    @(0, 'Grep', @{ pattern = 'x'; path = 'C:/Users/me/AppData/Local/Temp/claude/x.output' }, 'Grep in Windows temp'),
    @(0, 'Grep', @{ pattern = 'x'; path = 'Game/Assets' }, 'Grep in Assets'),

    @(2, 'Bash', @{ command = 'grep -rn foo .' }, 'recursive grep over .'),
    @(2, 'Bash', @{ command = 'grep -rn foo Game/' }, 'recursive grep over Game/'),
    @(2, 'Bash', @{ command = 'grep -r foo' }, 'recursive grep with no path'),
    @(2, 'Bash', @{ command = 'find . -name "*.cs"' }, 'find over .'),
    @(2, 'Bash', @{ command = 'cd Game && grep -rn foo .' }, 'recursive grep after cd Game &&'),
    @(2, 'Bash', @{ command = 'ls; find . -name "*.cs"' }, 'find after ;'),
    @(2, 'Bash', @{ command = 'echo $(grep -rn foo Game/)' }, 'recursive grep in command substitution'),
    @(2, 'Bash', @{ command = "cd Game`ngrep -rn foo Library/x" }, 'grep on a new line naming Library/'),
    @(2, 'Bash', @{ command = 'grep -n foo Game/obj/x' }, 'grep naming Game/obj'),
    @(2, 'Bash', @{ command = "grep -rn foo $root" }, 'recursive grep over the absolute repo root'),
    @(2, 'Bash', @{ command = 'Get-ChildItem -Recurse Game/Library' }, 'Get-ChildItem -Recurse into Library'),

    @(0, 'Bash', @{ command = 'grep -rn foo . --exclude-dir=Library' }, 'recursive grep excluding Library'),
    @(0, 'Bash', @{ command = 'find . -path ./Game/Library -prune -o -name "*.cs" -print' }, 'find pruning Library'),
    @(0, 'Bash', @{ command = 'grep -rn foo Core Harness' }, 'recursive grep in source folders'),
    @(0, 'Bash', @{ command = 'echo $(grep -rn foo Core/)' }, 'command substitution over Core/'),
    @(0, 'Bash', @{ command = 'grep -n foo Core/World.cs' }, 'grep one file'),
    @(0, 'Bash', @{ command = 'grep -n error Game/Logs/Packages-Update.log' }, 'grep a Unity log'),
    @(0, 'Bash', @{ command = 'grep -c worst C:/Users/me/AppData/Local/Temp/claude/x.output' }, 'grep in Windows temp'),
    @(0, 'Bash', @{ command = 'find Core -name "*.cs"' }, 'find in Core'),
    @(0, 'Bash', @{ command = 'git log | grep fix' }, 'piped grep'),
    @(0, 'Bash', @{ command = 'dotnet build Core/obj/x' }, 'a command that is not a search, naming obj'),
    @(0, 'Bash', @{ command = 'git commit -m "Block shell grep or find over Library/"' }, 'commit message mentioning grep and Library/'),
    @(0, 'Bash', @{ command = "git commit -F - <<EOF`n- recursive shell grep or find over Game/ that skips Library,`n- searches into Library/, Temp/`nEOF" }, 'heredoc mentioning grep and Library/'),

    # Each search judged on its own (the #157 review).
    @(2, 'Bash', @{ command = 'grep -rn foo . --exclude-dir=Library; grep -rn bar Game/obj' }, 'second search not covered by the first one''s exclusion'),
    @(2, 'Bash', @{ command = 'grep -rn foo Core && grep -rn bar .' }, 'second search over . after a safe one'),
    @(2, 'Bash', @{ command = 'grep foo -r' }, 'recursive flag after the pattern, no path'),
    @(2, 'Bash', @{ command = 'grep foo . -r' }, 'recursive flag after the path'),
    @(2, 'Bash', @{ command = 'grep -e foo -r .' }, 'pattern given with -e'),
    @(0, 'Bash', @{ command = 'grep -n Library Core/World.cs' }, 'searching for the word Library in a file'),
    @(0, 'Bash', @{ command = 'grep -rn -e Library Core' }, 'searching for Library with -e'),
    @(0, 'Bash', @{ command = 'grep -rn "Temp/" Harness' }, 'searching for the text Temp/'),
    @(0, 'Bash', @{ command = 'find Core -name Library' }, 'find naming Library as a name pattern'),
    @(2, 'Bash', @{ command = 'find Game/Library -name "*.asset"' }, 'find starting in Library'),
    @(0, 'Bash', @{ command = 'rg Library Core' }, 'rg searching for the word Library'),
    @(2, 'Bash', @{ command = 'rg foo Game/Library' }, 'rg into Library'),
    @(0, 'Bash', @{ command = 'rg foo' }, 'rg over the root, which obeys .gitignore'),
    @(2, 'Bash', @{ command = 'rg --no-ignore foo .' }, 'rg over the root ignoring .gitignore'),
    @(0, 'Bash', @{ command = 'rg --no-ignore foo . -g "!Library"' }, 'rg ignoring .gitignore but skipping Library'),
    @(0, 'Bash', @{ command = "grep -rn foo `"$root/Core`"" }, 'recursive grep over an absolute source folder'),

    # Paths resolved the way the shell would (the second #157 review).
    @(2, 'Bash', @{ command = 'grep -rn foo $CLAUDE_PROJECT_DIR' }, 'recursive grep over $CLAUDE_PROJECT_DIR'),
    @(2, 'Bash', @{ command = 'grep -rn foo "${CLAUDE_PROJECT_DIR}/Game"' }, 'recursive grep over ${CLAUDE_PROJECT_DIR}/Game'),
    @(2, 'Bash', @{ command = 'grep -rn foo Core/../Game' }, 'recursive grep over Core/../Game'),
    @(2, 'Bash', @{ command = 'grep -rn foo ~/Game' }, 'recursive grep over ~/Game when home is the repo', @{ home = $root }),
    @(0, 'Bash', @{ command = "grep -rn foo `"$root-old`"" }, 'recursive grep over a sibling folder sharing the repo''s name'),
    @(2, 'Bash', @{ command = 'grep -rn foo /home/me/KingdomWatch' }, 'recursive grep over a POSIX repo root', @{ project = '/home/me/KingdomWatch' }),
    @(0, 'Bash', @{ command = 'grep -rn foo /home/me/KingdomWatch/Core' }, 'recursive grep over a POSIX source folder', @{ project = '/home/me/KingdomWatch' }),
    @(0, 'Bash', @{ command = 'grep -rn foo /home/me/KingdomWatchOld' }, 'recursive grep over a POSIX sibling folder', @{ project = '/home/me/KingdomWatch' }),
    @(2, 'Bash', @{ command = 'cd Core && grep -rn foo ../Game' }, 'cd, then a relative path back into Game'),
    @(0, 'Bash', @{ command = 'cd Core && grep -rn foo .' }, 'cd into a source folder, then over .'),
    @(2, 'Bash', @{ command = 'grep -rn foo .' }, 'recursive grep over . from Game', @{ cwd = "$root/Game" }),
    @(0, 'Bash', @{ command = 'grep -rn foo .' }, 'recursive grep over . from Core', @{ cwd = "$root/Core" }),
    @(2, 'Grep', @{ pattern = 'x'; path = 'Library' }, 'Grep into Library from Game', @{ cwd = "$root/Game" }),
    @(2, 'Bash', @{ command = 'grep -rn foo Game/*' }, 'recursive grep over a wildcard that expands to Game/Library'),
    @(2, 'Bash', @{ command = 'grep -rn foo G*' }, 'recursive grep over a wildcard that expands to Game'),
    @(0, 'Bash', @{ command = 'grep -rn foo Core/*.cs' }, 'recursive grep over a wildcard in a source folder'),

    # Heredocs and here-strings.
    @(2, 'Bash', @{ command = "cat <<EOF`nx`nEOF`ngrep -rn foo ." }, 'search on the line after a heredoc ends'),
    @(2, 'Bash', @{ command = "cat <<EOF`nx`nEOF`nls`ngrep -rn foo ." }, 'search two lines after a heredoc ends'),
    @(2, 'Bash', @{ command = "cat <<< word`ngrep -rn foo ." }, 'search after a here-string'),
    @(2, 'Bash', @{ command = "git commit -m `"a <<EOF b`"`ngrep -rn foo ." }, 'search after << inside quotes'),

    # Only an exclusion that skips the folder counts.
    @(2, 'Bash', @{ command = 'grep -rn foo . --exclude=Library' }, 'grep --exclude, which filters files, not folders'),
    @(2, 'Bash', @{ command = 'grep -rn foo . --exclude-dir=NotLibrary' }, 'grep excluding a folder that is not Library'),
    @(0, 'Bash', @{ command = 'grep -rn foo . --exclude-dir Lib*' }, 'grep excluding Library by a glob'),
    @(0, 'Bash', @{ command = 'grep -rn foo . --exclude-dir={Library,obj}' }, 'grep excluding Library in a brace list'),
    @(2, 'Bash', @{ command = 'find . -path ./Temp -prune -o -name Library -print' }, 'find pruning another folder and naming Library'),
    @(2, 'Bash', @{ command = 'find . -not -path "*/Library/*" -name "*.cs"' }, 'find hiding Library, which still walks it'),
    @(0, 'Bash', @{ command = 'find . -name Library -prune -o -name "*.cs" -print' }, 'find pruning Library by name'),
    @(2, 'Bash', @{ command = 'rg --no-ignore foo . -g "!NotLibrary"' }, 'rg excluding a folder that is not Library'),
    @(2, 'Bash', @{ command = 'rg --no-ignore foo . -g "!**/Library/**"' }, 'rg hiding Library''s files, which still walks it'),
    @(0, 'Bash', @{ command = 'rg --no-ignore foo . --glob=!**/Library' }, 'rg skipping Library at any depth'),

    @(0, 'Read', @{ file_path = 'Game/Library/x' }, 'a tool the hook does not judge')
)

$failed = 0

foreach ($case in $cases) {
    $want, $tool, $toolInput, $what, $options = $case
    if ($null -eq $options) { $options = @{} }
    $env:CLAUDE_PROJECT_DIR = if ($options.project) { $options.project } else { $root }
    $env:HOME = if ($options.home) { $options.home } else { $home }
    $cwd = if ($options.cwd) { $options.cwd } else { $env:CLAUDE_PROJECT_DIR }
    $json = @{ tool_name = $tool; tool_input = $toolInput; cwd = $cwd } | ConvertTo-Json -Compress
    $json | pwsh -NoProfile -File $hook 2>$null | Out-Null
    $got = $LASTEXITCODE

    if ($got -eq $want) {
        Write-Output "ok    $what"
    }
    else {
        Write-Output "FAIL  $what (exit $got, wanted $want)"
        $failed++
    }
}

Write-Output "$($cases.Count - $failed) of $($cases.Count) as intended"
exit $(if ($failed -eq 0) { 0 } else { 1 })
