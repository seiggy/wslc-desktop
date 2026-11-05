using WslcDesktop.Settings;

namespace WslcDesktop.Tests.Settings;

public sealed class WslcConfigEditorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wslc-config-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public WslcConfigEditorTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.yaml");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private WslcConfigEditor Editor(string? yaml = null)
    {
        if (yaml is not null)
        {
            File.WriteAllText(_path, yaml);
        }

        var editor = new WslcConfigEditor(new SettingsFile(_path));
        editor.Load();
        return editor;
    }

    private string Text => File.ReadAllText(_path);

    [Fact]
    public void Drafts_start_from_the_values_in_the_file_and_default_when_unset()
    {
        var editor = Editor("session:\n  cpuCount: 2\n");

        Assert.Equal("2", editor.Get("cpuCount"));
        Assert.Equal("default", editor.Get("memorySize"));
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void An_invalid_draft_reports_its_message_and_blocks_saving()
    {
        var editor = Editor("session:\n  cpuCount: 2\n");

        editor.SetDraft("memorySize", "lots");

        Assert.NotNull(editor.ErrorFor("memorySize"));
        Assert.Null(editor.ErrorFor("cpuCount"));
        Assert.False(editor.IsValid);
        Assert.False(editor.Save());
        Assert.DoesNotContain("lots", Text);
    }

    [Fact]
    public void Fixing_the_draft_clears_the_error()
    {
        var editor = Editor();
        editor.SetDraft("memorySize", "lots");

        editor.SetDraft("memorySize", "8GB");

        Assert.Null(editor.ErrorFor("memorySize"));
        Assert.True(editor.IsValid);
    }

    [Fact]
    public void Saving_changes_only_the_edited_keys_and_keeps_comments_and_unknown_keys()
    {
        var editor = Editor("# keep me\nsession:\n  cpuCount: 2\n  memorySize: 4GB\nfuture: true\n");

        editor.SetDraft("memorySize", "8GB");

        Assert.True(editor.Save());
        Assert.Contains("# keep me", Text);
        Assert.Contains("  cpuCount: 2", Text);
        Assert.Contains("  memorySize: 8GB", Text);
        Assert.Contains("future: true", Text);
    }

    [Fact]
    public void Leaving_an_unset_key_at_default_adds_nothing_to_the_file()
    {
        var editor = Editor("session:\n  cpuCount: 2\n");

        Assert.True(editor.Save());

        Assert.DoesNotContain("memorySize", Text);
        Assert.DoesNotContain("idleTimeout", Text);
    }

    [Fact]
    public void Setting_an_active_key_to_default_comments_it_out_instead_of_deleting_it()
    {
        var editor = Editor("session:\n  cpuCount: 2\n");

        editor.SetDraft("cpuCount", "default");
        editor.Save();

        Assert.Contains("# cpuCount: default", Text);
        Assert.DoesNotContain("  cpuCount: 2", Text);
    }

    [Fact]
    public void Saving_a_missing_file_creates_it_from_the_template()
    {
        var editor = Editor();
        Assert.False(editor.FileExists);

        editor.SetDraft("cpuCount", "1");
        editor.Save();

        Assert.True(editor.FileExists);
        Assert.Contains("# wslc user settings", Text);
        Assert.Contains("  cpuCount: 1", Text);
    }

    [Fact]
    public void Reset_returns_every_key_to_default_keeps_comments_and_refreshes_the_drafts()
    {
        var editor = Editor("# keep me\nsession:\n  cpuCount: 2\n  memorySize: 4GB\n");
        editor.SetDraft("idleTimeout", "99");

        editor.Reset();

        Assert.Equal("default", editor.Get("cpuCount"));
        Assert.Equal("default", editor.Get("idleTimeout"));
        Assert.Contains("# keep me", Text);
        Assert.Contains("# cpuCount: default", Text);
        Assert.Contains("# memorySize: default", Text);
    }

    [Fact]
    public void Revert_discards_unsaved_drafts()
    {
        var editor = Editor("session:\n  cpuCount: 2\n");
        editor.SetDraft("cpuCount", "1");

        editor.Revert();

        Assert.Equal("2", editor.Get("cpuCount"));
    }
}
