# Images

The **Images** page lists local images with their ID, tag status, how many containers use them, size and age.

![Images list](/screenshots/images.png)

- **Pull** an image by typing a reference such as `docker.io/library/alpine:latest` in the box at the top. Private registries need a [sign-in](/settings#registries).
- **Run** (the play button on a row) opens the [New container](/features/new-container) dialog with that image already filled in. Untagged images use their image ID. Set the name, ports, mounts and environment (including `${secret:NAME}` tokens), then **Create**. Afterwards you can jump straight to the new container's details.
- **Delete** removes an image.

![New container dialog opened from an image](/screenshots/image-run-dialog.png)

## Image details

Select an image to see its overview (tags, ID, digest, size, OS/architecture), its layers and its configuration. **Run container** at the top opens the same pre-filled New container dialog.

![Image details](/screenshots/image-detail.png)

::: info
WSLc reports each layer's digest but not its size or the command that created it, so only digests are shown.
:::
