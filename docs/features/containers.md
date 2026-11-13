# Containers

The **Containers** page lists every container, running or not, from **every visible WSLc session**: the `wslc` CLI's default session (`wslc-cli-<user>`), which this app shares with the CLI, and any other sessions created by `wslc` or other tools (see `wslc system session list`).

![Containers list](/screenshots/containers.png)

Each row shows:

- **Name** and the image it was created from.
- **Session**: the owning session. This app's session carries a **This app** badge. Use the filter at the top right to show all sessions or just one. Sessions that can't be read are skipped and reported in a warning bar; their last known containers stay on screen.
- **State**: a green dot for running, grey for exited.
- **Restart**: the container's [restart policy](/features/restart-policy) and live status, such as `Restarting in 4s` or `Crash loop`. Empty when there is no policy.
- **CPU** and **Memory**: live usage for running containers, a dash otherwise.
- Row actions: **Start**, **Stop** and **Delete**. Buttons that don't apply to the current state are disabled.

Select a row to open the [container details](/features/container-details). Use **New container** (top right) to [create one](/features/new-container).

**Start**, **Stop**, **Delete**, logs, shell, inspect and network attach/detach always act on the session that owns the container; the Delete confirmation names that session. Some features exist only for this app's session: **New container** creates containers in the app's session (`wslc-cli-<user>`), and [restart policies](/features/restart-policy) and [stacks](/features/stacks) don't apply to containers in other sessions (their Restart column is empty). [Telemetry](/features/container-details#telemetry) is collected for every session, but only while it's on screen: the CPU and memory columns update every 5 seconds while this list is open.

Each running container's row shows small charts of the last five minutes: **Network** (receive in the accent color, transmit in amber), **Disk I/O** (read and write the same way), and **CPU** and **Memory** next to their current values. Hover the network or disk chart for the current rates and totals. When the window is too narrow, the network and disk charts are hidden first, then the CPU and memory charts, then the CPU and memory values.

::: tip
The list updates on its own: the app follows each session's `wslc events` stream, so containers started, stopped or removed from a terminal (in any session) show up within a moment, without a manual refresh. Switching pages never waits for the list; it shows what the app already holds and updates in place.
:::
