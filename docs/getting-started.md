# Getting started

## Prerequisites

- Windows 11 22H2 (build 22621) or later. The app builds against SDK 10.0.26100 but declares minimum Windows version 10.0.22621.0.
- WSL with container support (WSLc). The app shows your WSL version in the title bar once it connects.
- `wslc.exe` on your `PATH`. The WSLc API can't enumerate containers or sessions, so the app lists them through `wslc.exe` and performs everything else through the API.

::: tip
Run `wslc --help` in a terminal. If it prints the command list, the app can find it.
:::

## Install

Download `WslcDesktopManager-win-x64-Setup.exe` (or `-arm64`) from the [Releases page](https://github.com/seiggy/wslc-desktop/releases) and run it. It installs per user and updates from **Settings → Updates**. The installer is unsigned until a certificate is available, so Windows SmartScreen may warn: choose **More info → Run anyway**. Prefer no installer? Extract the portable zip and run `WslcDesktop.exe` (no auto-update). WSLc is still required, and app settings and data remain under `%LOCALAPPDATA%`. No release yet? [Build from source](/contributing).

Update packages (`.nupkg`) and feed indexes on the Releases page are used by the installed app, not manual installers. Microsoft Store submission packages are built in a separate workflow and are not attached to releases. Microsoft signs Store packages after certification; install through the Microsoft Store once the listing is live, or use the installer.

## First run

1. Launch **WSLc Desktop Manager**. It uses the `wslc` CLI's default session (`wslc-cli-<user>`): it reuses the session if it's already running, or has `wslc` start it.
2. If the WSLc components are missing, the app offers an **Install components** button. Use it, then **Retry**.
3. The **Containers** page opens. Anything you create here lives in the `wslc-cli-<user>` default session, shared with the `wslc` CLI.

![Containers page after first run](/screenshots/containers.png)

::: tip Shared with the CLI
The app and the `wslc` command line use the same session, so containers you start with `wslc` appear in the app and vice versa. Plain `wslc` commands (no `--session`) work with the app's containers. Other running sessions appear too, marked by name. See [Environment](/features/environment).
:::

::: info Upgrading from 1.0
Earlier versions ran a separate `WslcDesktop` session. While it's running it still appears in the app as another session, so its containers aren't lost. Moving them to the default session (for example by re-creating them or bringing their Compose stacks up again) is recommended but not required.
:::

## Navigation

The left navigation lists **Containers**, **Stacks**, **Images**, **Volumes**, **Networks** and **Environment**, with **Settings** at the bottom. Each has a [walkthrough](/features/containers).
