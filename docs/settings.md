# Settings and registries

Open **Settings** at the bottom of the navigation.

## Appearance

Choose **Use Windows setting**, **Light**, or **Dark** under **Theme**. The Windows setting follows your app color mode.
By default the app also follows your Windows accent color. Turn on **Use custom accent color**, choose **Choose accent color**,
pick a color or enter its hex value, then choose **Apply color**. Turn the switch off to follow Windows again.

Changes apply immediately across the app and persist for the next launch. Custom accent shades are adjusted for readable
controls in both themes; Windows high-contrast colors remain in control when a contrast theme is enabled.
Appearance preferences are saved in `%LOCALAPPDATA%\WslcDesktop\settings.json`, not WSLc's `settings.yaml`.

## App preferences

**Minimize to tray** hides the minimized window from the taskbar. **Close button minimizes to tray** makes the close button
hide the app instead of quitting. Click the tray icon to restore the window; choose **Exit** from its menu to quit.
Both preferences apply immediately. The tray icon follows the taskbar's theme independently of the app's theme.

## WSLc configuration

![WSLc configuration](/screenshots/settings.png)

These values are saved to wslc's `settings.yaml` (`%LOCALAPPDATA%\wslc\settings.yaml`). That file configures the **default session** (the one the `wslc` command line uses) and any new session it creates. WSLc Desktop Manager runs its own session and doesn't read this file. The editor keeps your comments and unknown keys when saving.

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
