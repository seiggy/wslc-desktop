using WslcDesktop.Compose.Planning.Services;

using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Planning;

namespace WslcDesktop.Compose.Tests.Planning;

public class PlannerTests
{
    private const string Sample = """
        name: demo
        services:
          web:
            image: nginx:alpine
            ports: ["8080:80"]
            environment:
              REDIS_HOST: redis
              GREETING: ${GREETING:-hello}
            volumes:
              - webdata:/usr/share/nginx/html
              - ./site:/site:ro
            networks: [front]
            depends_on:
              cache:
                condition: service_healthy
          cache:
            image: redis:7
            command: ["redis-server", "--appendonly", "yes"]
            healthcheck:
              test: ["CMD", "redis-cli", "ping"]
              interval: 5s
              retries: 3
            mem_limit: 128m
            cpus: 0.5
            networks:
              front:
                aliases: [kv]
        networks:
          front: {}
        volumes:
          webdata: {}
        """;

    private static ComposeOptions Options(Dictionary<string, string>? env = null) => new()
    {
        Directory = @"C:\work\demo",
        Env = env ?? [],
        FileExists = _ => true,
    };

    private static ComposeAction Run(ComposePlan plan, string service) =>
        plan.Actions.Single(a => a.Kind == ActionKind.RunService && a.Service == service);

    [Fact]
    public void Plans_resources_before_services_and_orders_by_depends_on()
    {
        var plan = ComposePlanner.Plan(Sample, Options());

        Assert.Empty(plan.Errors);
        Assert.Equal("demo", plan.Project);
        Assert.Equal(
            [ActionKind.CreateNetwork, ActionKind.CreateVolume, ActionKind.RunService, ActionKind.RunService],
            plan.Actions.Select(a => a.Kind));
        Assert.Equal(["cache", "web"], plan.Actions.Where(a => a.Kind == ActionKind.RunService).Select(a => a.Service));
        Assert.Equal("demo_front", plan.Actions[0].Target);
        Assert.Equal("demo_webdata", plan.Actions[1].Target);
    }

    [Fact]
    public void Translates_service_to_run_flags()
    {
        var args = Run(ComposePlanner.Plan(Sample, Options(new() { ["GREETING"] = "hi" })), "web").Args;

        Assert.Equal(["run", "-d", "--name", "demo-web"], args.Take(4));
        Assert.Contains("--label", args);
        Assert.Contains($"{ComposeLabels.Project}=demo", args);
        Assert.Contains($"{ComposeLabels.Service}=web", args);
        AssertPair(args, "--publish", "8080:80");
        AssertPair(args, "--env", "REDIS_HOST=redis");
        AssertPair(args, "--env", "GREETING=hi");
        AssertPair(args, "--volume", "demo_webdata:/usr/share/nginx/html");
        AssertPair(args, "--volume", Path.GetFullPath(@"C:\work\demo\site") + ":/site:ro");
        AssertPair(args, "--network", "demo_front");
        AssertPair(args, "--network-alias", "web");
        Assert.Equal("nginx:alpine", args[^1]);
    }

    [Fact]
    public void Translates_healthcheck_limits_and_command()
    {
        var args = Run(ComposePlanner.Plan(Sample, Options()), "cache").Args;

        AssertPair(args, "--health-cmd", "redis-cli ping");
        AssertPair(args, "--health-interval", "5s");
        AssertPair(args, "--health-retries", "3");
        AssertPair(args, "--memory", "128M");
        AssertPair(args, "--cpus", "0.5");
        AssertPair(args, "--network-alias", "kv");
        Assert.Equal(["redis:7", "redis-server", "--appendonly", "yes"], args.TakeLast(4));
    }

    [Fact]
    public void Healthy_dependency_becomes_a_wait()
    {
        var wait = Assert.Single(Run(ComposePlanner.Plan(Sample, Options()), "web").Waits!);

        Assert.Equal(new ServiceWait("cache", "service_healthy"), wait);
    }

    [Fact]
    public void Service_without_networks_joins_the_project_default_network()
    {
        var plan = ComposePlanner.Plan("services:\n  a:\n    image: alpine\n", new ComposeOptions { Directory = @"C:\x\proj", FileExists = _ => true });

        Assert.Equal("proj", plan.Project);
        Assert.Equal("proj_default", plan.Actions[0].Target);
        AssertPair(Run(plan, "a").Args, "--network", "proj_default");
    }

    [Fact]
    public void Additional_networks_are_connected_after_run_with_aliases()
    {
        const string yaml = """
            services:
              a:
                image: alpine
                networks: [one, two]
            networks:
              one: {}
              two: {}
            """;
        var action = Run(ComposePlanner.Plan(yaml, Options()), "a");

        AssertPair(action.Args, "--network", "demo_one");
        var connect = Assert.Single(action.After!);
        Assert.Equal(["network", "connect", "--network-alias", "a", "demo_two", "demo-a"], connect);
    }

    [Theory]
    [InlineData("deploy: { replicas: 3 }", "deploy")]
    [InlineData("profiles: [debug]", "profiles")]
    [InlineData("configs: [c]", "configs")]
    [InlineData("configs: [c]", "configs")]
    [InlineData("extends: { service: other }", "extends")]
    [InlineData("privileged: true", "privileged")]
    public void Unsupported_fields_warn(string field, string key)
    {
        var plan = ComposePlanner.Plan($"services:\n  a:\n    image: alpine\n    {field}\n", Options());

        Assert.Empty(plan.Errors);
        Assert.Contains(plan.Warnings, w => w.Contains($"'{key}'") && w.Contains("Service 'a'"));
    }

