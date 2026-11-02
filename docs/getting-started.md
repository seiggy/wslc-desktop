# Getting started

## Prerequisites

- Windows 11 (the app targets Windows 10.0.26100).
- WSL with container support (WSLc). The app shows your WSL version in the title bar once it connects.
- `wslc.exe` on your `PATH`. The WSLc API can't enumerate containers or sessions, so the app lists them through `wslc.exe` and performs everything else through the API.

::: tip
Run `wslc --help` in a terminal. If it prints the command list, the app can find it.
:::

## Install

Download the latest build from the [Releases page](https://github.com/seiggy/wslc-desktop/releases). The release offers a portable EXE and MSIX packages; pick whichever suits you. No release yet? [Build from source](/contributing).

## First run

1. Launch **WSLc Desktop**. It starts its own WSLc session named `WslcDesktop`, separate from the default session used by the `wslc` command line.
2. If the WSLc components are missing, the app offers an **Install components** button. Use it, then **Retry**.
3. The **Containers** page opens. Anything you create here lives in the `WslcDesktop` session.

![Containers page after first run](/screenshots/containers.png)

::: warning Separate sessions
Containers started with the `wslc` CLI's default session don't appear in the app, and vice versa. Use `wslc --session WslcDesktop ...` to work with the app's containers from a terminal. See [Environment](/features/environment).
:::

## Navigation

The left navigation lists **Containers**, **Stacks**, **Images**, **Volumes**, **Networks** and **Environment**, with **Settings** at the bottom. Each has a [walkthrough](/features/containers).
