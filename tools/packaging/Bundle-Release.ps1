<#
.SYNOPSIS
  Writes download guidance and checksums for public release files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Directory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$Directory = (Resolve-Path $Directory).Path
if ($Version -notmatch '^v?\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Bad version '$Version'." }
$files = @(Get-ChildItem $Directory -File | Where-Object Name -NotIn @('SHA256SUMS.txt', 'release-notes.md'))
if (-not $files) { throw 'No release files found.' }
if ($files | Where-Object Extension -In @('.msix', '.appx', '.appxbundle', '.msixbundle', '.appxupload', '.msixupload')) {
    throw 'Store submission packages must not be included in public releases. Use the Store packages workflow.'
}

@'
> **Direct downloads:** use `WslcDesktopManager-win-<architecture>-Setup.exe` (recommended) or
> `WslcDesktop-<version>-win-<architecture>-portable.zip` for x64 or ARM64.
> The `.nupkg` files and channel indexes support in-app updates; they are not manual installers.
> Microsoft Store submission packages are built separately and are not attached to releases.
'@ | Set-Content (Join-Path $Directory 'release-notes.md')

$sums = $files | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
Set-Content (Join-Path $Directory 'SHA256SUMS.txt') $sums
Get-Content (Join-Path $Directory 'SHA256SUMS.txt')
