# PreToolUse hook for Grep, Glob and Bash: keeps searches out of the folders
# Unity and dotnet generate - Library/, Temp/, obj/ - which are huge and hang a
# recursive search. Unity's Logs/ is left searchable: it is small, and its
# import, package and shader logs are what you grep when something breaks. The Grep and Glob tools already skip them through
# .gitignore unless a path names them; shell grep and find do not, so a
# recursive one over the repo root or Game/ is refused unless it excludes
# Library. Exit 2 blocks the tool call and shows Claude the reason.

$ErrorActionPreference = 'Stop'
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$tool = $payload.tool_name
$inputs = $payload.tool_input

# One of the generated folders, in the repo: under Game/ or a .NET project, or
# as the start of a relative path. Not just any path segment of that name -
# Windows' own temp folder is ...\AppData\Local\Temp\, where scratch files and
# test results live.
$generated = '(?i)((^|[\s"''=:])|(Game|Core|Core\.Tests|Harness)[\\/])(Library|Temp|obj)([\\/\s"'']|$)'

function Refuse([string] $why) {
    [Console]::Error.WriteLine(
        "Blocked by .claude/hooks/block-unity-search.ps1: $why " +
        "Search Assets/, Packages/, Core/, Core.Tests/, Harness/ or docs/ instead, " +
        "or exclude Library (e.g. grep --exclude-dir=Library, find -path '*/Library' -prune).")
    exit 2
}

switch ($tool) {
    { $_ -in 'Grep', 'Glob' } {
        # Grep's pattern is the text searched for, not a place; Glob's is a path.
        $fields = if ($tool -eq 'Glob') { 'path', 'pattern' } else { 'path', 'glob' }
        foreach ($field in $fields) {
            $value = $inputs.$field
            if ($value -and $value -match $generated) {
                Refuse "$tool's $field names a generated folder ($($Matches[4])/)."
            }
        }
    }

    'Bash' {
        $command = [string] $inputs.command

        # Only searches are judged: ls or cat of a file under obj/ is fine. A
        # search tool counts only where it runs - at the start of the command
        # or after a pipe, ;, && or a new line - not as a word in a commit
        # message or a heredoc that happens to say "grep" near "Library/".
        $runs = '(?im)(^|[;|&(]|\$\()\s*'
        $isSearch = $command -match ($runs + '(grep|egrep|rg|find|ag|Select-String)\b') -or
                    $command -match ($runs + '(Get-ChildItem|gci|ls|dir)\b[^|;\n]*-Recurse')
        if (-not $isSearch) { exit 0 }

        # Naming a folder only to exclude it is what we ask for, not a search of it.
        $excludes = $command -match '(?i)--exclude-dir|-prune|-not\s+-path|!\s+-path|--glob\s+[''"]?!'
        if (-not $excludes -and $command -match $generated) {
            Refuse "the search names a generated folder ($($Matches[4])/)."
        }

        # A recursive grep or a find that does not exclude Library, run over
        # the repo root or Game/. "No path given" means the working directory,
        # which is the repo root in these sessions.
        $recursive = $command -match '(?i)\bgrep\b[^|;]*\s-[a-zA-Z]*[rR]' -or
                     $command -match '(?i)\bgrep\b[^|;]*--recursive' -or
                     $command -match '(?i)(^|[\s;|&(])find\s'
        if ($recursive -and $command -notmatch '(?i)Library') {
            $root = [regex]::Escape(($env:CLAUDE_PROJECT_DIR -replace '\\', '/').TrimEnd('/'))
            $overRoot = $command -match '(?i)\s(\.|\./|Game|\./Game|Game/)(\s|$|[;|&)])' -or
                        ($root -and ($command -replace '\\', '/') -match "(?i)$root/?(\s|$|[;|&)'""])") -or
                        ($root -and ($command -replace '\\', '/') -match "(?i)$root/Game/?(\s|$|[;|&)'""])") -or
                        $command -match '(?i)\bgrep\b\s+(-[a-zA-Z]+\s+)*(''[^'']*''|"[^"]*"|\S+)\s*($|[;|&)])'
            if ($overRoot) {
                Refuse "a recursive search over the repo root or Game/ reaches Unity's Library/."
            }
        }
    }
}

exit 0
