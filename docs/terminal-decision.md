# Terminal decision

The Shell tab needs a real TTY (colors, cursor keys, resize, `vi`/`top`/`less`). The shell is `wslc exec --interactive --tty`, run under a Windows pseudoconsole (ConPTY). The output is parsed by a managed VT engine and drawn natively in the XAML tree with Win2D. There is no WebView2 and no child window, so nothing can overlay other content (no airspace problems).

## Options considered

| Option | Verdict |
| --- | --- |
| **Managed VT parser and screen buffer (`VtScreen`) drawn on a Win2D `CanvasControl`** | Chosen. Pure XAML: clips, themes, scales and layers like any control. The VT logic is UI-free and unit tested. |
| Native `HwndTerminal` child HWND (`Microsoft.Terminal.Control.dll`) | Dropped. Airspace overlay bugs and a repackaged DLL with no license metadata. Kept on branch `seiggy-wt-control-shell`. |
| xterm.js in WebView2 (PR #20) | Worked, but an embedded WebView2 was rejected. Kept on `seiggy-conpty-terminal-shell`. |
| WinUI 3 `TermControl` (WinRT) | No maintained package. |
| XAML Island | Wrong direction: islands host XAML inside Win32/WPF. |

## How it works

- `ConPtySession` owns the pseudoconsole: pipe pairs, `STARTUPINFOEX` with `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`, a reader thread, a dedicated writer thread (UI input never blocks on the pipe), `ResizePseudoConsole`, and a non-blocking `ClosePseudoConsole` shutdown.
- `VtScreen` handles SGR (16, 256 and truecolor), cursor movement, erase, insert/delete, scroll regions, scrollback, the alternate screen, wide characters, line drawing, OSC titles, and answers DSR/DA queries that ConPTY sends. It tracks application cursor keys, bracketed paste and mouse modes.
- `VtInput` encodes keys, paste and mouse reports. `TerminalView` draws the grid and handles selection, copy/paste (Ctrl+C copies only with a selection, otherwise it is SIGINT; Ctrl+V, Ctrl+Shift+V, Shift+Insert paste; right-click copies or pastes), wheel and Shift+PageUp/Down scrollback, and resizing.
- Font family and size come from Settings > Terminal. Colors follow the app theme.
- If the Win2D surface can't initialise, the old piped shell is used.

## ConPTY audit against MiniTerm and the pseudoconsole blog

Matches: pipe pairs, attribute list, thread-handle close, resize, reader thread, UTF-8 decoding across chunks. Deviations fixed: input was written on the UI thread (now a writer thread); shutdown ordering and handle ownership were tightened. The temporary clearing of the parent's standard handles is kept because a console-app parent (such as a test host) otherwise passes its redirected handles to the child; a GUI app has none, so it is a no-op there.

## Risks

- Win2D (`Microsoft.Graphics.Win2D`, MIT) restores through the single repo feed; x64 and ARM64 packaging still needs a check.
- The VT engine is deliberately minimal (no sixel, no hyperlinks, no IME composition UI).
- Raw VT output bypasses secret redaction. ConPTY needs Windows 10 1809 or later.

## Tests

`VtScreenTests`, `VtInputTests` and `TerminalTests` (command line, palettes, ConPTY output, exit code, input and resize). Interactive rendering validation (`ls --color`, `vi`, `top`, Ctrl+C, resize, copy/paste) is pending until UI automation is allowed.
