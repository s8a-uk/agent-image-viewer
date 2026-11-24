namespace AgentImageViewer.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Settings_live_in_the_roaming_application_data_folder()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentImageViewer", "settings.json"),
            AppPaths.SettingsFile);
    }

    [Fact]
    public void The_image_cache_lives_in_the_local_application_data_folder()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentImageViewer", "cache"),
            AppPaths.CacheDirectory);
    }

    [Fact]
    public void The_error_log_lives_next_to_the_cache()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentImageViewer", "error.log"),
            AppPaths.ErrorLog);
    }
}
