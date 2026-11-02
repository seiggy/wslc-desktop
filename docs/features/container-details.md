# Container details

Select a container to open its detail page. Four tabs cover everyday troubleshooting. Use the back arrow to return to the list.

## Logs

![Container logs](/screenshots/container-logs.png)

- **Follow** streams new output as it arrives.
- **Timestamps** prefixes each line with its time.
- **Last lines** sets how much history to load (default 200).
- **Clear** empties the view; it doesn't touch the container.

## Shell

![Interactive shell](/screenshots/container-shell.png)

Pick a shell (such as `sh`), choose **Start shell**, type a command in the box at the bottom and press Enter. The shell runs through `wslc exec` without a TTY, so line-oriented commands work but full-screen programs like `vim` or `top` don't. Choose **End shell** when you're done.

If the container is stopped, the tab offers **Start container** instead.

## Telemetry

![Telemetry charts](/screenshots/container-telemetry.png)

Live charts for **CPU**, **Memory**, **Network** (receive and transmit per second) and **Disk I/O** (read and write per second), plus process count and running totals.

## Inspect

![Inspect view](/screenshots/container-inspect.png)

A structured view of the container: overview (ID, image, status, timestamps, limits), ports and networking, and more. Use **Reload** to refresh it. From the networking section you can **Connect to network…** or **Disconnect** a network. A **Raw JSON** expander shows the unprocessed inspect output.
