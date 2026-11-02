# Volumes

**Volumes** lists named volumes in the session. Use **Prune unused** to remove volumes no container uses, or the row's remove button for a single volume.

![Volumes list](/screenshots/volumes.png)

Select a volume for its overview (driver, scope, creation time, mount point), the containers that use it and its labels. A **Raw JSON** expander shows the complete record.

![Volume details](/screenshots/volume-detail.png)

Volumes created by a [compose stack](/features/stacks) are labelled with the project name and named `<project>_<volume>`.
