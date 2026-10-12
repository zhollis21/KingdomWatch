# Stop hook: before Claude hands back, the Unity scripts must compile. Runs
# only when a .cs file under Game/Assets (the art aside) has changed since the
# last compile that succeeded (a fingerprint of their paths, sizes and write
# times, kept in .claude/hooks/.last-unity-compile, which is git-ignored).
# build-on-stop.ps1 covers Core, Core.Tests and Harness; nothing compiled the
# view until this, and on #98 a change to it was handed back unbuilt.
#
# Opens the project headless, which takes a minute or two and cannot run
# while the Editor has it open: then it says so rather than passing. A
# failed compile blocks the stop once, with the errors; if it still fails
# when Claude stops again (stop_hook_active), it lets Claude stop and say so,
# as build-on-stop.ps1 does.

$ErrorActionPreference = 'Stop'
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$game = Join-Path $root 'Game'
$stamp = Join-Path $root '.claude/hooks/.last-unity-compile'
$assets = Join-Path $game 'Assets'

if (-not (Test-Path $assets)) {
    exit 0
}

$sources = Get-ChildItem $assets -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/]Assets[\\/]Art[\\/]' }

$fingerprint = ($sources | Sort-Object FullName |
    ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }) -join "`n"
$hash = [BitConverter]::ToString(
    [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprint))) -replace '-', ''

if ((Test-Path $stamp) -and (Get-Content $stamp -Raw).Trim() -eq $hash) {
    exit 0
}

function Say([string]$Message) {
    @{ systemMessage = $Message } | ConvertTo-Json -Compress
}

$version = (Select-String -Path (Join-Path $game 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$unity = if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } else { "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" }
if (-not (Test-Path $unity)) {
    Say "The Unity scripts changed but were not compiled: Unity $version is not at $unity (install it, or set UNITY_EDITOR)."
    exit 0
}

# Core's build copies the dll Unity compiles against, so the view meets the
# Core it will run with. When it fails, Unity would compile against the last
# dll and pass, and the stamp would then skip the check after Core is fixed:
# say so and leave the stamp alone (build-on-stop.ps1 reports Core's errors).
& dotnet build (Join-Path $root 'Core/KingdomWatch.Core.csproj') -c Release -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) {
    Say 'The Unity scripts changed but were not compiled: Core does not build, so Unity would only meet the last Core that did.'
    exit 0
}

$log = Join-Path ([IO.Path]::GetTempPath()) "kw-unity-compile-$PID.log"
$info = [System.Diagnostics.ProcessStartInfo]::new($unity)
$info.UseShellExecute = $false
# Unity's own console chatter would land in this hook's output, which must
# be JSON or nothing; what matters is in the log file.
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($argument in @('-batchmode', '-nographics', '-quit', '-projectPath', $game, '-logFile', $log)) { $info.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($info)
$null = $process.StandardOutput.ReadToEndAsync()
$null = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
$text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }

if ($process.ExitCode -eq 0) {
    Set-Content -Path $stamp -Value $hash -NoNewline
    exit 0
}

if ($text -match 'another Unity instance') {
    Say 'The Unity scripts changed but were not compiled: the project is open in the Editor. Close it, or check the Console there.'
    exit 0
}

$errors = (($text -split "`r?`n") | Where-Object { $_ -match 'error CS\d+' } |
    ForEach-Object { $_.Trim() } | Sort-Object -Unique | Select-Object -First 20) -join "`n"
if (-not $errors) { $errors = "Unity exited with $($process.ExitCode); see $log." }

if ($payload.stop_hook_active) {
    Say "The Unity scripts still do not compile:`n$errors"
    exit 0
}

@{
    decision = 'block'
    reason   = "The Unity scripts do not compile (Unity -batchmode -quit on Game/). Fix these before finishing:`n$errors"
} | ConvertTo-Json -Compress
exit 0
