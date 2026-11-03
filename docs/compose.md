# Compose guide

WSLc Desktop Manager reads a standard compose file and translates it into `wslc` commands. It supports the common subset and tells you, before anything runs, about every key it can't honour. See [Stacks](/features/stacks) for the UI.

## Sample file

<<< @/public/examples/demo-stack/compose.yaml{yaml}

[Download compose.yaml](/examples/demo-stack/compose.yaml). With project name `demo-stack`, this creates the network `demo-stack_front`, the volume `demo-stack_site-data`, then runs `demo-stack-cache` and `demo-stack-web` (after `cache`). The `restart` key produces a warning in the preview.

## Naming

- The project name comes from `name:`, or the folder name, and can be edited in the preview.
- Containers are named `<project>-<service>` unless `container_name` is set.
- Networks and volumes are named `<project>_<key>` unless they set `name:` or are `external: true`.
- If no network is declared, services join `<project>_default`.

## Supported service keys

`image`, `build` (`context`, `dockerfile`, `args`, `target`), `command`, `entrypoint`, `environment`, `env_file`, `working_dir`, `user`, `ports`, `expose`, `volumes`, `networks`, `network_mode`, `healthcheck`, `mem_limit`, `cpus`, `depends_on`, `container_name`, `hostname`, `domainname`, `labels`, `stop_signal`, `stop_grace_period`, `tty`, `stdin_open`, `shm_size`, `tmpfs`, `dns`, `dns_search`, `pull_policy`.

`${VAR}` interpolation and `x-` extension keys are accepted. Services start in `depends_on` order, waiting for health where a healthcheck is defined.

Top-level networks support `driver`, `internal`, `driver_opts`, `labels`, `ipam` (subnet, gateway, ip_range) and `external`. Top-level volumes support `driver`, `driver_opts`, `labels` and `external`.

## Unsupported keys (warnings)

| Key | What happens |
| --- | --- |
| `restart` | WSLc has no restart policy; the container won't restart automatically. (`restart: "no"` is fine.) |
| `deploy` | Replicas, resources and placement are ignored; one container starts. |
| `scale` | One container is started. |
| `profiles` | Ignored; the service always starts. |
| `secrets`, `configs` | Not mounted. Top-level `secrets`/`configs` are ignored. |
| `extends`, top-level `include` | Not resolved. |
| `links` | Use networks and service names instead. |
| `platform` | Platform selection isn't supported. |

Any other unknown key is ignored with a warning. Problems such as a service with no `image` or `build`, a missing required `env_file`, or a dependency cycle are reported as errors and block **Up**.
