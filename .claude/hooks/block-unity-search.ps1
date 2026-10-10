# PreToolUse hook for Grep, Glob and Bash: keeps searches out of the folders
# Unity and dotnet generate - Library/, Temp/, obj/ - which are huge and hang a
# recursive search. Unity's Logs/ is left searchable: it is small, and its
# import, package and shader logs are what you grep when something breaks.
#
# The Grep and Glob tools skip those folders through .gitignore unless a path
# names one, so for them only the path is judged. For Bash, each command in the
# line is judged on its own: for every grep, rg, find or Get-ChildItem, its
# options, pattern and paths are told apart, and only its paths and its own
# exclusions count (the #157 review) - a pattern that says "Library" is a
# search for the word, and an exclusion in one command covers no other.
#
# Paths are judged where the shell would find them: from the folder the
# command runs in, following any cd before the search, with $CLAUDE_PROJECT_DIR,
# $HOME and ~ expanded. Only an exclusion that skips the Library folder itself
# counts - find's -prune, grep's --exclude-dir, rg's -g '!Library' - since one
# that only hides its files (find -not -path, rg -g '!**/Library/**') still
# walks all of it (the second #157 review).
#
# Exit 2 blocks the tool call and shows Claude the reason.
# Tests: pwsh .claude/hooks/Test-BlockUnitySearch.ps1

$ErrorActionPreference = 'Stop'
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$tool = $payload.tool_name
$inputs = $payload.tool_input
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$projectDir = (($env:CLAUDE_PROJECT_DIR, (Get-Location).Path) -ne $null)[0]
$homeDir = (($env:HOME, $env:USERPROFILE, $projectDir) -ne $null)[0]

# The generated folders a wildcard path could expand to, from the repo root.
$generatedFolders = 'Game/Library', 'Game/Temp', 'Game/obj', 'Core/obj', 'Core.Tests/obj', 'Harness/obj'

function Refuse([string] $why) {
    [Console]::Error.WriteLine(
        "Blocked by .claude/hooks/block-unity-search.ps1: $why " +
        "Search Assets/, Packages/, Core/, Core.Tests/, Harness/ or docs/ instead, " +
        "or skip Library (e.g. grep --exclude-dir=Library, find -path '*/Library' -prune, rg -g '!Library').")
    exit 2
}

# A path as the shell would resolve it from a folder: variables the hook can
# know expanded, ~ for home, Git Bash's /c/ for C:/ on Windows, and . and ..
# walked. $null for a path naming a variable the hook cannot know.
function FullPath([string] $path, [string] $from) {
    $known = @{ CLAUDE_PROJECT_DIR = $projectDir; HOME = $homeDir; PWD = $from }
    $p = [regex]::Replace($path.Trim(), '\$\{?(CLAUDE_PROJECT_DIR|HOME|PWD)\}?(?!\w)', { param($m) $known[$m.Groups[1].Value] })

    if ($p.Contains('$')) { return $null }
    if ($p -eq '~' -or $p.StartsWith('~/')) { $p = $homeDir + $p.Substring(1) }
    if ($IsWindows) { $p = $p -replace '^/([a-zA-Z])(?=/|$)', '$1:' }
    if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path $from $p }

    return ([IO.Path]::GetFullPath($p) -replace '\\', '/').TrimEnd('/')
}

$root = FullPath $projectDir $projectDir
$start = if ($payload.cwd) { FullPath ([string] $payload.cwd) $root } else { $root }

# A path relative to the repo root - '' for the root itself - or $null for one
# outside it, as Windows' own temp folder is. Inside means the root or below it
# on a folder boundary, so a sibling sharing its name is outside.
function RepoPath([string] $path, [string] $from) {
    $full = FullPath $path $from

    if ($null -eq $full) { return $null }
    if ([string]::Equals($full, $root, $comparison)) { return '' }
    if ($full.StartsWith($root + '/', $comparison)) { return $full.Substring($root.Length + 1) }
    return $null
}

# Which generated folder a path reaches into, or $null.
function GeneratedIn([string] $path, [string] $from) {
    $p = RepoPath $path $from
    if ($null -ne $p -and $p -match '(?i)(^|/)(Library|Temp|obj)(/|$)') { return $Matches[2] }
    return $null
}

