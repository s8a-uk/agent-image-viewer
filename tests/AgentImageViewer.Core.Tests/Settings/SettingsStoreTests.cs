using AgentImageViewer.Core.Settings;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _file;
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _file = Path.Combine(_temp.Path, "AgentImageViewer", "settings.json");
        _store = new SettingsStore(_file);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_returns_the_defaults_when_there_is_no_file()
    {
        AssertDefaults(_store.Load());
        Assert.False(File.Exists(_file));
    }

    [Fact]
    public void Save_creates_the_folder_and_Load_reads_everything_back()
    {
        var settings = new AppSettings
        {
            MaxCards = 12,
            Topmost = false,
            PlaySound = false,
            Theme = AppTheme.Light,
            Opacity = 0.65,
            WindowWidth = 520.5,
            WindowLeft = -1200,
            WindowTop = 48,
            WindowMonitor = @"\\.\DISPLAY2",
            AnchorBottom = false,
        };

        _store.Save(settings);
        var loaded = _store.Load();

        Assert.Equal(12, loaded.MaxCards);
        Assert.False(loaded.Topmost);
        Assert.False(loaded.PlaySound);
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.Equal(0.65, loaded.Opacity);
        Assert.Equal(520.5, loaded.WindowWidth);
        Assert.Equal(-1200, loaded.WindowLeft);
        Assert.Equal(48, loaded.WindowTop);
        Assert.Equal(@"\\.\DISPLAY2", loaded.WindowMonitor);
        Assert.False(loaded.AnchorBottom);
    }

    /// <summary>Pins the file format: settings written by earlier versions must keep loading, and vice versa.</summary>
    [Fact]
    public void Save_writes_indented_json_with_pascal_case_names_and_the_theme_by_name()
    {
        _store.Save(new AppSettings { Theme = AppTheme.Dark, Opacity = 0.8, WindowWidth = 440 });

        Assert.Equal(
            """
            {
              "MaxCards": 5,
              "Topmost": true,
              "PlaySound": true,
              "Theme": "Dark",
              "Opacity": 0.8,
              "WindowWidth": 440,
              "WindowLeft": null,
              "WindowTop": null,
              "WindowMonitor": null,
              "AnchorBottom": true
            }
            """.ReplaceLineEndings("\n"),
            File.ReadAllText(_file).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Load_reads_a_file_written_by_an_earlier_version()
    {
        Write("""
            {
              "MaxCards": 8,
              "Topmost": false,
              "PlaySound": true,
              "Theme": "Light",
              "Opacity": 0.9,
              "WindowWidth": 600,
              "WindowLeft": 100,
              "WindowTop": 200,
              "AnchorBottom": false
            }
            """);

        var settings = _store.Load();

        Assert.Equal(8, settings.MaxCards);
        Assert.False(settings.Topmost);
        Assert.Equal(AppTheme.Light, settings.Theme);
        Assert.Equal(0.9, settings.Opacity);
        Assert.Equal(600, settings.WindowWidth);
        Assert.Equal(100, settings.WindowLeft);
        Assert.Equal(200, settings.WindowTop);
        Assert.False(settings.AnchorBottom);
    }

    [Fact]
    public void Load_keeps_the_defaults_for_properties_missing_from_the_file()
    {
        Write("""{ "MaxCards": 9 }""");

        var settings = _store.Load();

        Assert.Equal(9, settings.MaxCards);
        Assert.True(settings.Topmost);
        Assert.True(settings.PlaySound);
        Assert.Equal(1.0, settings.Opacity);
        Assert.True(settings.AnchorBottom);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    [InlineData(100000, 50)]
    public void Load_clamps_the_card_limit(int stored, int expected)
    {
        Write($$"""{ "MaxCards": {{stored}} }""");

        Assert.Equal(expected, _store.Load().MaxCards);
    }

    [Theory]
    [InlineData("0", 0.3)]
    [InlineData("0.29", 0.3)]
    [InlineData("0.3", 0.3)]
    [InlineData("0.75", 0.75)]
    [InlineData("1", 1.0)]
    [InlineData("7.5", 1.0)]
    public void Load_clamps_the_opacity(string stored, double expected)
    {
        Write($$"""{ "Opacity": {{stored}} }""");

        Assert.Equal(expected, _store.Load().Opacity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("not json at all")]
    [InlineData("""{ "MaxCards": """)]
    [InlineData("""{ "MaxCards": "many" }""")]
    [InlineData("""{ "Theme": "Sepia" }""")]
    [InlineData("[1, 2, 3]")]
    public void Load_falls_back_to_the_defaults_for_a_corrupt_file(string content)
    {
        Write(content);

        AssertDefaults(_store.Load());
    }

    [Fact]
    public void An_unreadable_value_keeps_its_default_without_resetting_the_others()
    {
        Write("""{ "MaxCards": 7, "Theme": "Sepia", "Opacity": "half", "Topmost": false }""");

        var settings = _store.Load();

        Assert.Equal(7, settings.MaxCards);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(1.0, settings.Opacity);
        Assert.False(settings.Topmost);
    }

    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        _store.Save(new AppSettings { MaxCards = 3 });

        Assert.Equal([_file], Directory.GetFiles(Path.GetDirectoryName(_file)!));
    }

    [Fact]
    public void Save_replaces_a_corrupt_file()
    {
        Write("garbage");

        _store.Save(new AppSettings { MaxCards = 3 });

        Assert.Equal(3, _store.Load().MaxCards);
    }

    [Fact]
    public void Save_does_not_throw_when_the_file_cannot_be_written()
    {
        // The settings "folder" is a file, so neither the folder nor the file can be created.
        var blocker = _temp.WriteFile("blocker", "");
        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));

        store.Save(new AppSettings());

        AssertDefaults(store.Load());
    }

    [Fact]
    public void The_limits_offered_by_the_settings_window_are_fixed()
    {
        Assert.Equal(1, AppSettings.MinMaxCards);
        Assert.Equal(50, AppSettings.MaxMaxCards);
        Assert.Equal(0.3, AppSettings.MinOpacity);
        Assert.Equal(1.0, AppSettings.MaxOpacity);
    }

    private void Write(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, content);
    }

    private static void AssertDefaults(AppSettings settings)
    {
        Assert.Equal(5, settings.MaxCards);
        Assert.True(settings.Topmost);
        Assert.True(settings.PlaySound);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(1.0, settings.Opacity);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.True(settings.AnchorBottom);
    }
}
