# publish.ps1 [-Wait]
# Builds publish/build/<namespace>-<name>-<version>.zip with tcli and uploads it to Thunderstore.
# Namespace, name and version come from publish/thunderstore.toml (keep versionNumber in sync with
# Plugin.cs and CHANGELOG.md).
# The DSPCraftingSpeed team's service-account token is read from a DPAPI-encrypted file (only this
# Windows user can decrypt it) and handed to tcli through an environment variable for the duration of
# the call. The token is never printed. Create the file once with:
#   Read-Host "Thunderstore token" -AsSecureString | ConvertFrom-SecureString | Set-Content publish\DSPCraftingSpeed.token
# -Wait: wait until the token file exists.
param([switch]$Wait)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repo 'publish'
$tokenFile = Join-Path $publishDir 'DSPCraftingSpeed.token'   # git-ignored; tcli only packages the files listed in the toml
$tcli = Join-Path $env:USERPROFILE '.dotnet\tools\tcli.exe'

$toml = Join-Path $publishDir 'thunderstore.toml'
function TomlValue($key) { (Select-String -Path $toml -Pattern "^$key = `"(.+)`"").Matches[0].Groups[1].Value }
$ns = TomlValue 'namespace'
$name = TomlValue 'name'
$version = TomlValue 'versionNumber'
$api = "https://thunderstore.io/api/experimental/package/$ns/$name/"

# don't re-upload a version that is already live
$live = $null
try { $live = (Invoke-RestMethod $api).latest.version_number } catch { }
if ($live -eq $version) { Write-Output "version $version is already live on Thunderstore - nothing to do"; exit 0 }

# release build + package
foreach ($proj in 'CraftingSpeedPreloader\CraftingSpeedPreloader.csproj', 'CraftingSpeed\CraftingSpeed.csproj') {
  dotnet build (Join-Path $repo $proj) -c Release -nologo -v q | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "build failed: $proj" }
}
Push-Location $publishDir
try { & $tcli build | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'tcli build failed' } } finally { Pop-Location }
$zip = Join-Path $publishDir "build\$ns-$name-$version.zip"
if (-not (Test-Path $zip)) { throw "package not built: $zip" }
Write-Output "built $zip"

if ($Wait) { while (-not (Test-Path $tokenFile)) { Start-Sleep -Seconds 5 } }
if (-not (Test-Path $tokenFile)) { Write-Output "!! token file not found: $tokenFile"; exit 2 }

$secure = ConvertTo-SecureString (Get-Content $tokenFile -Raw).Trim()
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
  $env:TCLI_AUTH_TOKEN = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  Push-Location $publishDir
  try { & $tcli publish --file $zip 2>&1 | ForEach-Object { "$_" -replace 'tss_[A-Za-z0-9_\-\.]+', 'tss_***' } }
  finally { Pop-Location }
} finally {
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
  Remove-Item Env:TCLI_AUTH_TOKEN -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 5
try {
  $v = Invoke-RestMethod ($api + "$version/")
  Write-Output "Thunderstore now has $ns-$name $($v.version_number)"
} catch { Write-Output "could not verify on Thunderstore yet: $($_.Exception.Message)" }
