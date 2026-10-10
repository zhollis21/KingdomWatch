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
# Exit 2 blocks the tool call and shows Claude the reason.
# Tests: pwsh .claude/hooks/Test-BlockUnitySearch.ps1

$ErrorActionPreference = 'Stop'
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$tool = $payload.tool_name
$inputs = $payload.tool_input
$repo = (($env:CLAUDE_PROJECT_DIR, (Get-Location).Path) -ne $null)[0] -replace '\\', '/' -replace '/+$', ''

function Refuse([string] $why) {
    [Console]::Error.WriteLine(
        "Blocked by .claude/hooks/block-unity-search.ps1: $why " +
        "Search Assets/, Packages/, Core/, Core.Tests/, Harness/ or docs/ instead, " +
        "or exclude Library (e.g. grep --exclude-dir=Library, find -path '*/Library' -prune).")
    exit 2
}

# A path as the repo sees it: relative, forward slashes, no leading ./. An
# absolute path outside the repo - Windows' own temp folder, say - gives $null.
function RepoPath([string] $path) {
    $p = ($path -replace '\\', '/').Trim()
    if ($p -match '^(/[a-zA-Z]/|[a-zA-Z]:/)') {
        $abs = $p -replace '^/([a-zA-Z])/', '$1:/'
        $root = $repo -replace '^/([a-zA-Z])/', '$1:/'
        if (-not $abs.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return $null }
        $p = $abs.Substring($root.Length)
    }
    return ($p -replace '^(\./)+', '' -replace '^/+', '' -replace '/+$', '')
}

# Which generated folder a path reaches into, or $null.
function GeneratedIn([string] $path) {
    $p = RepoPath $path
    if ($null -ne $p -and $p -match '(^|/)(Library|Temp|obj)(/|$)') { return $Matches[2] }
    return $null
}

# Whether a path is the repo root or Game/, which a recursive search walks
# straight into Library from.
function IsRootOrGame([string] $path) {
    $p = RepoPath $path
    return $null -ne $p -and ($p -eq '' -or $p -eq '.' -or $p -ieq 'Game')
}

# Splits a command line into commands at ; && || | ( ) $( and new lines,
# outside quotes, dropping heredoc bodies, and each command into its words
# with quotes removed.
function Commands([string] $line) {
    $commands = New-Object System.Collections.Generic.List[object]
    $words = New-Object System.Collections.Generic.List[string]
    $word = New-Object System.Text.StringBuilder
    $inWord = $false
    $quote = [char]0
    $heredoc = $null
    $lines = $line -split "`n"
    $skipping = $false

    foreach ($text in $lines) {
        if ($skipping) {
            if ($text.Trim() -eq $heredoc) { $skipping = $false }
            continue
        }

        if ($text -match "<<-?\s*['""]?(\w+)['""]?") { $heredoc = $Matches[1] }

        for ($i = 0; $i -lt $text.Length; $i++) {
            $c = $text[$i]

            if ($quote -ne [char]0) {
                if ($c -eq $quote) { $quote = [char]0 } else { [void]$word.Append($c) }
                continue
            }

            if ($c -eq "'" -or $c -eq '"') { $quote = $c; $inWord = $true; continue }

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
        }
        else {
            [void]$word.Append("`n")
        }

        if ($heredoc) { $skipping = $true }
    }

    if ($inWord) { $words.Add($word.ToString()) }
    if ($words.Count -gt 0) { $commands.Add($words.ToArray()) }
    return , $commands
}

# Judges one search: refuses it if a path reaches into a generated folder, or
# if it walks the root or Game/ recursively without excluding Library.
function Judge([string] $name, [string[]] $paths, [bool] $recursive, [bool] $excludes) {
    foreach ($path in $paths) {
        $folder = GeneratedIn $path
        if ($folder) { Refuse "$name searches a generated folder ($folder/)." }
    }

    if ($recursive -and -not $excludes) {
        $targets = if ($paths.Count -eq 0) { @('.') } else { $paths }
        foreach ($path in $targets) {
            if (IsRootOrGame $path) {
                Refuse "a recursive $name over the repo root or Game/ reaches Unity's Library/."
            }
        }
    }
}

