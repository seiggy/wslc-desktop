# FAQ

## How is this different from Docker Desktop?

WSLc Desktop Manager manages WSLc, the container runtime built into WSL, rather than a VM and daemon. It uses familiar terms (containers, images, volumes, networks, stacks) but doesn't imitate features WSLc lacks. Where it can fill a gap safely it does so in the app, for example [restart policies](/features/restart-policy), which only run while the app is running.

## Does it replace the `wslc` CLI?

No. It's a graphical front end. It joins the `wslc` CLI's default session (`wslc-cli-<user>`), so the CLI and the app share the same containers.

## Where is my data stored?

In the WSLc storage of the `wslc` CLI's default session, which the app shares with the CLI. Its location is the **Storage path** setting under [Settings → WSLc configuration](/settings#wslc-configuration). App preferences are in `%LOCALAPPDATA%\WslcDesktop\settings.json`; wslc's own settings are in `%LOCALAPPDATA%\wslc\settings.yaml`.

## Does the app store my registry credentials?

No. They're passed to wslc and kept in its credential store. The app only remembers server names you signed in to.

## Can I use my Docker Compose files?

Mostly. See the [Compose guide](/compose) for supported keys and warnings.

## Why do I only see some of my containers?

The app shows every session WSLc lists as running: the default session it shares with the CLI, and any others. A session that isn't running doesn't appear. Containers from version 1.0's separate `WslcDesktop` session show up only while that session is running. See [Environment](/features/environment).

## Can I run two copies of the app?

No, run one at a time. See [Troubleshooting](/troubleshooting).
