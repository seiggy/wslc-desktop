# Releasing

Releases are built by `.github/workflows/release.yml`.

**Cut a release:** push a tag (`git tag v1.2.3 && git push origin v1.2.3`) or run the *Release* workflow manually with a version
(`1.2.3` or `1.2.3-beta.1`; pre-release suffixes mark the GitHub Release as a pre-release). Untick *create_release* to build artifacts only.

## Artifacts

| File | Description |
| --- | --- |
| `WslcDesktopManager-win-x64-Setup.exe`, `…-win-arm64-Setup.exe` | **Recommended.** Velopack installer (unsigned): per-user install to `%LOCALAPPDATA%\WslcDesktopManager`, Start menu/desktop shortcuts, in-app updates. |
| `WslcDesktopManager-<v>-win-<arch>-full.nupkg`, `…-delta.nupkg` | Velopack update packages. Installed apps download these from the GitHub Release. Deltas exist when a previous release is available. |
| `RELEASES-win-<arch>`, `releases.win-<arch>.json`, `assets.win-<arch>.json` | Velopack update feed for the `win-x64` / `win-arm64` channels. Required for in-app updates; don't delete. |
| `WslcDesktopManager-win-<arch>-Portable.zip` | Velopack portable bundle (needs no install; updates only via the installed app). |
| `WslcDesktop-<v>-win-x64-portable.zip`, `…-win-arm64-portable.zip` | Plain self-contained folder, zipped. Extract and run `WslcDesktop.exe`. No self-extraction, so it starts immediately. No auto-update. |
| `WslcDesktop-<v>-x64.msix`, `…-arm64.msix` | Unsigned Microsoft Store MSIX packages; not sideload installers. |
| `WslcDesktop-<v>.appxbundle` | Unsigned Store submission bundle containing both architectures. Upload this file to Partner Center; the individual MSIX files need not be uploaded as well. |
| `SHA256SUMS.txt` | Checksums of every file. |

Package version is `<major>.<minor>.<patch>.0` (the tag), stamped into the manifest and assembly.
The minimum supported OS is Windows 11 22H2 (`10.0.22621.0`), declared in both the project and MSIX manifest.
MakeAppx carries each MSIX package's minimum into the bundle automatically. SDK targeting and `MaxVersionTested` remain `10.0.26100.0`.

## Microsoft Store submission

The release workflow builds unsigned packages using the Partner Center identity in `src/WslcDesktop/Package.appxmanifest`:

| Partner Center field | Value |
| --- | --- |
| Reserved app name / Package/Properties/DisplayName | `WSLc Desktop Manager` |
| Package/Identity/Name | `seiggy.DesktopManagerforWSLc` |
| Package/Identity/Publisher | `CN=CE168BCD-6F3C-4900-89EA-A9A764FE595F` |
| Package/Properties/PublisherDisplayName | `seiggy` |
| Store ID | `9N9HFWX6046M` |

Download `WslcDesktop-<v>.appxbundle` from the GitHub Release or the `release-files` workflow artifact and upload it in
the app's Partner Center submission. PR builds also provide a `store-bundle` workflow artifact for validation, built with version `1.0.0.0`;
use a release build with the intended version for submission. Complete the listing, declarations, and certification steps in Partner Center.
Microsoft signs the packages after certification; no purchased certificate, PFX, or test-certificate trust setup is needed.
Both the package display name and application visual-elements display name must match the reserved app name above.
After the documentation site has deployed this policy, use `https://seiggy.github.io/wslc-desktop/privacy` for the
Partner Center privacy policy URL. The canonical policy is `PRIVACY.md`; the docs page includes it so both stay in sync.
Store review also needs justification for the restricted capabilities below. The package family name and SID are derived from the identity,
not stamped separately into the manifest.

The GitHub MSIX packages and bundle are submission artifacts, not directly installable unsigned packages. For direct downloads, use the
Velopack `Setup.exe` or the portable zip. The workflow no longer generates sideload packages or self-signed certificates, and no longer reads `MSIX_CERT_PFX_BASE64`,
`MSIX_CERT_PASSWORD`, or `MSIX_PUBLISHER`; any previously configured values can be removed from repository settings.
Store signing does not sign the Velopack or portable files (unsigned until a certificate is available). Older GitHub releases retain their original test-signed packages.

See Microsoft's [MSIX Store submission requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements).

### Restricted capability justifications

Partner Center requests a separate explanation for each restricted capability. Use the following text for the corresponding fields.
These declarations support desktop execution and WSLc interoperability; they do not themselves grant administrator privileges or bypass Windows access controls.

#### runFullTrust

WSLc Desktop Manager is a native WinUI 3 desktop application that provides a graphical interface for Microsoft WSLc.
It uses the Microsoft.WSL.Containers API and launches the locally installed wslc.exe command-line tool to create and manage
WSLc sessions and containers, manage images, volumes and networks, and run Compose stacks. It also reads container logs
and resource statistics and provides interactive shells inside containers. The runFullTrust capability is required to run
the packaged desktop executable and support these native API and command-line integrations outside an AppContainer.
The application runs in the current Windows user's context; this capability does not itself grant administrator privileges.

#### unvirtualizedResources

