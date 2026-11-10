# set-publish-token.ps1
# 保存 DSPCraftingSpeed 团队的 Thunderstore 发布令牌，用当前 Windows 账号加密（只有你这台电脑上的你能解开）。
# 保存一次即可；之后 tools\publish.ps1 会自动读取它来发布，不用再登录网页。
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$tokenFile = Join-Path $repo 'publish\DSPCraftingSpeed.token'
$page = 'https://thunderstore.io/teams/DSPCraftingSpeed/service-accounts/'

if (Test-Path $tokenFile) {
  $answer = Read-Host "已经保存过令牌了。要换成新的吗？(y/n)"
  if ($answer -ne 'y') { Write-Host '没有改动。'; exit 0 }
}

Write-Host ''
Write-Host '1. 浏览器会打开 Thunderstore 的 DSPCraftingSpeed 团队 Service Accounts 页面（要先登录）。'
Write-Host '2. 点 Add service account，名字随便填，复制生成的令牌（tss_ 开头，只显示一次）。'
Write-Host '3. 回到这个窗口，粘贴（屏幕上不会显示），按回车。'
Write-Host ''
Start-Process $page

$secure = Read-Host '令牌' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $looksRight = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr).Trim().StartsWith('tss_') }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
if (-not $looksRight) {
  Write-Host '这看起来不是 Thunderstore 的令牌（应该以 tss_ 开头），没有保存。再运行一次重试。'
  exit 1
}

$secure | ConvertFrom-SecureString | Set-Content $tokenFile
Write-Host ''
Write-Host "已加密保存：$tokenFile"
Write-Host '以后发布直接双击「发布到Thunderstore.cmd」，或者让 Claude 发布，都不用再登录网页。'
