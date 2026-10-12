# What tools/Profile.ps1 (performance) and tools/Look.ps1 (a look at a
# change) share: finding Unity, running it headless, refusing a run without
# the art, and building the Windows development player (#98).

$ErrorActionPreference = 'Stop'

# The Unity the project names, or UNITY_EDITOR; throws when it is not there.
function Get-UnityEditor([string]$Game) {
    $version = (Select-String -Path (Join-Path $Game 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
    $unity = if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } else { "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" }
    if (-not (Test-Path $unity)) { throw "Unity $version not found at $unity; install it through Unity Hub or set UNITY_EDITOR." }
    $unity
}

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

# Whether a Unity - the Editor, or a headless run - has the project open: it
# holds Temp/UnityLockfile shut to everyone else while it does. Unity 6
# writes nothing about it to the log of the run it refuses, only exit 1
# (#98 review).
function Test-UnityProjectOpen([string]$Game) {
    $lock = Join-Path $Game 'Temp/UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try {
        [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Dispose()
        return $false
    }
    catch [IO.IOException] {
        return $true
    }
}

# Runs Unity headless against the project and throws, pointing at its log,
# if it fails.
function Invoke-Unity([string]$Unity, [string]$Game, [string]$Method, [string]$Log, [string[]]$Extra) {
    if (Test-UnityProjectOpen $Game) { throw "The project is open in the Editor (or another Unity run); close it first." }
    $arguments = @('-batchmode', '-nographics', '-projectPath', $Game, '-logFile', $Log, '-executeMethod', $Method) + $Extra
    $exitCode = Invoke-Program $Unity $arguments
    if ($exitCode -ne 0) {
        # Unity makes the log's folder itself, but one that dies before
        # writing leaves none, and reading it then would hide the exit code
        # (#135 review).
        throw "Unity $Method failed (exit $exitCode); see $Log."
    }
}

# Without the art a run draws coloured cells and plain markers, and says
# nothing about the game as it is drawn - a worktree made without the
# submodule produced exactly that, unnoticed. -Flat says it is meant.
function Assert-Art([string]$Game, [switch]$Flat) {
    $art = Join-Path $Game 'Assets/Art'
    if (-not $Flat -and -not ((Test-Path $art) -and (Get-ChildItem $art -Force | Where-Object Name -ne '.git' | Select-Object -First 1))) {
        throw "The art submodule ($art) is empty. Run: git submodule update --init Game/Assets/Art (see docs/unity.md), or pass -Flat for a run without it."
    }
}

# Builds Core, which copies its dll into the project, then the Windows
# development player into Game/Builds/Windows.
function Build-Player([string]$Root, [string]$Game, [string]$Unity) {
    Write-Host 'Building Core...'
    dotnet build (Join-Path $Root 'Core/KingdomWatch.Core.csproj') -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Core did not build.' }

    Write-Host 'Building the Windows development player...'
    Invoke-Unity $Unity $Game 'KingdomWatch.Game.Editor.DevBuild.Windows' (Join-Path $Game 'Builds/devbuild.log') @('-buildTarget', 'Win64')
}

Export-ModuleMember -Function Get-UnityEditor, Invoke-Program, Test-UnityProjectOpen, Invoke-Unity, Assert-Art, Build-Player
