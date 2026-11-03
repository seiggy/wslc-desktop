<#
.SYNOPSIS
  Builds the portable (self-contained, single-file) EXE and unsigned Microsoft Store MSIX for one platform.
.EXAMPLE
  ./tools/packaging/Build-Release.ps1 -Arch x64 -Version 1.2.3 -OutDir artifacts
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [Parameter(Mandatory)][string]$Version,
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
dotnet restore $project -r $rid "-p:Platform=$platform" -p:SelfContained=true @restoreArgs
if ($LASTEXITCODE) { throw 'restore failed' }

function Reset-BuildOutput {
    Remove-Item (Join-Path $projDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $projDir "obj\$platform") -Recurse -Force -ErrorAction SilentlyContinue
}

$common = @('-c', 'Release', "-p:Platform=$platform", '-r', $rid, '--no-restore', '-nologo', '-v:m',
    "-p:Version=$core", "-p:InformationalVersion=$full")

# WinUI's single-file support bundles all content and extracts it at launch.
Write-Host "== Portable ($rid)"
Reset-BuildOutput
$pub = Join-Path $OutDir "portable-$Arch"
dotnet publish $project @common -p:PublishProfile=Portable -p:DebugType=embedded -o $pub
if ($LASTEXITCODE) { throw 'publish failed' }
$files = @(Get-ChildItem $pub -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'WslcDesktop.exe') {
    throw "Portable publish must contain only WslcDesktop.exe; found: $($files.Name -join ', ')"
}
Move-Item $files[0].FullName (Join-Path $OutDir "WslcDesktop-$full-win-$Arch-portable.exe") -Force
Remove-Item $pub -Recurse -Force

# Preserve the Partner Center identity and stamp only the release version.
Write-Host "== MSIX ($rid)"
Reset-BuildOutput
$original = Get-Content $manifest -Raw
$pkgDir = Join-Path $OutDir "msix-$Arch"
try {
    [xml]$xml = $original
    $xml.Package.Identity.Version = $msixVersion
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
