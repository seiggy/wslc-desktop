# Releasing

Releases are built by `.github/workflows/release.yml`.

**Cut a release:** push a tag (`git tag v1.2.3 && git push origin v1.2.3`) or run the *Release* workflow manually with a version
(`1.2.3` or `1.2.3-beta.1`; pre-release suffixes mark the GitHub Release as a pre-release). Untick *create_release* to build artifacts only.

## Artifacts

| File | Description |
| --- | --- |
| `WslcDesktop-<v>-win-x64-portable.exe`, `…-win-arm64-portable.exe` | Single-file, self-contained unpackaged build. Run the EXE directly; no companion files or runtime installation required. |
| `WslcDesktop-<v>-x64.msix`, `…-arm64.msix` | Unsigned Microsoft Store MSIX packages; not sideload installers. |
| `WslcDesktop-<v>.appxbundle` | Unsigned Store submission bundle containing both architectures. Upload this file to Partner Center; the individual MSIX files need not be uploaded as well. |
| `SHA256SUMS.txt` | Checksums of every file. |

Package version is `<major>.<minor>.<patch>.0` (the tag), stamped into the manifest and assembly.

## Microsoft Store submission

The release workflow builds unsigned packages using the Partner Center identity in `src/WslcDesktop/Package.appxmanifest`:

| Partner Center field | Value |
| --- | --- |
| Package/Identity/Name | `seiggy.DesktopManagerforWSLc` |
| Package/Identity/Publisher | `CN=CE168BCD-6F3C-4900-89EA-A9A764FE595F` |
| Package/Properties/PublisherDisplayName | `seiggy` |
| Store ID | `9N9HFWX6046M` |

Download `WslcDesktop-<v>.appxbundle` from the GitHub Release or the `release-files` workflow artifact and upload it in
the app's Partner Center submission. PR builds also provide a `store-bundle` workflow artifact for validation, built with version `1.0.0.0`;
use a release build with the intended version for submission. Complete the listing, declarations, and certification steps in Partner Center.
Microsoft signs the packages after certification; no purchased certificate, PFX, or test-certificate trust setup is needed.
Store review also needs justification for the restricted capabilities below. The package family name and SID are derived from the identity,
not stamped separately into the manifest.

The GitHub MSIX packages and bundle are submission artifacts, not directly installable unsigned packages. For direct downloads, use the
portable EXE. The workflow no longer generates sideload packages or self-signed certificates, and no longer reads `MSIX_CERT_PFX_BASE64`,
`MSIX_CERT_PASSWORD`, or `MSIX_PUBLISHER`; any previously configured values can be removed from repository settings.
Store signing does not sign the portable EXE. Older GitHub releases retain their original test-signed packages.

See Microsoft's [MSIX Store submission requirements](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements).

## Packaging notes

- Unpackaged is still the default (`WindowsPackageType=None`); the release script builds the MSIX with `-p:WindowsPackageType=MSIX`.
- Portable publishing uses `Properties/PublishProfiles/Portable.pubxml`: `PublishSingleFile`, `IncludeAllContentForSelfExtract`,
  .NET self-contained, and Windows App SDK self-contained. WinUI requires extraction of the bundled native runtime, PRI, and assets.
  This is one file to distribute, not a zero-extraction executable. The .NET host caches the extracted files under
  `%TEMP%\.net` by default (or `DOTNET_BUNDLE_EXTRACT_BASE_DIR` when configured). First launch needs writable space there.
  App settings and container data continue to live under `%LOCALAPPDATA%`; WSLc remains an external prerequisite.
- Portable builds embed debug symbols; both PR and release builds reject any extra files in the portable output.
  MSIX builds do not enable single-file publishing.
- To publish just the portable EXE locally:
  `dotnet publish src\WslcDesktop -c Release -p:Platform=x64 -r win-x64 -p:PublishProfile=Portable -p:DebugType=embedded -p:RestoreConfigFile=.github\nuget.ci.config -o out\portable`.
  See Microsoft's [single-file WinUI deployment requirements](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe).
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
  identities or versions, and missing or duplicate architectures before creating the unsigned bundle.
