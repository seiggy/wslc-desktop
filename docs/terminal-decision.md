# Terminal decision

The Shell tab needed a real TTY (colors, cursor keys, resize, `vi`/`top`/`less`). The shell is `wslc exec --interactive --tty`, run under a Windows pseudoconsole (ConPTY) and drawn by the real Windows Terminal rendering engine. No WebView2 is used.

## Options considered

| Option | Verdict |
| --- | --- |
| **Native `HwndTerminal` (`Microsoft.Terminal.Control.dll`, the engine behind `Microsoft.Terminal.Wpf`) hosted as a child HWND** | Chosen. Real Windows Terminal renderer: GPU text, selection, scrollback, mouse, VT. Works in WinUI 3. |
| WinUI 3 `TermControl` (WinRT) | No maintained package. The only one (`WindowsTerminal.WinUI3.Control` 1.11, 2022) is stale. Building it from the Terminal repo isn't redistributable from here. |
| XAML Island | Wrong direction: islands host XAML inside Win32/WPF. WinUI 3 is already XAML, so the realistic route is hosting a native HWND. |
| xterm.js in WebView2 (PR #20) | Worked, but the user rejected an embedded WebView2. Kept on branch `seiggy-conpty-terminal-shell`. |
| Own VT parser and renderer | Large surface and a long tail of bugs. Rejected. |

## How it works

- Package `CI.Microsoft.Terminal.Wpf` 1.25 is referenced with all assets excluded. Only `runtimes\{win-x64,win-arm64}\native\Microsoft.Terminal.Control.dll` is copied next to the exe (per platform), so no WPF assemblies ship.
- `NativeTerminal` P/Invokes `CreateTerminal`, `TerminalSendOutput`, `TerminalTriggerResize`, `TerminalSetTheme`, key/char events and selection. This packaged build predates the dispatcher-callout variant of `CreateTerminal` in main: it takes `(parent, out hwnd, out terminal)`.
- `TerminalView` creates the child HWND under the window and positions it over a XAML placeholder (scaled by `RasterizationScale`), hiding it when the tab isn't visible. A window subclass forwards key, char, focus and size messages to the control and resizes the ConPTY to match.
- Ctrl+C copies only while text is selected (otherwise it is SIGINT). Ctrl+V, Shift+Insert and Ctrl+Shift+V paste.

## Risks and tradeoffs

- **Licensing:** the Terminal source is MIT, but the NuGet is a repackage by "CI2Nuget Repackage Team" with no license metadata. Confirm redistribution rights before shipping.
- **Feed:** restores from the internal feed; it is also on nuget.org, so `.github/nuget.ci.config` should restore it. The repo `nuget.config` is unchanged.
- **Airspace:** a child HWND draws above XAML, so a flyout or combo dropdown overlapping the terminal area renders behind it.
- **Version:** the 1.25 DLL has no dispatcher callouts and may differ from newer Terminal builds. Bumping it needs a re-check of the exports.
- Raw VT output bypasses secret redaction. ConPTY needs Windows 10 1809 or later.
- If the DLL fails to load, the old piped shell is used.

## Tests

`TerminalTests` covers command-line quoting, exec arguments, input encoding, palettes, and ConPTY output, exit code, input and resize. Rendering was verified by hand in the app (`ls --color`, `vi`, Ctrl+C, resize).