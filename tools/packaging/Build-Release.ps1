<#
.SYNOPSIS
  Builds the portable (self-contained) zip and the unsigned per-architecture MSIX for one platform.
.EXAMPLE
  ./tools/packaging/Build-Release.ps1 -Arch x64 -Version 1.2.3 -Publisher "CN=Contoso" -OutDir artifacts
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [Parameter(Mandatory)][string]$Version,
    [string]$Publisher = 'CN=WSLc Desktop Test',
    [string]$OutDir = 'artifacts',
    [string]$NuGetConfig
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repo 'src\WslcDesktop\WslcDesktop.csproj'
$projDir = Split-Path $project
$manifest = Join-Path $projDir 'Package.appxmanifest'
$null = New-Item -ItemType Directory -Force $OutDir
$OutDir = (Resolve-Path $OutDir).Path

if ($Version -notmatch '^v?(?<core>\d+\.\d+\.\d+)(?<pre>-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' must look like 1.2.3 or 1.2.3-beta.1."
}
$core = $Matches.core
$full = "$core$($Matches.pre)"
$msixVersion = "$core.0"
$platform = if ($Arch -eq 'x64') { 'x64' } else { 'ARM64' }
$rid = "win-$Arch"

if (-not $NuGetConfig -and (Select-String -Path (Join-Path $repo 'nuget.config') -Pattern 'packagefeedproxy|pkgs\.dev\.azure|pkgs\.visualstudio' -Quiet)) {
    Write-Host '::warning::nuget.config points at an internal feed; pass -NuGetConfig with a public-feed config in CI.'
}
$restoreArgs = @()
if ($NuGetConfig) { $restoreArgs += '--configfile', $NuGetConfig }

Write-Host "== Restore ($rid)"
dotnet restore $project -r $rid "-p:Platform=$platform" @restoreArgs
if ($LASTEXITCODE) { throw 'restore failed' }

function Reset-BuildOutput {
    Remove-Item (Join-Path $projDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $projDir "obj\$platform") -Recurse -Force -ErrorAction SilentlyContinue
}

$common = @('-c', 'Release', "-p:Platform=$platform", '-r', $rid, '--no-restore', '-nologo', '-v:m',
    "-p:Version=$core", "-p:InformationalVersion=$full")

# WinUI 3 does not support true single-file publishing, so the portable build is one
# self-contained folder that is zipped.
Write-Host "== Portable ($rid)"
Reset-BuildOutput
$pub = Join-Path $OutDir "portable-$Arch"
dotnet publish $project @common --self-contained -p:WindowsPackageType=None -o $pub
if ($LASTEXITCODE) { throw 'publish failed' }
$zip = Join-Path $OutDir "WslcDesktop-$full-win-$Arch-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $pub -Recurse -Force

# The manifest is stamped for the build and restored afterwards so the working tree stays clean.
Write-Host "== MSIX ($rid)"
Reset-BuildOutput
$original = Get-Content $manifest -Raw
$pkgDir = Join-Path $OutDir "msix-$Arch"
try {
    [xml]$xml = $original
    $xml.Package.Identity.Version = $msixVersion
    $xml.Package.Identity.Publisher = $Publisher
    $xml.Save($manifest)
    dotnet build $project @common -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true `
        -p:AppxBundle=Never -p:AppxPackageSigningEnabled=false -p:AppxSymbolPackageEnabled=false "-p:AppxPackageDir=$pkgDir\"
    if ($LASTEXITCODE) { throw 'msix build failed' }
}
finally {
    Set-Content $manifest $original -NoNewline
}
$msix = Get-ChildItem $pkgDir -Recurse -Filter *.msix | Select-Object -First 1
if (-not $msix) { throw 'No .msix produced.' }
Move-Item $msix.FullName (Join-Path $OutDir "WslcDesktop-$full-$Arch.msix") -Force
Remove-Item $pkgDir -Recurse -Force
Reset-BuildOutput

Get-ChildItem $OutDir -File | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
