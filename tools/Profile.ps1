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

# Without the art the route flies over coloured cells and plain markers, and
# its timings and screenshots say nothing about the game as it is drawn - a
# worktree made without the submodule produced exactly that, unnoticed.
# -Flat says a run without the art is meant.
$art = Join-Path $game 'Assets/Art'
if (-not $Flat -and -not ((Test-Path $art) -and (Get-ChildItem $art -Force | Where-Object Name -ne '.git' | Select-Object -First 1))) {
    throw "The art submodule ($art) is empty. Run: git submodule update --init Game/Assets/Art (see docs/unity.md), or pass -Flat for a run without it."
}

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
if ($Seed) { $arguments += @('-seed', $Seed) }
if ($Stops) { $arguments += @('-stops', ($Stops -join ',')) }
Invoke-Program $player $arguments | Out-Null

# The run's log names it; its captures, one per stop, share its stamp.
$log = Get-ChildItem $captures -Filter 'Run_*.log' | Where-Object LastWriteTime -ge $started | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $log) {
    # The player says why in its log - a stop -Stops names that does not
    # exist - so say it here too.
    $playerLog = Join-Path $captures 'player.log'
    $why = if (Test-Path $playerLog) { (Select-String -Path $playerLog -Pattern '^(ScriptedRun|SimulationDriver): ' | Select-Object -First 1).Line } else { $null }
    throw "The run did not finish; see $playerLog.$(if ($why) { "`n$why" })"
}
$stamp = $log.BaseName
$captured = @(Get-ChildItem $captures -Filter "${stamp}_*.raw" | Sort-Object Name)
if ($captured.Count -eq 0) { throw "The run recorded no capture; see $(Join-Path $captures 'player.log')." }

# A run of a few stops is a look, not a measurement: its screenshots are
# what it is for, and analysing its captures would cost a Unity session.
if (-not $Stops) {
    Write-Host "Analysing $($captured.Count) captures..."
    $arguments = foreach ($stop in $captured) { '-capture'; $stop.FullName }
    Invoke-Unity 'KingdomWatch.Game.Editor.ProfilerAnalysis.Batch' (Join-Path $captures 'analysis.log') $arguments
}

Get-Content $log.FullName
Write-Host ''
if ($Stops) {
    Write-Host "Screenshots: $(Join-Path $captures "${stamp}_NN_<stop>.png")"
}
else {
    Write-Host "Markers: $(Join-Path $captures "${stamp}_NN_<stop>.markers.txt")"
}
