namespace AgentImageViewer.Core.Settings;

/// <summary>User settings. The public properties are the content of settings.json.</summary>
public sealed class AppSettings
{
    public const int MinMaxCards = 1;
    public const int MaxMaxCards = 50;
    public const double MinOpacity = 0.3;
    public const double MaxOpacity = 1.0;

    public int MaxCards { get; set; } = 5;

    public bool Topmost { get; set; } = true;

    public bool PlaySound { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Opacity of the whole main window, <see cref="MinOpacity"/> to <see cref="MaxOpacity"/>.</summary>
    public double Opacity { get; set; } = MaxOpacity;

    /// <summary>Width chosen by the user, in device-independent units.</summary>
    public double? WindowWidth { get; set; }

    /// <summary>Left edge of the window in physical screen pixels, which mean the same on every monitor.</summary>
    public double? WindowLeft { get; set; }

    /// <summary>Top edge of the window in physical screen pixels.</summary>
    public double? WindowTop { get; set; }

    /// <summary>Device name of the monitor the window was last on, such as <c>\\.\DISPLAY2</c>.</summary>
    public string? WindowMonitor { get; set; }

    public bool AnchorBottom { get; set; } = true;

    public static int ClampMaxCards(int value) => Math.Clamp(value, MinMaxCards, MaxMaxCards);

    public static double ClampOpacity(double value) => Math.Clamp(value, MinOpacity, MaxOpacity);
}
