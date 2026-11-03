---
layout: home
hero:
  name: WSLc Desktop Manager
  text: A native Windows app for WSLc
  tagline: Manage containers, images, volumes, networks and compose stacks without living in the command line. Built for people who know Docker Desktop.
  image:
    src: /logo.svg
    alt: WSLc Desktop Manager logo
  actions:
    - theme: brand
      text: Get started
      link: /getting-started
    - theme: alt
      text: Feature walkthroughs
      link: /features/containers
features:
  - title: Containers
    details: Start, stop and delete containers, then dig into live logs, an interactive shell, telemetry charts and a full inspect view.
    link: /features/containers
  - title: Stacks (Compose)
    details: Open a compose file, review the plan and any warnings for keys WSLc can't honour, then bring the stack up or down.
    link: /features/stacks
  - title: Images, volumes, networks
    details: Pull and inspect images, browse layers, and manage volumes and networks with usage and prune.
    link: /features/images
  - title: Settings and registries
    details: Edit wslc's settings.yaml, tune the app's own session and sign in to private registries.
    link: /settings
---

<div class="vp-doc" style="max-width: 960px; margin: 3rem auto; padding: 0 24px;">

## See it in action

![The Containers page in WSLc Desktop Manager](/screenshots/containers.png)

WSLc Desktop Manager is a native WinUI 3 wrapper over the [WSLc](https://wsl.dev) container runtime. It follows the vocabulary Docker Desktop users already know, but stays honest about what WSLc can and can't do: anything WSLc doesn't support is called out rather than imitated.

</div>
