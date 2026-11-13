# Restart policies

WSLc has no restart policy of its own that covers every case (the `wslc` CLI accepts only `no` and `always` for `--restart`), so WSLc Desktop Manager adds an **app-level restart policy**.

::: warning It only runs while the app is running
The policy is enforced by WSLc Desktop Manager, not by WSLc. If the app is closed, a crashed container stays down until you open the app again. Turn on [Start with Windows](/settings#app-preferences) and **Minimize to tray** to keep the policy in effect all the time.
:::

## Policies

| Policy | Restarts the container when… |
| --- | --- |
| **Never** (default) | Never. |
| **Always** | It stops for any reason, including a clean exit. |
| **On failure** | It exits with a non-zero code (an unknown exit code counts as a failure). Set **Max restarts** to cap the attempts. |
| **Unless stopped** | It stops for any reason, unless you stopped it from the app. A stop you made sticks across app restarts. |

Choose a policy in the [New container](/features/new-container) dialog or on the [container details](/features/container-details) page. Policies are saved per container name in `%LOCALAPPDATA%\WslcDesktop\restart-policies.json`. [Compose](/compose) `restart:` values are applied to the services of a stack when you bring it up.

## What happens

- **When the app starts**, containers whose policy says they should be running but aren't are started right away. A stop you made in an earlier app session only sticks for **Unless stopped**.
- **While the app runs**, a container that exits is restarted with a growing delay: 1, 2, 4, 8… seconds, up to 60 seconds.
- **Stopping or deleting a container from the app never triggers a restart.** Starting it again from the app clears any crash-loop state.
- A container that runs for 60 seconds without exiting is considered healthy and its attempt count resets.
- After 5 failed attempts in a row (or the **Max restarts** you set for **On failure**) the app stops trying and marks the container **Crash loop**. Fix the cause and start it yourself.
- Setting a policy on a container that is currently stopped doesn't start it.

The **Restart** column on the Containers page shows the policy and live status: `Restarting in 4s`, `Crash loop`, or the policy with a restart count.

## How crashes are detected

The app reads the session's container list and the exit code from each container's status. It keeps a `wslc events` stream open for the session, so a container that dies is re-read within a fraction of a second, whether the app owns the session or is attached to one left running by an earlier copy of the app. When the app owns the session, the WSLc API's `ProcessCrashed` and `Terminated` events and init-process exits trigger a re-read too. A backed-off restart is checked again exactly when it is due.

- **Safety net**: the list is also re-read every 30 seconds (2 minutes while hidden in the tray), and the whole session is re-read whenever its event stream reconnects.
- **Without an event stream** (for example on a WSL version without `wslc events`), the app falls back to polling: every 10 seconds when it owns the session (30 seconds while hidden), every 2 seconds when attached (10 seconds while hidden). Restarts can then lag by a few seconds.

::: info Known gap
A container stopped outside the app (for example `wslc stop` in a terminal) looks like an exit to the app, so **Always** and **On failure** (when the stop gives a non-zero code) restart it. Stop containers from the app, or set the policy to **Never** first. If the whole session ends, the app can only restart containers once the session is back.
:::

## Native `--restart`

The app doesn't pass `--restart` to `wslc`. It is the single authority, which also makes `on-failure` and `unless-stopped` possible. If you create a container with `wslc run --restart always` yourself, WSLc restarts it as well; this is harmless because starting a running container does nothing.
