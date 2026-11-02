# Settings and registries

Open **Settings** at the bottom of the navigation. It has three sections.

## WSLc configuration

![WSLc configuration](/screenshots/settings.png)

These values are saved to wslc's `settings.yaml` (`%LOCALAPPDATA%\wslc\settings.yaml`). That file configures the **default session** (the one the `wslc` command line uses) and any new session it creates. WSLc Desktop runs its own session and doesn't read this file. The editor keeps your comments and unknown keys when saving.

| Setting | Purpose |
| --- | --- |
| CPU count | Virtual CPUs for the default session. |
| Memory | Memory limit, e.g. `2GB`. |
| Max storage | Maximum disk image size, e.g. `500GB`. |
| Storage path | Base folder for the default session's storage. Changing it creates a new, empty session; existing data isn't moved. |
| Default binding address | Host address published ports bind to when none is given (default `127.0.0.1`). |
| Host loopback name | DNS name that resolves to the host (default `host.wslc.internal`, or `none`). |
| Idle timeout | Seconds an idle session VM keeps running (default 30). |
| Credential store | `wincred` or `file`. |

Type `default` to return a setting to its built-in value. Invalid values are explained next to the field.

## App session

![App session and registries](/screenshots/settings-registries.png)

Controls the app's own `WslcDesktop` session: **CPU count**, **Memory (MB)** and **GPU access**. Choose **Save**, then restart the app for the change to apply. **Reset to defaults** restores 4 CPUs, 4096 MB and GPU off.

## Registries {#registries}

Sign in to private registries so images can be pulled. Enter the **Server** (blank for the default registry), **Username** and **Password or access token**, then **Sign in**. Credentials are handed to wslc and kept in its credential store; the app never stores or shows them. WSLc can't list saved credentials, so the list shows only registries you signed in to from this app (server names only).

::: warning
Use an access token with the narrowest scope that can pull, rather than a account password.
:::
