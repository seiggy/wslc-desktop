# Volumes

**Volumes** lists named volumes in the session. Use **Prune unused** to remove volumes no container uses, or the row's remove button for a single volume.

![Volumes list](/screenshots/volumes.png)

## Creating a volume

**New volume** asks for a name and a driver:

- **guest** (default) stores the volume inside the WSLc VM; no size is needed.
- **vhd** stores the volume in a virtual disk and needs a size. A **Size** field with a unit (MB, GB or TB, default 10 GB) appears only for this driver. The size must be between 3 MB and 64 TB; anything else shows a message in the dialog instead of an error from WSLc.

![New VHD volume](/screenshots/volume-new-vhd.png)

Select a volume for its overview (driver, scope, creation time, mount point), the containers that use it and its labels. A **Raw JSON** expander shows the complete record.

![Volume details](/screenshots/volume-detail.png)

Volumes created by a [compose stack](/features/stacks) are labelled with the project name and named `<project>_<volume>`.