# Whether a recursive search from this path walks into Library: it is the
# repo root or Game/, or a wildcard the shell would expand to one of them or
# to a generated folder itself.
function WalksIntoGenerated([string] $path, [string] $from) {
    $p = RepoPath $path $from

    if ($null -eq $p) { return $false }
    if ($p -eq '' -or $p -ieq 'Game') { return $true }
    if ($p -notmatch '[*?[]') { return $false }
    return @(@('Game') + $generatedFolders | Where-Object { $_ -like $p }).Count -gt 0
}

# Splits a command line into commands at ; && || | ( ) $( and new lines,
# outside quotes, dropping heredoc bodies, and each command into its words
# with quotes removed. A heredoc's body runs from the line after its << to
# its terminator line; <<< is a here-string, with no body.
function Commands([string] $line) {
    $commands = New-Object System.Collections.Generic.List[object]
    $words = New-Object System.Collections.Generic.List[string]
    $word = New-Object System.Text.StringBuilder
    $inWord = $false
    $quote = [char]0
    $heredocs = New-Object System.Collections.Generic.Queue[string]
    $skipping = $false

    foreach ($text in ($line -split "`n")) {
        if ($skipping) {
            if ($text.Trim() -eq $heredocs.Peek()) {
                [void]$heredocs.Dequeue()
                $skipping = $heredocs.Count -gt 0
            }

            continue
        }

        for ($i = 0; $i -lt $text.Length; $i++) {
            $c = $text[$i]

            if ($quote -ne [char]0) {
                if ($c -eq $quote) { $quote = [char]0 } else { [void]$word.Append($c) }
                continue
            }

            if ($c -eq "'" -or $c -eq '"') { $quote = $c; $inWord = $true; continue }

            if ($c -eq '<' -and $i + 1 -lt $text.Length -and $text[$i + 1] -eq '<') {
                if ($inWord) { $words.Add($word.ToString()); [void]$word.Clear(); $inWord = $false }

                if ($i + 2 -lt $text.Length -and $text[$i + 2] -eq '<') {
                    $i += 2
                    continue
                }

                $marker = [regex]::Match($text.Substring($i), "^<<-?\s*['""]?(\w+)['""]?")

                if ($marker.Success) {
                    $heredocs.Enqueue($marker.Groups[1].Value)
                    $i += $marker.Length - 1
                }
                else {
                    $i++
                }

                continue
            }

            if ($c -match '[;|&()]' -or ($c -eq '$' -and $i + 1 -lt $text.Length -and $text[$i + 1] -eq '(')) {
                if ($inWord) { $words.Add($word.ToString()); [void]$word.Clear(); $inWord = $false }
                if ($words.Count -gt 0) { $commands.Add($words.ToArray()); $words.Clear() }
                continue
            }

            if ([char]::IsWhiteSpace($c)) {
                if ($inWord) { $words.Add($word.ToString()); [void]$word.Clear(); $inWord = $false }
                continue
            }

            [void]$word.Append($c)
            $inWord = $true
        }

        if ($quote -eq [char]0) {
            if ($inWord) { $words.Add($word.ToString()); [void]$word.Clear(); $inWord = $false }
            if ($words.Count -gt 0) { $commands.Add($words.ToArray()); $words.Clear() }
            $skipping = $heredocs.Count -gt 0
        }
        else {
            [void]$word.Append("`n")
        }
    }

    if ($inWord) { $words.Add($word.ToString()) }
    if ($words.Count -gt 0) { $commands.Add($words.ToArray()) }
    return , $commands
}

# Whether a folder-name glob, or a {a,b} list of them, matches Library - what
# grep's --exclude-dir compares a folder's base name against.
function NamesLibrary([string] $glob) {
    $globs = if ($glob -match '^\{(.+)\}$') { $Matches[1] -split ',' } else { @($glob) }
    return @($globs | Where-Object { 'Library' -like $_.TrimEnd('/') }).Count -gt 0
}

# Judges one search: refuses it if a path reaches into a generated folder, or
# if it walks the root or Game/ recursively without skipping Library.
function Judge([string] $name, [string[]] $paths, [bool] $recursive, [bool] $excludes, [string] $from) {
    foreach ($path in $paths) {
        $folder = GeneratedIn $path $from
        if ($folder) { Refuse "$name searches a generated folder ($folder/)." }
    }

    if ($recursive -and -not $excludes) {
        $targets = if ($paths.Count -eq 0) { @('.') } else { $paths }
        foreach ($path in $targets) {
            if (WalksIntoGenerated $path $from) {
                Refuse "a recursive $name over the repo root or Game/ reaches Unity's Library/."
            }
        }
    }
}

