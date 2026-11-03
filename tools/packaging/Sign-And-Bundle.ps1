<#
.SYNOPSIS
  Signs per-architecture MSIX packages, combines them into an .appxbundle, signs it, and writes SHA256 checksums.
.DESCRIPTION
  Uses the PFX in -PfxPath (password from the PFX_PASSWORD environment variable) when supplied; otherwise
  creates a throwaway self-signed test certificate and exports its public .cer next to the packages.
  The certificate subject must equal the Publisher stamped into the packages.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Publisher,
    [string]$Directory = 'artifacts',
    [string]$PfxPath,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$Directory = (Resolve-Path $Directory).Path
if ($Version -notmatch '^v?(?<core>\d+\.\d+\.\d+)(?<pre>-[0-9A-Za-z.-]+)?$') { throw "Bad version '$Version'." }
$full = "$($Matches.core)$($Matches.pre)"
$bundleVersion = "$($Matches.core).0"

function Find-SdkTool([string]$name) {
    $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $hit = Get-ChildItem $root -Directory | Where-Object Name -Match '^10\.' | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $hit) { throw "$name not found in the Windows SDK." }
    $hit
}
$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'

$selfSigned = -not $PfxPath
$cert = $null
try {
    if ($selfSigned) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
            -FriendlyName 'WSLc Desktop test signing' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(1) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Export-Certificate -Cert $cert -FilePath (Join-Path $Directory "WslcDesktop-$full-test-signing.cer") | Out-Null
        $mode = 'self-signed (TEST ONLY)'
    }
    else {
        if (-not $env:PFX_PASSWORD) { throw 'PFX_PASSWORD is not set.' }
        $secure = ConvertTo-SecureString $env:PFX_PASSWORD -AsPlainText -Force
        $cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $secure
        if ($cert.Subject -ne $Publisher) {
            throw "Signing certificate subject '$($cert.Subject)' does not match the package Publisher '$Publisher'. Set the MSIX_PUBLISHER repository variable to the certificate subject."
        }
        $mode = 'signed'
    }

    function Sign([string]$file) {
        $signArgs = @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint)
        if (-not $selfSigned) { $signArgs += '/tr', $TimestampUrl, '/td', 'SHA256' }
        & $signtool @signArgs $file
        if ($LASTEXITCODE) { throw "signtool failed for $file" }
    }

    $msix = @(Get-ChildItem $Directory -Filter *.msix)
    if ($msix.Count -lt 1) { throw 'No .msix files to bundle.' }
    $msix | ForEach-Object { Sign $_.FullName }

    $stage = Join-Path $Directory 'bundle-stage'
    $null = New-Item -ItemType Directory -Force $stage
    $msix | Copy-Item -Destination $stage
    $bundle = Join-Path $Directory "WslcDesktop-$full.appxbundle"
    Remove-Item $bundle -ErrorAction SilentlyContinue
    & $makeappx bundle /d $stage /p $bundle /bv $bundleVersion /o
    if ($LASTEXITCODE) { throw 'makeappx bundle failed' }
    Remove-Item $stage -Recurse -Force
    Sign $bundle
}
finally {
    if ($cert) { Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue }
}

$sums = Get-ChildItem $Directory -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
Set-Content (Join-Path $Directory 'SHA256SUMS.txt') $sums

$note = if ($selfSigned) {
@"
> **Test-signed build.** The MSIX packages and bundle in this release are signed with a throwaway self-signed certificate
> (``$Publisher``) because no release signing certificate was configured. To install them, first trust
> ``WslcDesktop-$full-test-signing.cer`` (Local Machine > Trusted People), or use the portable EXE instead.
"@
} else { "MSIX packages and the bundle are signed with the project's release certificate (``$Publisher``)." }
Set-Content (Join-Path $Directory 'signing-notes.md') $note
Write-Host "Signing mode: $mode"
Get-Content (Join-Path $Directory 'SHA256SUMS.txt')
