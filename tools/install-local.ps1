# install-local.ps1 [-Profile Default]
# Copies the Release build of the mod (plugin + preloader DLLs) into an r2modman profile, replacing the
# installed copies. manifest.json is left alone so r2modman still offers the Thunderstore update.
# Refuses while the game is running.
param([string]$Profile = 'Default')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bepinex = "$env:APPDATA\r2modmanPlus-local\DysonSphereProgram\profiles\$Profile\BepInEx"

if (Get-Process DSPGAME -ErrorAction SilentlyContinue) { Write-Output '!! the game is running - close it first; nothing installed'; exit 2 }

$plugin = Get-ChildItem "$bepinex\plugins" -Recurse -Filter CraftingSpeed.dll | Select-Object -First 1
$preloader = Get-ChildItem "$bepinex\patchers" -Recurse -Filter CraftingSpeedPreloader.dll | Select-Object -First 1
if (-not $plugin -or -not $preloader) { Write-Output "!! CraftingSpeed is not installed in profile '$Profile' (install it once with r2modman)"; exit 3 }

Copy-Item "$repo\CraftingSpeed\bin\Release\CraftingSpeed.dll" $plugin.FullName -Force
Copy-Item "$repo\CraftingSpeedPreloader\bin\Release\CraftingSpeedPreloader.dll" $preloader.FullName -Force
$version = (Get-Item $plugin.FullName).VersionInfo.FileVersion
Write-Output "installed into $($plugin.DirectoryName) and $($preloader.DirectoryName)"
