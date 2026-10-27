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

    A run that logs an error, exception or failed assert fails without
    step 4, listing them: its timings are not worth comparing (#140). A
    player still running after five minutes is stopped, and the run fails
    (#194).

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
    the art submodule would draw it, flat frames in Unity's own font. Also
    lets the run go ahead without the art submodule, which otherwise stops it.

.PARAMETER SkipBuild
    Runs the player already in Game/Builds/Windows instead of building it.

.PARAMETER Seed
    Launches the player with -seed: the world from another seed, with no
    rebuild, since the seed is read when the player starts. The default is
    the scene's, 1.

.PARAMETER Stops
    Launches the player with -stops: only the stops named, each keeping its
    number from the whole route, and no capture analysis - a look at one view
    rather than a measurement. The stops are whole-map, quarter, quarter-pan,
    half, half-pan, full, full-pan, double, double-pan, whole-map-again,
    quarter-again, whole-from-quarter and quarter-back (ScriptedRun.cs).

.EXAMPLE
    pwsh tools/Profile.ps1

.EXAMPLE
    pwsh tools/Profile.ps1 -SkipBuild -Seed 30 -Stops whole-map
    Seed 30's whole map from the last build, in about the time the game takes
    to start.

.PREREQUISITES
    - The Unity version in Game/ProjectSettings/ProjectVersion.txt, with
      Windows Build Support, installed through Unity Hub (or UNITY_EDITOR set
      to its Unity.exe)
    - The art submodule checked out; without it the script stops, unless
      -Flat is passed
    - .NET SDK per global.json
#>
[CmdletBinding()]
param(
    [int]$Width = 2400,
    [int]$Height = 1080,
    [switch]$SkipBuild,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$Seed,
    [string[]]$Stops,
    [switch]$HudDemo,
    [switch]$Notch,
    [switch]$Flat
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'Game'
$captures = Join-Path $game 'ProfilerCaptures'
$player = Join-Path $game 'Builds/Windows/KingdomWatch.exe'

Import-Module (Join-Path $PSScriptRoot 'UnityPlayer.psm1') -Force
$unity = Get-UnityEditor $game

New-Item -ItemType Directory -Force $captures | Out-Null
Assert-Art $game -Flat:$Flat

if (-not $SkipBuild) { Build-Player $root $game $unity }
if (-not (Test-Path $player)) { throw "No player at $player; run without -SkipBuild." }

Write-Host "Flying the route at ${Width}x${Height}..."
$started = Get-Date
$arguments = @('-scripted-run', $captures, '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-logFile', (Join-Path $captures 'player.log'))
if ($HudDemo) { $arguments += '-hud-demo' }
if ($Notch) { $arguments += '-hud-notch' }
if ($Flat) { $arguments += '-hud-flat' }
if ($Seed) { $arguments += @('-seed', $Seed) }
if ($Stops) { $arguments += @('-stops', ($Stops -join ',')) }
# Five minutes, about five times a whole route with the player's start, so
# only a run that will never finish - one that throws every frame - reaches
# it (#194 review).
$timeout = 300
$exitCode = Invoke-Program $player $arguments -TimeoutSeconds $timeout
$playerLog = Join-Path $captures 'player.log'

# The run's log names it; its captures, one per stop, share its stamp.
$log = Get-ChildItem $captures -Filter 'Run_*.log' | Where-Object LastWriteTime -ge $started | Sort-Object LastWriteTime | Select-Object -Last 1
if ($null -eq $exitCode -or -not $log) {
    # Killed at the timeout, or crashed: the player's log says what it can.
    $what = if ($null -eq $exitCode) { "did not finish within $timeout s and was stopped" } else { "did not finish (exit $exitCode)" }
    throw "The run $what; see $playerLog.$((Get-PlayerProblems $playerLog | ForEach-Object { "`n$_" }) -join '')"
}
$stamp = $log.BaseName
$captured = @(Get-ChildItem $captures -Filter "${stamp}_*.raw" | Sort-Object Name)

# A finished run quits with 1 when it logged errors, and lists them in its
# log. Checked before the captures, since an error can be why there are none.
if ($exitCode -ne 0) {
    Get-Content $log.FullName
    throw "The run logged errors (exit $exitCode), so its captures were not analysed; they are listed above, and in full in $playerLog."
}
if ($captured.Count -eq 0) { throw "The run recorded no capture; see $playerLog." }

# A run of a few stops is a look, not a measurement: its screenshots are
# what it is for, and analysing its captures would cost a Unity session.
if (-not $Stops) {
    Write-Host "Analysing $($captured.Count) captures..."
    $arguments = foreach ($stop in $captured) { '-capture'; $stop.FullName }
    Invoke-Unity $unity $game 'KingdomWatch.Game.Editor.ProfilerAnalysis.Batch' (Join-Path $captures 'analysis.log') $arguments
}

Get-Content $log.FullName
Write-Host ''
if ($Stops) {
    Write-Host "Screenshots: $(Join-Path $captures "${stamp}_NN_<stop>.png")"
}
else {
    Write-Host "Markers: $(Join-Path $captures "${stamp}_NN_<stop>.markers.txt")"
}
