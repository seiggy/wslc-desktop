# FAQ

## How is this different from Docker Desktop?

WSLc Desktop manages WSLc, the container runtime built into WSL, rather than a VM and daemon. It uses familiar terms (containers, images, volumes, networks, stacks) but doesn't imitate features WSLc lacks, such as restart policies.

## Does it replace the `wslc` CLI?

No. It's a graphical front end. It runs its own `WslcDesktop` session, and you can use the CLI against it with `--session WslcDesktop`.

## Where is my data stored?

In the WSLc storage for the `WslcDesktop` session. App preferences are in `%LOCALAPPDATA%\WslcDesktop\settings.json`; wslc's own settings are in `%LOCALAPPDATA%\wslc\settings.yaml`.

## Does the app store my registry credentials?

No. They're passed to wslc and kept in its credential store. The app only remembers server names you signed in to.

## Can I use my Docker Compose files?

Mostly. See the [Compose guide](/compose) for supported keys and warnings.

## Why do I only see some of my containers?

The app shows its own session. See [Environment](/features/environment).

## Can I run two copies of the app?

No, run one at a time. See [Troubleshooting](/troubleshooting).
