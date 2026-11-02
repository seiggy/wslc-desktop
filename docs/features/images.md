# Images

The **Images** page lists local images with their ID, tag status, how many containers use them, size and age.

![Images list](/screenshots/images.png)

- **Pull** an image by typing a reference such as `docker.io/library/alpine:latest` in the box at the top. Private registries need a [sign-in](/settings#registries).
- **Delete** removes an image.

## Image details

Select an image to see its overview (tags, ID, digest, size, OS/architecture), its layers and its configuration.

![Image details](/screenshots/image-detail.png)

::: info
WSLc reports each layer's digest but not its size or the command that created it, so only digests are shown.
:::
