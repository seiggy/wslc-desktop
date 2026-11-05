# Contributing and building from source

## Build the app

You need Windows 11 22H2 (build 22621) or later, WSL with container support and the .NET SDK the repository targets (see the README and `src/WslcDesktop/*.csproj`; the PR build workflow documents the SDK used in CI).

```powershell
dotnet build src\WslcDesktop -p:Platform=x64 -r win-x64
```

Run `src\WslcDesktop\bin\x64\Debug\...\win-x64\WslcDesktop.exe`. Close any running instance first: only one instance can use the `WslcDesktop` session.

Code layout:

- `src/WslcDesktop`: the WinUI 3 app (views, controls, and composition of Core and SDK services).
- `src/WslcDesktop.Core`: UI-free domain logic, CLI adapters, settings, and session operations.
- `src/WslcDesktop.Sdk`: native SDK integration for owned sessions, with a CLI backend fallback for unsupported operations.
- `src/WslcDesktop.Localization`: shared UI resources and display-language lookup without UI dependencies.
- `src/WslcDesktop.Compose`: Compose parsing, execution planning, and the runner.
- `tests`: xUnit projects for Core/app logic and Compose, plus opt-in SDK integration tests against real WSL.
- `assets/branding`: logo and brand assets.

## Shared build settings and dependencies

The root `Directory.Build.props` enables nullable reference types, implicit usings,
and warnings-as-errors for all projects. MSBuild automatically imports it early;
keep project-specific target frameworks, platform settings, and output types in
their respective project files. Fix build warnings rather than suppressing them.

NuGet Central Package Management is enabled in the root `Directory.Packages.props`.
Declare package references without versions in project files and maintain exact
versions in the central file. Transitive pinning is enabled, so a central
`PackageVersion` can also pin a transitive dependency when addressing a vulnerability.
Pins must satisfy the dependency's minimum version; NuGet rejects a downgrade.
Restore through the repository's `nuget.config` after changing package versions.

```powershell
dotnet restore WslcDesktop.slnx
dotnet build WslcDesktop.slnx --no-restore
```

## UI translations

The English source catalog is `src/WslcDesktop.Localization/UiStrings.resx`.
The neighboring `UiStrings.es.resx`, `.fr.resx`, `.de.resx`, `.it.resx`, `.ja.resx`,
and `.zh-Hans.resx` files contain Spanish, French, German, Italian, Japanese,
and Simplified Chinese translations. These initial translations are machine-generated
and should be reviewed by native speakers before being treated as final.

The app follows the current Windows display language, including regional fallback
(for example, Spanish in Mexico uses the Spanish catalog). Unsupported languages
fall back to English. Restart the app after changing the Windows display language.

Use stable semantic resource keys and add translator comments where context matters.
In C#, use `UiStrings.Get` or `UiStrings.Format`; in XAML, use
`{localization:Localized Key=ResourceKey}` with
`xmlns:localization="using:WslcDesktop.Localization"`.
Preserve numbered format arguments such as `{0}` in every translation. Do not
translate commands, protocol values, user-supplied names, paths, or raw process output.
When adding a language, add its satellite `.resx` catalog and extend the localization
tests. Missing resource keys fail explicitly instead of silently displaying blank UI.

## Build these docs

The docs use [VitePress](https://vitepress.dev) and need Node.js 20 or newer.

```powershell
cd docs
npm install
npm run dev      # live preview
npm run build    # production build into docs/.vitepress/dist
```

The site is served from `/wslc-desktop/` on GitHub Pages. To preview with a different base, set `DOCS_BASE` (for example `$env:DOCS_BASE='/'`).

Pages are Markdown files in `docs/`; screenshots live in `docs/public/screenshots`. Capture new ones at the same window size (about 1280×820), dark theme, with no personal information or credentials visible.

## Pull requests

Open a pull request against `main`. Pull requests that touch `docs/` run a docs build; merges to `main` publish the site.
