<#
.SYNOPSIS
    Builds a Windows development player, flies its scripted camera route,
    and analyses the capture it records (#132).

.DESCRIPTION
    Four steps, each headless, so a rendering change can be measured before
    and after without anyone at the Editor:

    1. Builds Core, which copies KingdomWatch.Core.dll into the Unity project.
    2. Builds the player into Game/Builds/Windows (Game/Assets/Editor/DevBuild.cs).
    3. Runs it with -scripted-run, which flies the route in
       Game/Assets/Simulation/ScriptedRun.cs and writes, into
       Game/ProfilerCaptures: Run_<stamp>.log (how long each stop took to
       fill in, and its frame times), and for each stop a capture and a
       screenshot, Run_<stamp>_NN_<stop>.raw and .png.
    4. Analyses each capture into Run_<stamp>_NN_<stop>.markers.txt
       (Game/Assets/Editor/ProfilerAnalysis.cs, Batch), in one Unity session.

    Unity locks an open project, so the Editor must be closed for steps 2
    and 4. Everything it writes is git-ignored: the screenshots show the
    licensed art, which never goes in this repository.

.PARAMETER Width
    Window width in pixels. 2400 by 1080 is a phone held sideways, which is
    how many screen pixels the view has to fill on one.

.PARAMETER Height
    Window height in pixels.

.PARAMETER HudDemo
    Launches the player with -hud-demo: someone is selected and the debug
    card is open, so the screenshots show those parts of the panel. Leave it
    off when comparing frame times with an earlier run.

.PARAMETER Notch
    Launches the player with -hud-notch: the panel is inset as a phone's camera
    cutout and rounded corners would inset it, to look at the layout on a
    screen that has neither.

.PARAMETER Flat
    Launches the player with -hud-flat: the panel is drawn as a build without
    the art submodule would draw it, flat frames in Unity's own font.

.PARAMETER SkipBuild
    Runs the player already in Game/Builds/Windows instead of building it.

.EXAMPLE
    pwsh tools/Profile.ps1

.PREREQUISITES
    - The Unity version in Game/ProjectSettings/ProjectVersion.txt, with
      Windows Build Support, installed through Unity Hub (or UNITY_EDITOR set
      to its Unity.exe)
    - The art submodule checked out, or the route flies over plain markers
    - .NET SDK per global.json
#>
[CmdletBinding()]
param(
    [int]$Width = 2400,
    [int]$Height = 1080,
    [switch]$SkipBuild,
    [switch]$HudDemo,
    [switch]$Notch,
    [switch]$Flat
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'Game'
$captures = Join-Path $game 'ProfilerCaptures'
$player = Join-Path $game 'Builds/Windows/KingdomWatch.exe'

$version = (Select-String -Path (Join-Path $game 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$unity = if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } else { "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" }
if (-not (Test-Path $unity)) { throw "Unity $version not found at $unity; install it through Unity Hub or set UNITY_EDITOR." }

# Runs a program and waits for it alone, returning its exit code. Each
# argument goes through ProcessStartInfo.ArgumentList, which quotes it:
# Start-Process -ArgumentList joins an array with bare spaces, so a checkout
# under a folder with a space in its name split every path in two (#135
# review). And it waits for the process alone: Start-Process -Wait also waits
# for everything it started, and Unity's licensing client outlives it.
function Invoke-Program([string]$Path, [string[]]$Arguments) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($Path)
    $info.UseShellExecute = $false
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    $process.WaitForExit()
    $process.ExitCode
}

# Runs Unity headless against the project and throws, pointing at its log,
# if it fails.
function Invoke-Unity([string]$Method, [string]$Log, [string[]]$Extra) {
    $arguments = @('-batchmode', '-nographics', '-projectPath', $game, '-logFile', $Log, '-executeMethod', $Method) + $Extra
    $exitCode = Invoke-Program $unity $arguments
    if ($exitCode -ne 0) {
        # Unity makes the log's folder itself, but one that dies before
        # writing leaves none, and reading it then would hide the exit code
        # (#135 review).
        $open = (Test-Path $Log) -and (Select-String -Path $Log -Pattern 'another Unity instance' -Quiet)
        $hint = if ($open) { ' The project is open in the Editor; close it first.' } else { '' }
        throw "Unity $Method failed (exit $exitCode); see $Log.$hint"
    }
}

New-Item -ItemType Directory -Force $captures | Out-Null

if (-not $SkipBuild) {
    Write-Host 'Building Core...'
    dotnet build (Join-Path $root 'Core/KingdomWatch.Core.csproj') -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Core did not build.' }

    Write-Host 'Building the Windows development player...'
    Invoke-Unity 'KingdomWatch.Game.Editor.DevBuild.Windows' (Join-Path $game 'Builds/devbuild.log') @('-buildTarget', 'Win64')
}
if (-not (Test-Path $player)) { throw "No player at $player; run without -SkipBuild." }

Write-Host "Flying the route at ${Width}x${Height}..."
$started = Get-Date
$arguments = @('-scripted-run', $captures, '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-logFile', (Join-Path $captures 'player.log'))
if ($HudDemo) { $arguments += '-hud-demo' }
if ($Notch) { $arguments += '-hud-notch' }
if ($Flat) { $arguments += '-hud-flat' }
Invoke-Program $player $arguments | Out-Null

# The run's log names it; its captures, one per stop, share its stamp.
$log = Get-ChildItem $captures -Filter 'Run_*.log' | Where-Object LastWriteTime -ge $started | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $log) { throw "The run did not finish; see $(Join-Path $captures 'player.log')." }
$stamp = $log.BaseName
$stops = @(Get-ChildItem $captures -Filter "${stamp}_*.raw" | Sort-Object Name)
if ($stops.Count -eq 0) { throw "The run recorded no capture; see $(Join-Path $captures 'player.log')." }

Write-Host "Analysing $($stops.Count) captures..."
$arguments = foreach ($stop in $stops) { '-capture'; $stop.FullName }
Invoke-Unity 'KingdomWatch.Game.Editor.ProfilerAnalysis.Batch' (Join-Path $captures 'analysis.log') $arguments

Get-Content $log.FullName
Write-Host ''
Write-Host "Markers: $(Join-Path $captures "${stamp}_NN_<stop>.markers.txt")"