function JudgeGrep([string] $name, [string[]] $argv, [string] $from) {
    # Options that take the next word as their value, short and long.
    $valued = 'efmABCdD'
    $valuedLong = '^--(regexp|file|max-count|after-context|before-context|context|include|exclude|exclude-dir|directories|devices)$'
    $recursive = $false
    $excludes = $false
    $patternGiven = $false
    $positional = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $argv.Count; $i++) {
        $a = $argv[$i]
        if ($a -eq '--') { $positional.AddRange([string[]]$argv[($i + 1)..($argv.Count - 1)]); break }

        if ($a -like '--*') {
            $option = ($a -split '=', 2)[0]
            $value = if ($a.Contains('=')) { ($a -split '=', 2)[1] } elseif ($option -match $valuedLong) { $i++; $argv[$i] } else { $null }
            if ($option -eq '--recursive' -or $option -eq '--dereference-recursive') { $recursive = $true }
            if ($option -in '--regexp', '--file') { $patternGiven = $true }
            # --exclude filters files by name; only --exclude-dir skips a folder.
            if ($option -eq '--exclude-dir' -and $value -and (NamesLibrary $value)) { $excludes = $true }
            continue
        }

        if ($a -like '-?*' -and $a -ne '-') {
            for ($j = 1; $j -lt $a.Length; $j++) {
                $flag = $a[$j]
                if ($flag -ceq 'r' -or $flag -ceq 'R') { $recursive = $true }
                if ($valued.IndexOf($flag) -ge 0) {
                    if ($flag -eq 'e' -or $flag -eq 'f') { $patternGiven = $true }
                    if ($j -eq $a.Length - 1) { $i++ }
                    break
                }
            }
            continue
        }

        $positional.Add($a)
    }

    $paths = if ($patternGiven) { $positional.ToArray() } else { @($positional | Select-Object -Skip 1) }
    Judge $name $paths $recursive $excludes $from
}

# Whether an rg glob skips the Library folder itself: a negated glob that
# matches Library, or Game/Library, as rg matches it against the folder. One
# matching only what is inside (!**/Library/**) leaves rg walking it.
function SkipsLibrary([string] $glob) {
    if (-not $glob.StartsWith('!')) { return $false }
    $p = $glob.Substring(1).Trim('/') -replace '\*\*', '*'
    return 'Library' -like $p -or 'Game/Library' -like $p
}

function JudgeRg([string[]] $argv, [string] $from) {
    # rg obeys .gitignore, which keeps Library out, unless told not to.
    $valued = 'efgmABCtTjM'
    $valuedLong = '^--(regexp|file|glob|iglob|max-count|after-context|before-context|context|type|type-not|threads|max-columns)$'
    $unignored = $false
    $excludes = $false
    $patternGiven = $false
    $positional = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $argv.Count; $i++) {
        $a = $argv[$i]
        if ($a -eq '--') { $positional.AddRange([string[]]$argv[($i + 1)..($argv.Count - 1)]); break }

        if ($a -like '--*') {
            $option = ($a -split '=', 2)[0]
            $value = if ($a.Contains('=')) { ($a -split '=', 2)[1] } elseif ($option -match $valuedLong) { $i++; $argv[$i] } else { $null }
            if ($option -in '--no-ignore', '--no-ignore-vcs') { $unignored = $true }
            if ($option -in '--regexp', '--file') { $patternGiven = $true }
            if ($option -in '--glob', '--iglob' -and $value -and (SkipsLibrary $value)) { $excludes = $true }
            continue
        }

        if ($a -like '-?*' -and $a -ne '-') {
            for ($j = 1; $j -lt $a.Length; $j++) {
                $flag = $a[$j]
                if ($flag -ceq 'u') { $unignored = $true }
                if ($valued.IndexOf($flag) -ge 0) {
                    if ($flag -eq 'e' -or $flag -eq 'f') { $patternGiven = $true }
                    $value = if ($j -lt $a.Length - 1) { $a.Substring($j + 1) } else { $i++; $argv[$i] }
                    if ($flag -ceq 'g' -and $value -and (SkipsLibrary $value)) { $excludes = $true }
                    break
                }
            }
            continue
        }

        $positional.Add($a)
    }

    $paths = if ($patternGiven) { $positional.ToArray() } else { @($positional | Select-Object -Skip 1) }
    Judge 'rg' $paths $unignored $excludes $from
}

