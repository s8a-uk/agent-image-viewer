namespace AgentImageViewer.Core;

/// <summary>Per-user locations of the viewer's data.</summary>
public static class AppPaths
{
    private const string AppFolder = "AgentImageViewer";

    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolder,
        "settings.json");

    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolder,
        "cache");

    /// <summary>Unexpected errors that ended the viewer, for bug reports.</summary>
    public static string ErrorLog => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolder,
        "error.log");
}
