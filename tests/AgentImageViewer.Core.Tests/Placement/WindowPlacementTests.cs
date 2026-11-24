using AgentImageViewer.Core.Placement;
using AgentImageViewer.Core.Settings;

namespace AgentImageViewer.Core.Tests.Placement;

public sealed class WindowPlacementTests
{
    private const double Width = 400;
    private const double Height = 200;

    // A landscape primary monitor with a taskbar, and a portrait monitor to its left.
    private static readonly DisplayMonitor Primary = new(
        @"\\.\DISPLAY1",
        new ScreenRect(0, 0, 3072, 1728),
        new ScreenRect(0, 0, 3072, 1680),
        IsPrimary: true);

    private static readonly DisplayMonitor Left = new(
        @"\\.\DISPLAY2",
        new ScreenRect(-1728, -648, 1728, 3072),
        new ScreenRect(-1728, -648, 1728, 3024),
        IsPrimary: false);

    private static readonly DisplayMonitor[] Both = [Primary, Left];

    [Fact]
    public void Without_a_saved_position_the_window_goes_to_the_bottom_right_of_the_primary_monitor()
    {
        var placement = WindowPlacement.Resolve(new AppSettings(), [Left, Primary], Width, Height);

        Assert.Equal(new WindowPosition(3072 - Width - WindowPlacement.DefaultMargin, 1680 - Height, AnchorBottom: true), placement);
    }

    [Fact]
    public void The_saved_position_on_the_saved_monitor_is_restored()
    {
        var settings = Saved(-1200, 400, @"\\.\DISPLAY2");

        Assert.Equal(new WindowPosition(-1200, 400, false), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void A_window_docked_to_the_bottom_is_put_back_on_the_bottom_edge_at_its_current_height()
    {
        // Saved while it was taller: its top was higher up than a shorter window's would be.
        var settings = Saved(-1200, 1900, @"\\.\DISPLAY2", anchorBottom: true);

        Assert.Equal(new WindowPosition(-1200, 2376 - Height, true), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void A_docked_window_without_a_saved_monitor_docks_on_the_monitor_its_position_is_on()
    {
        var settings = Saved(500, 1000, monitor: null, anchorBottom: true);

        Assert.Equal(new WindowPosition(500, 1680 - Height, true), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void The_monitor_name_is_matched_case_insensitively()
    {
        var settings = Saved(-1200, 400, @"\\.\display2");

        Assert.Equal(new WindowPosition(-1200, 400, false), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void A_saved_position_that_is_no_longer_on_the_saved_monitor_moves_to_that_monitors_default_corner()
    {
        // The monitors were rearranged: the saved position now lies on the primary monitor.
        var settings = Saved(500, 400, @"\\.\DISPLAY2");

        Assert.Equal(WindowPlacement.DefaultOn(Left, Width, Height), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void When_the_saved_monitor_is_disconnected_a_position_on_another_monitor_is_kept()
    {
        var settings = Saved(500, 400, @"\\.\DISPLAY3");

        Assert.Equal(new WindowPosition(500, 400, false), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void When_the_saved_monitor_is_disconnected_and_the_position_is_off_screen_the_primary_default_is_used()
    {
        var settings = Saved(-1200, 400, @"\\.\DISPLAY2");

        Assert.Equal(WindowPlacement.DefaultOn(Primary, Width, Height), WindowPlacement.Resolve(settings, [Primary], Width, Height));
    }

    [Fact]
    public void Settings_from_before_the_monitor_was_saved_keep_a_position_that_is_on_any_monitor()
    {
        var settings = Saved(-1200, 400, monitor: null);

        Assert.Equal(new WindowPosition(-1200, 400, false), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void A_position_inside_the_bounding_box_but_between_monitors_counts_as_off_screen()
    {
        // Above the primary monitor and right of the portrait one: inside the virtual screen, but on no monitor.
        var settings = Saved(500, -400, monitor: null);

        Assert.Equal(WindowPlacement.DefaultOn(Primary, Width, Height), WindowPlacement.Resolve(settings, Both, Width, Height));
    }

    [Fact]
    public void A_window_whose_grab_area_is_off_the_monitor_counts_as_off_screen()
    {
        // Only the last 10 pixels of the window are on the primary monitor's left edge.
        var settings = Saved(-Width + 10, 400, @"\\.\DISPLAY1");

        Assert.Equal(WindowPlacement.DefaultOn(Primary, Width, Height), WindowPlacement.Resolve(settings, [Primary], Width, Height));
    }

    [Fact]
    public void Without_a_primary_flag_the_first_monitor_is_the_fallback()
    {
        var unflagged = Primary with { IsPrimary = false };

        Assert.Equal(WindowPlacement.DefaultOn(unflagged, Width, Height), WindowPlacement.Resolve(new AppSettings(), [unflagged, Left with { IsPrimary = false }], Width, Height));
    }

    [Fact]
    public void Resolve_needs_at_least_one_monitor()
    {
        Assert.Throws<ArgumentException>(() => WindowPlacement.Resolve(new AppSettings(), [], Width, Height));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(99.9, 49.9, true)]
    [InlineData(100, 0, false)]
    [InlineData(0, 50, false)]
    [InlineData(-0.1, 0, false)]
    public void ScreenRect_contains_its_top_left_edge_but_not_its_bottom_right_edge(double x, double y, bool expected)
    {
        Assert.Equal(expected, new ScreenRect(0, 0, 100, 50).Contains(x, y));
    }

    private static AppSettings Saved(double left, double top, string? monitor, bool anchorBottom = false) => new()
    {
        WindowLeft = left,
        WindowTop = top,
        WindowMonitor = monitor,
        AnchorBottom = anchorBottom,
    };
}
