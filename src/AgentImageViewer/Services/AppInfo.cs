using System.Reflection;

namespace AgentImageViewer.Services;

public static class AppInfo
{
    /// <summary>The version with the source revision it was built from (e.g. "0.1.0+3ec7a0e…"), for bug reports.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
