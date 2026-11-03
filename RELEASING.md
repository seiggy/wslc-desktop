# Releasing

Releases are built by `.github/workflows/release.yml`.

**Cut a release:** push a tag (`git tag v1.2.3 && git push origin v1.2.3`) or run the *Release* workflow manually with a version
(`1.2.3` or `1.2.3-beta.1`; pre-release suffixes mark the GitHub Release as a pre-release). Untick *create_release* to build artifacts only.

## Artifacts

| File | Description |
| --- | --- |
| `WslcDesktop-<v>-win-x64-portable.exe`, `…-win-arm64-portable.exe` | Single-file, self-contained unpackaged build. Run the EXE directly; no companion files or runtime installation required. |
| `WslcDesktop-<v>-x64.msix`, `…-arm64.msix` | Per-architecture MSIX. |
| `WslcDesktop-<v>.appxbundle` | Bundle of both MSIX packages. `.appxbundle` and `.msixbundle` are the same format (identical `AppxBundleManifest.xml`); only the extension differs. |
| `SHA256SUMS.txt` | Checksums of every file. |
| `WslcDesktop-<v>-test-signing.cer` | Only present for test-signed builds (see below). |

Package version is `<major>.<minor>.<patch>.0` (the tag), stamped into the manifest and assembly.

## Signing (user action required)

Add these in *Settings → Secrets and variables → Actions*:

- Secret `MSIX_CERT_PFX_BASE64`: base64 of a code-signing `.pfx` (`[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx'))`).
- Secret `MSIX_CERT_PASSWORD`: the PFX password.
- Variable `MSIX_PUBLISHER`: the certificate subject exactly, e.g. `CN=Zack Way, O=..., C=US`. The build fails if it doesn't match the cert.

Without the secrets, packages are signed with a throwaway self-signed certificate (subject `CN=WSLc Desktop Test` unless `MSIX_PUBLISHER` is set)
and the release notes say so. Users must trust the attached `.cer` (Local Machine → Trusted People) to install them; the portable EXE needs no certificate.
Package identity (`seiggy.WslcDesktop`) and `PublisherDisplayName` live in `src/WslcDesktop/Package.appxmanifest`; for Microsoft Store submission
replace them with the values from Partner Center (the Store re-signs the package). Store review also needs justification for the restricted
capabilities below.

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
- Local run: `pwsh tools/packaging/Build-Release.ps1 -Arch x64 -Version 0.0.1 -OutDir out`, then `Sign-And-Bundle.ps1`
  (needs the Windows SDK's `makeappx`/`signtool`).