WSLc Desktop Manager must share local configuration and container storage with the independently installed WSLc command-line
tool and service, which operate outside the app's MSIX package. The app reads and writes
%LOCALAPPDATA%\wslc\settings.yaml for WSLc's default-session configuration and supplies
%LOCALAPPDATA%\WslcDesktop\Data as storage for its own WslcDesktop session.
MSIX file-system write virtualization can redirect newly created files into package-private storage that these external
processes cannot see, causing inconsistent settings or inaccessible session data. The unvirtualizedResources capability,
together with disabled file-system write virtualization, keeps these files at their real per-user locations so the app
and WSLc can use the same configuration and storage. Container data is intended to remain available independently of the
GUI installation. Access remains subject to the current user's Windows permissions.

## Velopack installer and updates

[Velopack](https://docs.velopack.io/) provides the direct-download installer and auto-update; MSIX/appxbundle stay for the Store.

- `Program.Main` calls `VelopackApp.Build().Run()` before anything else; it handles install/update/uninstall hooks.
- `UpdateService` checks the GitHub Release feed (`GithubSource`, prereleases only when the installed build is a prerelease),
  downloads, and applies on restart. UI: **Settings → Updates**. It shows "managed elsewhere" for Store, portable-zip and dev builds.
- `Pack-Velopack.ps1` runs `vpk pack` per architecture on the publish folder `Build-Release.ps1` leaves in `publish\win-<arch>`.
  Each architecture uses its own channel (`win-x64`, `win-arm64`) so feeds never mix. The release workflow first runs `vpk download github`
  to fetch the previous release (for delta packages), packs, and uploads the files with the other assets via `gh release`.
- The pack id is `WslcDesktopManager` so the install folder (`%LOCALAPPDATA%\WslcDesktopManager`) never overlaps
  `%LOCALAPPDATA%\WslcDesktop`, which holds app settings and container storage. Don't rename it to `WslcDesktop`:
  uninstalling would then delete user data.
- Local test feed: set `WSLC_UPDATE_FEED` to a folder (or URL) of vpk output to make an installed app update from it instead of GitHub.

### Signing later

Releases are unsigned, so Windows SmartScreen shows "Windows protected your PC" on first run (More info → Run anyway).
To sign once a certificate exists, pass signing arguments to `Pack-Velopack.ps1`; it forwards them to `vpk pack`, which signs
every executable and the Setup.exe: `-SignParams '/td sha256 /fd sha256 /f cert.pfx /p <password>'` (signtool) or
`-AzureTrustedSignFile metadata.json` (Azure Trusted Signing). In `release.yml`, add the flag to the *Pack Velopack* step
and supply secrets from repository secrets; no other change is needed. See Velopack's [code signing docs](https://docs.velopack.io/packaging/signing).

### Why the portable build is a folder, not a single EXE

The previous single-file portable EXE (~245 MB) self-extracts ~225 MB to `%TEMP%\.net` on launch. Measured on a dev machine:
a warm run showed its window in ~0.5 s, but a never-before-seen file took ~9 s (Defender scanning the download plus extraction),
and ~8 s again on a second run, with no UI during that time. Managed code cannot show a splash before extraction finishes.
The zipped folder starts in ~0.6 s with the same files, so it replaced the single-file EXE.

## Packaging notes

- Unpackaged is still the default (`WindowsPackageType=None`); the release script builds the MSIX with `-p:WindowsPackageType=MSIX`.
- Portable publishing uses `Properties/PublishProfiles/Portable.pubxml` (self-contained .NET and Windows App SDK, not single-file).
  App settings and container data live under `%LOCALAPPDATA%`; WSLc remains an external prerequisite.
- To publish just the portable folder locally:
  `dotnet publish src\WslcDesktop -c Release -p:Platform=x64 -r win-x64 -p:PublishProfile=Portable -p:DebugType=embedded -p:RestoreConfigFile=.github\nuget.ci.config -o out\portable`.
- Local Velopack build: `dotnet tool install -g vpk --version 1.2.161`, run `Build-Release.ps1`, then
  `.\tools\packaging\Pack-Velopack.ps1 -Arch x64 -Version 1.0.0 -OutDir out\velopack`.
- Capabilities: `runFullTrust` (WinUI 3 desktop app that starts `wslc.exe` and uses the WSLc API) and `unvirtualizedResources`.
  The manifest sets `desktop6:FileSystemWriteVirtualization` and `RegistryWriteVirtualization` to `disabled`, so
  `%LOCALAPPDATA%\wslc\settings.yaml` and `%LOCALAPPDATA%\WslcDesktop\` are the real paths shared with the CLI and the WSLc service,
  not redirected into the package's `LocalCache`.
- Packaged builds restore from nuget.org in CI (the repo `nuget.config` points at an internal feed).
- Local release build (requires the Windows SDK's `makeappx`, but no signing certificate):
  ```powershell
  .\tools\packaging\Build-Release.ps1 -Arch x64 -Version 1.0.0 -OutDir out -NuGetConfig .github\nuget.ci.config
  .\tools\packaging\Build-Release.ps1 -Arch arm64 -Version 1.0.0 -OutDir out -NuGetConfig .github\nuget.ci.config
  .\tools\packaging\Bundle-Release.ps1 -Version 1.0.0 -Directory out
  ```
  Build architectures sequentially because the script resets shared build output. The bundler rejects signed packages, mismatched Store
  identities, display names, versions, minimum OS versions or device families, and missing or duplicate architectures before creating the unsigned bundle.
