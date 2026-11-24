using Microsoft.Win32;

namespace AgentImageViewer.Services;

/// <summary>Registers the viewer under HKCU\...\Run so it starts with Windows.</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AgentImageViewer";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>
    /// Whether the viewer is registered, whichever exe the entry points to: after the exe has moved, the old entry
    /// still starts a viewer at logon, so it must show as enabled and be removable.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Registers this exe (replacing an entry for another path) or removes the entry.</summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
