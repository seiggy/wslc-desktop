# Terminal decision

The Shell tab needed a real TTY (colors, cursor keys, resize, `vi`/`top`/`less`). The shell is `wslc exec --interactive --tty`, run under a Windows pseudoconsole (ConPTY).

## Options considered

| Option | Verdict |
| --- | --- |
| Windows Terminal control (`Microsoft.Terminal.Control`) | Not published as a hostable WinUI 3 NuGet. It is built inside the Terminal repo and isn't a supported redistributable. Rejected. |
| `Microsoft.Terminal.Wpf` | WPF only, with a native renderer dependency. Doesn't host in WinUI 3. Rejected. |
| Own VT parser and renderer on ConPTY | Large surface (alt screen, mouse, IME, selection, scrollback, wide characters) and a long tail of bugs. Rejected. |
| **ConPTY + xterm.js in WebView2** | Chosen. xterm.js is MIT, mature (used by VS Code) and handles all of the above. ConPTY is the same pipe the GUIConsole sample uses. |

## Tradeoffs

- WebView2 is already required by Windows App SDK, so there is no new runtime. If it can't initialize, the old piped shell is used as a fallback.
- xterm.js 6.0.0 and addon-fit 0.11.0 are vendored as static files under `Terminal/web` (with their license) and copied as Content. There is **no new NuGet or npm dependency**, so the single-feed `nuget.config` and `.github/nuget.ci.config` restore are unchanged, and the x64, ARM64, MSIX and Velopack layouts just carry a folder of files.
- The raw VT stream bypasses secret redaction.
- ConPTY needs Windows 10 1809 or later, which the app already requires.
- A ConPTY child can inherit the parent's redirected standard handles; `ConPtySession` clears them around `CreateProcess` so this also works under a test host.

## Tests

`TerminalTests` covers command-line quoting, exec arguments, the web-message protocol, input encoding, palettes, and ConPTY output, exit code, input and resize.
