<#
.SYNOPSIS
  Bundles unsigned x64 and ARM64 Microsoft Store MSIX packages and writes release checksums.
.DESCRIPTION
  Validates the packages against the Partner Center identity in Package.appxmanifest.
  No certificate is used. Microsoft signs the packages after Store certification.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Directory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$Directory = (Resolve-Path $Directory).Path
if ($Version -notmatch '^v?(?<core>\d+\.\d+\.\d+)(?<pre>-[0-9A-Za-z.-]+)?$') { throw "Bad version '$Version'." }
$full = "$($Matches.core)$($Matches.pre)"
$bundleVersion = "$($Matches.core).0"
$manifestPath = Join-Path $PSScriptRoot '..\..\src\WslcDesktop\Package.appxmanifest'
$expected = ([xml](Get-Content $manifestPath -Raw)).Package
$msix = @(Get-ChildItem $Directory -Filter *.msix -File)
if ($msix.Count -ne 2) { throw 'Expected exactly two unsigned MSIX packages: x64 and ARM64.' }

$architectures = foreach ($package in $msix) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        if ($archive.GetEntry('AppxSignature.p7x')) { throw "$($package.Name) is signed; Store build output must be unsigned." }
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "$($package.Name) has no AppxManifest.xml." }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $manifest = ([xml]$reader.ReadToEnd()).Package }
        finally { $reader.Dispose() }
        if ($manifest.Identity.Name -cne $expected.Identity.Name -or
            $manifest.Identity.Publisher -cne $expected.Identity.Publisher -or
            $manifest.Properties.PublisherDisplayName -cne $expected.Properties.PublisherDisplayName) {
            throw "$($package.Name) does not match the Partner Center identity in Package.appxmanifest."
        }
        if ($manifest.Identity.Version -cne $bundleVersion) {
            throw "$($package.Name) must have version $bundleVersion; found $($manifest.Identity.Version)."
        }
        $manifest.Identity.ProcessorArchitecture
    }
    finally { $archive.Dispose() }
}
if (@($architectures | Where-Object { $_ -eq 'x64' }).Count -ne 1 -or
    @($architectures | Where-Object { $_ -eq 'arm64' }).Count -ne 1) {
    throw "Expected one x64 and one ARM64 package; found: $($architectures -join ', ')."
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeappx = Get-ChildItem $sdkRoot -Directory | Where-Object Name -Match '^10\.' | Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe not found in the Windows SDK.' }

$stage = Join-Path $Directory 'bundle-stage'
if (Test-Path $stage) { throw "Remove the stale bundle staging directory before retrying: $stage" }
$null = New-Item -ItemType Directory $stage
try {
    $msix | Copy-Item -Destination $stage
    $bundle = Join-Path $Directory "WslcDesktop-$full.appxbundle"
    & $makeappx bundle /d $stage /p $bundle /bv $bundleVersion /o
    if ($LASTEXITCODE) { throw 'makeappx bundle failed' }
}
finally { Remove-Item $stage -Recurse -Force }

@"
> **Microsoft Store submission packages.** The MSIX packages and appxbundle are unsigned and use the app's
> Partner Center identity. Upload the appxbundle to Partner Center; Microsoft signs it after certification.
> These downloads are not sideload installers. For direct use outside the Store, download the portable EXE.
"@ | Set-Content (Join-Path $Directory 'release-notes.md')

$sums = Get-ChildItem $Directory -File | Where-Object Name -NotIn @('SHA256SUMS.txt', 'release-notes.md') |
    Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
    }
Set-Content (Join-Path $Directory 'SHA256SUMS.txt') $sums
Get-Content (Join-Path $Directory 'SHA256SUMS.txt')