function JudgeFind([string[]] $argv, [string] $from) {
    # The starting points come before the first expression word.
    $paths = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $argv.Count -and $argv[$i] -notmatch '^[-!(]') { $paths.Add($argv[$i]); $i++ }
    $starts = if ($paths.Count -eq 0) { @('.') } else { $paths.ToArray() }

    # Only -prune stops find walking a folder: -not -path hides what is in
    # it, after walking it. The test before -prune has to match Library -
    # by name, or by path as find prints it from a starting point.
    $excludes = $false
    for ($k = $i + 2; $k -lt $argv.Count; $k++) {
        if ($argv[$k] -ne '-prune') { continue }
        $test = $argv[$k - 2]
        $value = $argv[$k - 1]

        if ($test -in '-name', '-iname') {
            $excludes = $excludes -or ('Library' -like $value)
        }
        elseif ($test -in '-path', '-ipath', '-wholename', '-iwholename') {
            foreach ($s in $starts) {
                $s = $s.TrimEnd('/')
                $excludes = $excludes -or ("$s/Library" -like $value) -or ("$s/Game/Library" -like $value)
            }
        }
    }

    Judge 'find' $paths.ToArray() $true $excludes $from
}

function JudgeChildItem([string] $name, [string[]] $argv, [string] $from) {
    $recursive = $false
    $paths = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $argv.Count; $i++) {
        $a = $argv[$i]
        if ($a -match '^-(?i)(r|re|rec|recu|recur|recurs|recurse)$') { $recursive = $true; continue }
        if ($a -match '^-(?i)(path|literalpath|lp)$') { $i++; $paths.Add($argv[$i]); continue }
        if ($a -like '-*') { if ($a -match '^-(?i)(filter|include|exclude|depth)$') { $i++ }; continue }
        $paths.Add($a)
    }

    Judge $name $paths.ToArray() $recursive $false $from
}

switch ($tool) {
    { $_ -in 'Grep', 'Glob' } {
        # Grep's pattern is the text searched for, not a place; Glob's is a path.
        $fields = if ($tool -eq 'Glob') { 'path', 'pattern' } else { 'path', 'glob' }
        foreach ($field in $fields) {
            $value = $inputs.$field
            if ($value) {
                $folder = GeneratedIn $value $start
                if ($folder) { Refuse "$tool's $field reaches a generated folder ($folder/)." }
            }
        }
    }

    'Bash' {
        # Where the shell is: where the command starts, then wherever each
        # cd before a search has moved it.
        $here = $start

        foreach ($words in (Commands ([string] $inputs.command))) {
            # Skip variable assignments and wrappers to the command itself.
            $at = 0
            while ($at -lt $words.Count -and ($words[$at] -match '^\w+=' -or $words[$at] -in 'sudo', 'command', 'env', 'time')) { $at++ }
            if ($at -ge $words.Count) { continue }

            $name = (($words[$at] -replace '\\', '/') -split '/')[-1] -replace '\.exe$', ''
            $rest = [string[]]@($words | Select-Object -Skip ($at + 1))

            switch -Regex ($name) {
                '^(?i)(cd|pushd|chdir|Set-Location|sl)$' {
                    $target = @($rest | Where-Object { $_ -notin '-L', '-P', '--' -and $_ -notmatch '^-(?i)(path|literalpath)$' })
                    $to = if ($target.Count -eq 0) { $homeDir } elseif ($target[0] -eq '-') { $null } else { $target[0] }
                    if ($to) {
                        $moved = FullPath $to $here
                        if ($moved) { $here = $moved }
                    }
                }
                '^(e|f)?grep$' { JudgeGrep $name $rest $here }
                '^rg$' { JudgeRg $rest $here }
                '^find$' { JudgeFind $rest $here }
                '^(?i)(Get-ChildItem|gci|ls|dir|Select-String|sls)$' { JudgeChildItem $name $rest $here }
            }
        }
    }
}

exit 0
