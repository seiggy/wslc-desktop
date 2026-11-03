# Contributing and building from source

## Build the app

You need Windows 11 22H2 (build 22621) or later, WSL with container support and the .NET SDK the repository targets (see the README and `src/WslcDesktop/*.csproj`; the PR build workflow documents the SDK used in CI).

```powershell
dotnet build src\WslcDesktop -p:Platform=x64 -r win-x64
```

Run `src\WslcDesktop\bin\x64\Debug\...\win-x64\WslcDesktop.exe`. Close any running instance first: only one instance can use the `WslcDesktop` session.

Code layout:

- `src/WslcDesktop`: the WinUI 3 app (views, controls, services).
- `src/WslcDesktop.Compose`: the compose parser and planner.
- `tests`: automated tests.
- `assets/branding`: logo and brand assets.

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