function JudgeGrep([string] $name, [string[]] $argv) {
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
            if ($option -in '--exclude-dir', '--exclude' -and $value -match 'Library') { $excludes = $true }
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
    Judge $name $paths $recursive $excludes
}

function JudgeRg([string[]] $argv) {
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
            if ($option -in '--glob', '--iglob' -and $value -match '^!.*Library') { $excludes = $true }
            continue
        }

        if ($a -like '-?*' -and $a -ne '-') {
            for ($j = 1; $j -lt $a.Length; $j++) {
                $flag = $a[$j]
                if ($flag -ceq 'u') { $unignored = $true }
                if ($valued.IndexOf($flag) -ge 0) {
                    if ($flag -eq 'e' -or $flag -eq 'f') { $patternGiven = $true }
                    $value = if ($j -lt $a.Length - 1) { $a.Substring($j + 1) } else { $i++; $argv[$i] }
                    if ($flag -ceq 'g' -and $value -match '^!.*Library') { $excludes = $true }
                    break
                }
            }
            continue
        }

        $positional.Add($a)
    }

    $paths = if ($patternGiven) { $positional.ToArray() } else { @($positional | Select-Object -Skip 1) }
    Judge 'rg' $paths $unignored $excludes
}

function JudgeFind([string[]] $argv) {
    # The starting points come before the first expression word.
    $paths = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $argv.Count -and $argv[$i] -notmatch '^[-!(]') { $paths.Add($argv[$i]); $i++ }
    $expression = ($argv | Select-Object -Skip $i) -join ' '
    $excludes = $expression -match '(?i)(-prune|-not\s+-i?(path|wholename)|!\s+-i?(path|wholename))' -and $expression -match 'Library'
    Judge 'find' $paths.ToArray() $true $excludes
}

function JudgeChildItem([string] $name, [string[]] $argv) {
    $recursive = $false
    $paths = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $argv.Count; $i++) {
        $a = $argv[$i]
        if ($a -match '^-(?i)(r|re|rec|recu|recur|recurs|recurse)$') { $recursive = $true; continue }
        if ($a -match '^-(?i)(path|literalpath|lp)$') { $i++; $paths.Add($argv[$i]); continue }
        if ($a -like '-*') { if ($a -match '^-(?i)(filter|include|exclude|depth)$') { $i++ }; continue }
        $paths.Add($a)
    }

    Judge $name $paths.ToArray() $recursive $false
}

switch ($tool) {
    { $_ -in 'Grep', 'Glob' } {
        # Grep's pattern is the text searched for, not a place; Glob's is a path.
        $fields = if ($tool -eq 'Glob') { 'path', 'pattern' } else { 'path', 'glob' }
        foreach ($field in $fields) {
            $value = $inputs.$field
            if ($value) {
                $folder = GeneratedIn $value
                if ($folder) { Refuse "$tool's $field reaches a generated folder ($folder/)." }
            }
        }
    }

    'Bash' {
        foreach ($words in (Commands ([string] $inputs.command))) {
            # Skip variable assignments and wrappers to the command itself.
            $at = 0
            while ($at -lt $words.Count -and ($words[$at] -match '^\w+=' -or $words[$at] -in 'sudo', 'command', 'env', 'time')) { $at++ }
            if ($at -ge $words.Count) { continue }

            $name = (($words[$at] -replace '\\', '/') -split '/')[-1] -replace '\.exe$', ''
            $rest = [string[]]@($words | Select-Object -Skip ($at + 1))

            switch -Regex ($name) {
                '^(e|f)?grep$' { JudgeGrep $name $rest }
                '^rg$' { JudgeRg $rest }
                '^find$' { JudgeFind $rest }
                '^(?i)(Get-ChildItem|gci|ls|dir|Select-String|sls)$' { JudgeChildItem $name $rest }
            }
        }
    }
}

exit 0
