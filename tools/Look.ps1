<#
.SYNOPSIS
    Builds the Windows development player and screenshots a change where it
    shows: a seed, a year, and the camera on a kind of building (#98).
.DESCRIPTION
    The feature check, where tools/Profile.ps1 is the performance one. A
    change to what the view draws is not done until it has been seen (the
    #98 kickoff), and most of what a village builds stands only years in,
    which the profiling route - year 0, around the first village - never
    reaches. This runs the world -Years on before the first frame, flies
    to -Show (the first finished building of that kind, at full size) and
    the whole map, saves a screenshot of each, and prints their paths.

    A view regression check is two looks: the same -Seed, -Years and -Show
    on the base branch and on the change, side by side. Nothing is kept as
    a reference image, since the screenshots show the licensed art.

    Unity locks an open project, so the Editor must be closed for the
    build. Everything written is git-ignored, under Game/ProfilerCaptures.
.PARAMETER Show
    A BuildingKind (House, Barn, Field, Granary, Woodshed, ...). The run
    fails, saying so, when none of that kind stands by then: more -Years.
    Or a building's number, finished or not, as a run prints it (Building#50
    is -Show 50). The show stop is flown first, from -Days; the world keeps
    running through it, a few days by its screenshot. Without it, only the
    whole map.
.PARAMETER Years
    Years the world runs on before the first frame. Default 0.
.PARAMETER Days
    Days past -Years, for a season: a year is four 30-day seasons from
    spring, so -Days 60 is the first day of autumn. Default 0, the first
    day of spring.
.PARAMETER Seed
    The world's seed. Default the scene's, 1.
.PARAMETER SkipBuild
    Runs the player already in Game/Builds/Windows instead of building it.
.PARAMETER Width
    Window width in pixels; 2400 by 1080 is a phone held sideways.
.PARAMETER Height
    Window height in pixels.
.PARAMETER Flat
    Lets the run go ahead without the art submodule, drawing plain markers.
.EXAMPLE
    pwsh tools/Look.ps1 -Show Granary -Years 3
.EXAMPLE
    pwsh tools/Look.ps1 -SkipBuild -Seed 7 -Years 10 -Show Woodshed
#>
[CmdletBinding()]
param(
    [string]$Show,
    [ValidateRange(0, 1000)]
    [int]$Years = 0,
    [ValidateRange(0, 1000)]
    [int]$Days = 0,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$Seed,
    [switch]$SkipBuild,
    [int]$Width = 2400,
    [int]$Height = 1080,
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

$stops = if ($Show) { 'show,whole-map' } else { 'whole-map' }
Write-Host "Looking at seed $(if ($Seed) { $Seed } else { 1 }), year $Years$(if ($Days) { " day $Days" })$(if ($Show) { ", a $Show" })..."
$started = Get-Date
$playerLog = Join-Path $captures 'player.log'
$arguments = @('-scripted-run', $captures, '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-logFile', $playerLog, '-stops', $stops)
if ($Years -gt 0) { $arguments += @('-years', $Years) }
if ($Days -gt 0) { $arguments += @('-days', $Days) }
if ($Seed) { $arguments += @('-seed', $Seed) }
if ($Show) { $arguments += @('-show', $Show) }
if ($Flat) { $arguments += '-hud-flat' }
Invoke-Program $player $arguments | Out-Null

# The run's log names it; its screenshots share its stamp.
$log = Get-ChildItem $captures -Filter 'Run_*.log' | Where-Object LastWriteTime -ge $started | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $log) {
    # The player says why in its log: no such kind, or none standing yet.
    $why = if (Test-Path $playerLog) { (Select-String -Path $playerLog -Pattern '^(ScriptedRun|SimulationDriver): ' | Select-Object -First 1).Line } else { $null }
    throw "The look did not finish; see $playerLog.$(if ($why) { "`n$why" })"
}

$shots = @(Get-ChildItem $captures -Filter "$($log.BaseName)_*.png" | Sort-Object Name)
if ($shots.Count -eq 0) { throw "The look saved no screenshot; see $playerLog." }

Get-Content $log.FullName
Write-Host ''
$shots | ForEach-Object { Write-Host "Screenshot: $($_.FullName)" }
