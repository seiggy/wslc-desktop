# Environment

**Environment** shows the WSL, kernel and session manager versions and every WSLc session on the machine.

![Environment page](/screenshots/environment.png)

For each session you see its ID, name, who created it, how many containers are running out of the total and how many images it holds. The session this app uses is marked **This app**; its terminate button is disabled.

Terminate another session from the stop button on its row. Sessions are isolated: each has its own containers, images and storage. The app uses the `wslc` CLI's default session (named like `wslc-cli-<user>`), so plain `wslc` commands work with the app's containers:

```powershell
wslc list --all
wslc image list
```

If you used version 1.0, its separate `WslcDesktop` session is listed here while it's running. Its containers stay usable from the app; moving them to the default session is recommended but not required.
