
using WslcDesktop.Compose.Planning;
using WslcDesktop.Compose.Secrets;

namespace WslcDesktop.Compose.Tests.Secrets;

public class SecretNeedsTests
{
    private const string Yaml = """
        name: demo
        services:
          web:
            image: alpine
            environment:
              API: ${secret:api}
              DB: ${secret:db}
          worker:
            image: alpine
            environment:
              DB: pw=${secret:db}
          cache:
            image: alpine
            secrets: [redis_pw]
        secrets:
          redis_pw: {}
        """;

    [Fact]
    public void Needs_ListEverySecretWithItsServicesAndStoredState()
    {
        var plan = ComposePlanner.Plan(Yaml, new ComposeOptions { SecretExists = n => n == "db" });

        Assert.Equal(["api", "db", "redis_pw"], plan.SecretNeeds.Select(n => n.Name).ToArray());
        var api = plan.SecretNeeds.Single(n => n.Name == "api");
        Assert.Equal(["web"], api.Services);
        Assert.False(api.Exists);
        var db = plan.SecretNeeds.Single(n => n.Name == "db");
        Assert.Equal(["web", "worker"], db.Services);
        Assert.True(db.Exists);
        Assert.Equal(["cache"], plan.SecretNeeds.Single(n => n.Name == "redis_pw").Services);
        Assert.Equal(["api", "redis_pw"], plan.MissingSecrets);
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void Needs_AllStored_CanRun()
    {
        var plan = ComposePlanner.Plan(Yaml, new ComposeOptions { SecretExists = _ => true });
        Assert.All(plan.SecretNeeds, n => Assert.True(n.Exists));
        Assert.True(plan.CanRun);
    }

    [Fact]
    public void Needs_FileAndEnvironmentSources_AreOfferedAsImports()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-x");
        var plan = ComposePlanner.Plan("""
            name: demo
            services:
              app:
                image: alpine
                secrets: [from_file, from_env]
            secrets:
              from_file:
                file: ./pw.txt
              from_env:
                environment: MY_TOKEN
            """, new ComposeOptions { Directory = dir, SecretExists = _ => false });

        Assert.Empty(plan.Errors);
        var file = plan.SecretNeeds.Single(n => n.Name == "from_file");
        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "pw.txt")), file.Import!.FilePath);
        Assert.Equal("MY_TOKEN", plan.SecretNeeds.Single(n => n.Name == "from_env").Import!.EnvVar);
        Assert.Contains(plan.Actions, a => a.Args.Contains("FROM_FILE=${secret:from_file}"));
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void Needs_InvalidStoredName_IsAnError()
    {
        var plan = ComposePlanner.Plan("""
            name: demo
            services:
              app:
                image: alpine
                secrets: [x]
            secrets:
              x:
                name: "bad name"
            """, new ComposeOptions { SecretExists = _ => true });
        Assert.NotEmpty(plan.Errors);
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void Generator_ProducesDistinctValuesOfRequestedLength()
    {
        var a = SecretGenerator.Create(24);
        Assert.Equal(24, a.Length);
        Assert.NotEqual(a, SecretGenerator.Create(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecretGenerator.Create(4));
    }
}
