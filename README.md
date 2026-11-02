# wslc-desktop

<p align="center"><img src="assets/branding/wslc-desktop-logo.svg" alt="WSLc Desktop logo: a stack of image layers with a small penguin" width="160"></p>

Desktop App for managing WSLc for users familiar with that other product

**Documentation:** <https://seiggy.github.io/wslc-desktop/> — getting started, feature walkthroughs with screenshots, a compose guide, and troubleshooting.

## Build

Requires the .NET 10 SDK and WSL with container support.

```
dotnet build src\WslcDesktop -p:Platform=x64
```

The app is a WinUI 3 (Windows App SDK) wrapper over the
[`Microsoft.WSL.Containers`](https://wsl.dev/api-reference/csharp/) API. The API can't
enumerate containers or sessions, so the app lists them through `wslc.exe` (which must be on
`PATH`) and performs all other operations through the API.
