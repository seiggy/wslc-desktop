# Containers

The **Containers** page lists every container in the app's `WslcDesktop` session, running or not.

![Containers list](/screenshots/containers.png)

Each row shows:

- **Name** and the image it was created from.
- **State**: a green dot for running, grey for exited.
- **Restart**: the container's [restart policy](/features/restart-policy) and live status, such as `Restarting in 4s` or `Crash loop`. Empty when there is no policy.
- **CPU** and **Memory**: live usage for running containers, a dash otherwise.
- Row actions: **Start**, **Stop** and **Delete**. Buttons that don't apply to the current state are disabled.

Select a row to open the [container details](/features/container-details). Use **New container** (top right) to [create one](/features/new-container).

::: tip
The list refreshes automatically, so containers started from a terminal in the same session show up without a manual refresh.
:::
