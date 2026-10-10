# Stop hook: before Claude hands back, the solution must build. Runs only when
# a .cs file under Core/, Core.Tests/ or Harness/ has changed since the last
# build that succeeded (a fingerprint of their paths, sizes and write times,
# kept in .claude/hooks/.last-build, which is git-ignored).
#
# A failed build blocks the stop once, with the errors, so Claude fixes them.
# If it is still failing when Claude stops again (stop_hook_active), the hook
# lets it stop - so Claude can say the build is broken and why - rather than
# trapping it in a loop on an error it cannot fix.

$ErrorActionPreference = 'Stop'
$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$stamp = Join-Path $root '.claude/hooks/.last-build'

$sources = foreach ($dir in 'Core', 'Core.Tests', 'Harness') {
    $path = Join-Path $root $dir
    if (Test-Path $path) {
        Get-ChildItem $path -Recurse -Filter *.cs -File |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
    }
}

$fingerprint = ($sources | Sort-Object FullName |
    ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }) -join "`n"
$hash = [BitConverter]::ToString(
    [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprint))) -replace '-', ''

if ((Test-Path $stamp) -and (Get-Content $stamp -Raw).Trim() -eq $hash) {
    exit 0
}

$output = & dotnet build (Join-Path $root 'KingdomWatch.sln') -c Release -v q -nologo 2>&1 | Out-String

if ($LASTEXITCODE -eq 0) {
    Set-Content -Path $stamp -Value $hash -NoNewline
    exit 0
}

$errors = ($output -split "`r?`n" | Where-Object { $_ -match ': error ' } |
    ForEach-Object { ($_ -replace '\s*\[[^\]]+\.csproj\]\s*$', '').Trim() } |
    Sort-Object -Unique | Select-Object -First 20) -join "`n"
if (-not $errors) { $errors = ($output -split "`r?`n" | Select-Object -Last 20) -join "`n" }

if ($payload.stop_hook_active) {
    @{ systemMessage = "The solution still does not build:`n$errors" } | ConvertTo-Json -Compress
    exit 0
}

@{
    decision = 'block'
    reason   = "dotnet build KingdomWatch.sln -c Release failed. Fix these before finishing:`n$errors"
} | ConvertTo-Json -Compress
exit 0
