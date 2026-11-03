# Troubleshooting

## "Couldn't start the WSLc session"

![Couldn't start the WSLc session](/screenshots/troubleshooting-session-error.png)

The app couldn't create or attach to its `WslcDesktop` session, for example with *Cannot create a file when that file already exists*.

1. **Run only one instance of the app at a time.** Two instances compete for the same session. Close every WSLc Desktop Manager window (check Task Manager for stray `WslcDesktop.exe` processes), then choose **Retry** or relaunch.
2. Check the [Environment](/features/environment) page or run `wslc system session list` to see whether a stale `WslcDesktop` session is still registered.
3. Make sure WSL is up to date (`wsl --update`) and that `wslc --help` works in a terminal.

## Install components button

If the app offers **Install components**, the WSLc runtime isn't installed. Use the button, wait for it to finish, then choose **Retry**.

## My containers aren't listed

The app only shows its own session. Containers from the `wslc` CLI's default session live elsewhere. See [Environment](/features/environment) and use `wslc --session WslcDesktop ...` from a terminal.

## A published port doesn't respond

Ports bind to `127.0.0.1` by default. Browse to `http://localhost:<port>`, or set a [default binding address](/settings) for the CLI session. Check the port isn't already used by another container (the Containers page shows each container's ports in Inspect).

## Shell shows nothing or a full-screen program is garbled

The shell has no TTY. Use line-oriented commands; full-screen programs such as `vim` and `top` aren't supported.

## Image pull fails with authorization errors

[Sign in to the registry](/settings#registries) first, using an access token that can pull.

## A compose stack shows warnings

Warnings list keys WSLc can't honour; the stack still starts. See the [Compose guide](/compose#unsupported-keys-warnings).

## Still stuck?

Search or open an [issue](https://github.com/seiggy/wslc-desktop/issues). Include your WSL version (shown in the title bar) and any error text.
