
using WslcDesktop.Compose.Planning;
using WslcDesktop.Compose.Secrets;
using WslcDesktop.Compose.Secrets.Injection;

namespace WslcDesktop.Compose.Tests.Secrets;

public class SecretTests
{
    private static string? Lookup(string n) => n switch { "db" => "s3cr3t-pw", "api" => "tok\"en\\x", _ => null };

    [Fact]
    public void Resolve_ReplacesTokens_AndThrowsWhenMissing()
    {
        Assert.Equal("u:s3cr3t-pw@h", SecretTokens.Resolve("u:${secret:db}@h", Lookup));
        var ex = Assert.Throws<SecretUnavailableException>(() => SecretTokens.Resolve("${secret:nope}", Lookup));
        Assert.Equal("nope", ex.SecretName);
    }

    [Theory]
    [InlineData("db", true)]
    [InlineData("db-1.x_y", true)]
    [InlineData("", false)]
    [InlineData("-bad", false)]
    [InlineData("a b", false)]
    public void IsValidName(string name, bool ok) => Assert.Equal(ok, SecretTokens.IsValidName(name));

    [Fact]
    public void Find_ListsReferencedNames() =>
        Assert.Equal(["db", "api"], SecretTokens.Find("a=${secret:db} b=${secret:api}").ToArray());

    [Fact]
    public void Redactor_ReplacesPlainAndJsonEscapedValues()
    {
        var r = new SecretRedactor(new Dictionary<string, string> { ["db"] = "s3cr3t-pw", ["api"] = "tok\"en\\x" });
        Assert.Equal("PW=${secret:db}", r.Redact("PW=s3cr3t-pw"));
        Assert.Equal("{\"K\":\"${secret:api}\"}", r.Redact("{\"K\":\"tok\\\"en\\\\x\"}"));
    }

    [Fact]
    public void Redactor_SkipsShortValues_AndPrefersLongest()
    {
        var r = new SecretRedactor(new Dictionary<string, string> { ["a"] = "abc", ["b"] = "hunter2", ["c"] = "hunter2hunter2" });
        Assert.Equal("abc", r.Redact("abc"));
        Assert.Equal("${secret:c}", r.Redact("hunter2hunter2"));
        Assert.Equal("${secret:b}", r.Redact("hunter2"));
        Assert.True(SecretRedactor.Empty.IsEmpty);
    }

    [Fact]
    public void Prepare_InheritsEnvInsteadOfPuttingValueOnCommandLine()
    {
        using var cmd = SecretInjection.Prepare(["run", "-d", "--env", "DB_PW=${secret:db}", "alpine"], Lookup);
        Assert.DoesNotContain(cmd.Args, a => a.Contains("s3cr3t-pw"));
        Assert.Contains("DB_PW", cmd.Args);
        Assert.Equal("s3cr3t-pw", cmd.Environment["DB_PW"]);
    }

    [Fact]
    public void Prepare_UsesEnvFileForReservedNames_AndCleansUp()
    {
        string? file;
        using (var cmd = SecretInjection.Prepare(["run", "--env", "PATH=${secret:db}", "alpine"], Lookup))
        {
            Assert.DoesNotContain(cmd.Args, a => a.Contains("s3cr3t-pw"));
            Assert.Contains("--env-file", cmd.Args);
            file = cmd.Args[cmd.Args.ToList().IndexOf("--env-file") + 1];
            Assert.Contains("PATH=s3cr3t-pw", File.ReadAllText(file));
        }

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Prepare_RejectsTokensOutsideEnvValues() =>
        Assert.ThrowsAny<Exception>(() => SecretInjection.Prepare(["run", "--label", "x=${secret:db}", "alpine"], Lookup));

    private const string Yaml = """
        name: demo
        services:
          app:
            image: alpine
            environment:
              PW: ${secret:db}
        """;

    [Fact]
    public void Planner_KeepsTokenAndFlagsMissingSecrets()
    {
        var ok = ComposePlanner.Plan(Yaml, new ComposeOptions { SecretExists = _ => true });
        Assert.True(ok.CanRun);
        Assert.Contains("db", ok.SecretRefs);
        Assert.Contains(ok.Actions, a => a.Args.Contains("PW=${secret:db}"));

        var missing = ComposePlanner.Plan(Yaml, new ComposeOptions { SecretExists = _ => false });
        Assert.False(missing.CanRun);
        Assert.Contains("db", missing.MissingSecrets);
    }

    [Fact]
    public void Planner_MapsTopLevelSecretsToEnv()
    {
        var plan = ComposePlanner.Plan("""
            name: demo
            services:
              app:
                image: alpine
                secrets: [db_password]
            secrets:
              db_password: {}
            """, new ComposeOptions { SecretExists = _ => true });
        Assert.True(plan.CanRun);
        Assert.Contains(plan.Actions, a => a.Args.Contains("DB_PASSWORD=${secret:db_password}"));
    }

    [Fact]
    public void Planner_RejectsTokensInLabels()
    {
        var plan = ComposePlanner.Plan("""
            name: demo
            services:
              app:
                image: alpine
                labels: { x: "${secret:db}" }
            """, new ComposeOptions { SecretExists = _ => true });
        Assert.False(plan.CanRun);
    }
}
