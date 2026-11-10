# Builds the mod and the in-game test runner, installs both into an r2modman profile, starts the
# game windowed through Steam, waits for BepInEx/CraftingSpeedTests/report.txt and prints it.
# The test runner blocks all saving, so the save it loads is never written.
#
#   powershell -ExecutionPolicy Bypass -File tests\run-ingame-tests.ps1 [-Profile Default] [-TimeoutSeconds 900]
param(
    [string]$Profile = "Default",
    [int]$TimeoutSeconds = 900,
    [string]$Steam = "D:\Program Files (x86)\Steam\steam.exe"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$bepinex = "$env:APPDATA\r2modmanPlus-local\DysonSphereProgram\profiles\$Profile\BepInEx"

if (Get-Process DSPGAME -ErrorAction SilentlyContinue) { throw "The game is already running; close it first." }

foreach ($proj in "CraftingSpeedPreloader\CraftingSpeedPreloader.csproj", "CraftingSpeed\CraftingSpeed.csproj", "tests\InGameTests\InGameTests.csproj") {
    dotnet build (Join-Path $repo $proj) -c Release -nologo -v q -p:BepInExDir="$bepinex"
    if ($LASTEXITCODE -ne 0) { throw "build failed: $proj" }
}

Copy-Item "$repo\CraftingSpeed\bin\Release\CraftingSpeed.dll" "$bepinex\plugins\DSPCraftingSpeed-CraftingSpeedMod\" -Force
Copy-Item "$repo\CraftingSpeedPreloader\bin\Release\CraftingSpeedPreloader.dll" "$bepinex\patchers\DSPCraftingSpeed-CraftingSpeedMod\" -Force
New-Item -ItemType Directory -Force "$bepinex\plugins\CraftingSpeed-InGameTests" | Out-Null
Copy-Item "$repo\tests\InGameTests\bin\Release\CraftingSpeed.InGameTests.dll" "$bepinex\plugins\CraftingSpeed-InGameTests\" -Force

$report = "$bepinex\CraftingSpeedTests\report.txt"
Remove-Item $report -ErrorAction SilentlyContinue

$target = "$bepinex\core\BepInEx.Preloader.dll"
& $Steam -applaunch 1366540 --doorstop-enable true --doorstop-target "`"$target`"" -screen-fullscreen 0 -screen-width 1280 -screen-height 800
Write-Host "Game starting; waiting for the report..."

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $report) -and (Select-String -Path $report -Pattern "^RESULT" -Quiet)) { break }
    Start-Sleep -Seconds 5
}
if (Test-Path $report) { Get-Content $report -Encoding UTF8 } else { Write-Host "No report produced." }
Write-Host "`nScreenshots: $bepinex\CraftingSpeedTests"