    [Fact]
    public void Restart_no_does_not_warn()
    {
        var plan = ComposePlanner.Plan("services:\n  a:\n    image: alpine\n    restart: \"no\"\n", Options());

        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Dependency_cycle_is_an_error()
    {
        const string yaml = """
            services:
              a: { image: alpine, depends_on: [b] }
              b: { image: alpine, depends_on: [a] }
            """;

        Assert.Contains(ComposePlanner.Plan(yaml, Options()).Errors, e => e.Contains("cycle"));
    }

    [Fact]
    public void Unknown_dependency_and_missing_image_are_errors()
    {
        const string yaml = """
            services:
              a: { image: alpine, depends_on: [ghost] }
              b: {}
            """;
        var errors = ComposePlanner.Plan(yaml, Options()).Errors;

        Assert.Contains(errors, e => e.Contains("ghost"));
        Assert.Contains(errors, e => e.Contains("'b'") && e.Contains("image"));
    }

    [Fact]
    public void Healthy_condition_without_healthcheck_warns_and_degrades()
    {
        const string yaml = """
            services:
              a: { image: alpine, depends_on: { b: { condition: service_healthy } } }
              b: { image: alpine }
            """;
        var plan = ComposePlanner.Plan(yaml, Options());

        Assert.Contains(plan.Warnings, w => w.Contains("no healthcheck"));
        Assert.Equal("service_started", Run(plan, "a").Waits![0].Condition);
    }

    [Fact]
    public void Build_maps_to_a_build_action_tagged_with_the_image()
    {
        const string yaml = """
            services:
              app:
                build:
                  context: ./app
                  dockerfile: Dockerfile.dev
                  args: { MODE: prod }
                  ssh: [default]
            """;
        var plan = ComposePlanner.Plan(yaml, Options());
        var build = plan.Actions.Single(a => a.Kind == ActionKind.Build);

        Assert.Equal("demo-app", build.Target);
        AssertPair(build.Args, "--tag", "demo-app");
        AssertPair(build.Args, "--build-arg", "MODE=prod");
        AssertPair(build.Args, "--file", Path.GetFullPath(@"C:\work\demo\app\Dockerfile.dev"));
        Assert.Contains(plan.Warnings, w => w.Contains("build.ssh"));
        Assert.Equal("demo-app", Run(plan, "app").Args[^1]);
    }

    [Fact]
    public void Missing_required_env_file_is_an_error_and_optional_is_skipped()
    {
        const string yaml = """
            services:
              a:
                image: alpine
                env_file: [.env.a, { path: .env.b, required: false }]
            """;
        var plan = ComposePlanner.Plan(yaml, new ComposeOptions { Directory = @"C:\work\demo", FileExists = _ => false });

        Assert.Single(plan.Errors);
        Assert.Contains(".env.a", plan.Errors[0]);
    }

    [Fact]
    public void Entrypoint_list_splits_into_flag_and_arguments()
    {
        const string yaml = """
            services:
              a:
                image: alpine
                entrypoint: ["sh", "-c"]
                command: echo hi
            """;
        var args = Run(ComposePlanner.Plan(yaml, Options()), "a").Args;

        AssertPair(args, "--entrypoint", "sh");
        Assert.Equal(["alpine", "-c", "echo", "hi"], args.TakeLast(4));
    }

    [Fact]
    public void Network_mode_host_uses_host_network()
    {
        var args = Run(ComposePlanner.Plan("services:\n  a:\n    image: alpine\n    network_mode: host\n", Options()), "a").Args;

        AssertPair(args, "--network", "host");
        Assert.DoesNotContain("--network-alias", args);
    }

    [Fact]
    public void External_network_and_volume_are_not_created()
    {
        const string yaml = """
            services:
              a: { image: alpine, networks: [shared], volumes: ["data:/d"] }
            networks:
              shared: { external: true, name: corp }
            volumes:
              data: { external: true }
            """;
        var plan = ComposePlanner.Plan(yaml, Options());

        Assert.DoesNotContain(plan.Actions, a => a.Kind is ActionKind.CreateNetwork or ActionKind.CreateVolume);
        AssertPair(Run(plan, "a").Args, "--network", "corp");
        AssertPair(Run(plan, "a").Args, "--volume", "data:/d");
    }

    [Fact]
    public void Undefined_volume_is_an_error()
    {
        var plan = ComposePlanner.Plan("services:\n  a:\n    image: alpine\n    volumes: [\"nope:/d\"]\n", Options());

        Assert.Contains(plan.Errors, e => e.Contains("nope"));
    }

    [Fact]
    public void Invalid_yaml_is_reported_not_thrown()
    {
        var plan = ComposePlanner.Plan("services: [unterminated", Options());

        Assert.NotEmpty(plan.Errors);
    }

    [Fact]
    public void Anchors_and_merge_keys_are_resolved()
    {
        const string yaml = """
            x-common: &common
              image: alpine
              environment: { A: "1" }
            services:
              a:
                <<: *common
              b:
                <<: *common
                environment: { A: "2" }
            """;
        var plan = ComposePlanner.Plan(yaml, Options());

        AssertPair(Run(plan, "a").Args, "--env", "A=1");
        AssertPair(Run(plan, "b").Args, "--env", "A=2");
    }

    [Fact]
    public void Project_name_override_and_normalization()
    {
        var plan = ComposePlanner.Plan("services:\n  a:\n    image: alpine\n", new ComposeOptions { ProjectName = "My App!", Directory = "C:\\x", FileExists = _ => true });

        Assert.Equal("my-app-", plan.Project);
    }

    private static void AssertPair(IReadOnlyList<string> args, string flag, string value)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == flag && args[i + 1] == value)
            {
                return;
            }
        }

        Assert.Fail($"Expected '{flag} {value}' in: {string.Join(' ', args)}");
    }
}
