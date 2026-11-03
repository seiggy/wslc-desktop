<#
.SYNOPSIS
  Packs a published (self-contained) build into a Velopack release: Setup.exe, full/delta nupkg, portable zip and the channel feed.
.DESCRIPTION
  Run Build-Release.ps1 first; it leaves the publish folder at publish\win-<arch>. Releases are unsigned by default.
  To sign later, pass -SignParams (signtool arguments, e.g. '/td sha256 /fd sha256 /f cert.pfx /p ***') or
  -AzureTrustedSignFile (path to Azure Trusted Signing metadata.json). Both are forwarded to `vpk pack`, which signs
  every executable and the Setup.exe. Nothing else in the workflow has to change.
.EXAMPLE
  ./tools/packaging/Pack-Velopack.ps1 -Arch x64 -Version 1.2.3 -OutDir artifacts/velopack
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutDir = 'artifacts\velopack',
    [string]$PublishDir,
    [string]$ReleaseNotes,
    [string]$SignParams,
    [string]$AzureTrustedSignFile
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' must look like 1.2.3 or 1.2.3-beta.1." }
if (-not $PublishDir) { $PublishDir = Join-Path $repo "publish\win-$Arch" }
if (-not (Test-Path (Join-Path $PublishDir 'WslcDesktop.exe'))) { throw "No published app in $PublishDir. Run Build-Release.ps1 first." }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'vpk not found. Install with: dotnet tool install -g vpk' }

$null = New-Item -ItemType Directory -Force $OutDir
$OutDir = (Resolve-Path $OutDir).Path
# The pack id doubles as the install folder (%LOCALAPPDATA%\WslcDesktopManager). It must NOT be "WslcDesktop":
# that folder holds the app's settings and container storage, and uninstalling would delete it.
$args = @(
    '[win]', 'pack',
    '--packId', 'WslcDesktopManager',
    '--packVersion', $Version,
    '--packDir', $PublishDir,
    '--mainExe', 'WslcDesktop.exe',
    '--packTitle', 'WSLc Desktop Manager',
    '--packAuthors', 'seiggy',
    '--icon', (Join-Path $repo 'src\WslcDesktop\Assets\AppIcon.ico'),
    '--runtime', "win-$Arch",
    '--channel', "win-$Arch",
    '--outputDir', $OutDir
)
if ($ReleaseNotes) { $args += '--releaseNotes', $ReleaseNotes }
if ($SignParams) { $args += '--signParams', $SignParams }
if ($AzureTrustedSignFile) { $args += '--azureTrustedSignFile', $AzureTrustedSignFile }

vpk @args
if ($LASTEXITCODE) { throw 'vpk pack failed' }

# Previous versions' packages are only needed as delta inputs; ship just this version plus the feed indexes.
Get-ChildItem $OutDir -Filter *.nupkg | Where-Object { $_.Name -notlike "*-$Version-*" } | Remove-Item -Force
Get-ChildItem $OutDir -File | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
