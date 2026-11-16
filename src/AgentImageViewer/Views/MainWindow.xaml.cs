using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentImageViewer.Core.Cards;
using AgentImageViewer.Core.Placement;
using AgentImageViewer.Core.Settings;
using AgentImageViewer.Imaging;
using AgentImageViewer.Interop;
using AgentImageViewer.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AgentImageViewer.Views;

public partial class MainWindow : FluentWindow
{
    private const double MaxHeightRatio = 0.9;

    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CardBoard _board;
    private readonly WindowSnapper _snapper;
    private readonly WindowOpacity _opacity;
    private readonly NotificationSound _sound = new();
    private bool _positioned;
    private bool _closeAllowed;
    private double _userWidth;
    private Point? _pressPoint;
    private DependencyObject? _pressTarget;

    public MainWindow(AppSettings settings, SettingsStore settingsStore, CardBoard board)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _board = board;
        InitializeComponent();

        CardList.ItemsSource = board.Cards;
        // The window is only ever visible while it has cards.
        ((INotifyCollectionChanged)board.Cards).CollectionChanged += (_, _) =>
        {
            if (board.Cards.Count == 0)
            {
                Hide();
            }
        };

        Topmost = settings.Topmost;
        MaxHeight = SystemParameters.WorkArea.Height * MaxHeightRatio;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _snapper = new WindowSnapper(this);
        _opacity = new WindowOpacity(this) { Value = settings.Opacity };
        _snapper.MoveCompleted += (_, e) =>
        {
            // A user resize switches SizeToContent to Manual; the height must keep following the cards.
            SizeToContent = SizeToContent.Height;
            if (e.Resized)
            {
                _userWidth = Width;
            }

            UpdateMaxHeight();
            SavePosition();
        };
        Loaded += (_, _) => RestorePosition();

        if (settings.WindowWidth is { } width)
        {
            Width = Math.Clamp(width, MinWidth, MaxWidth);
        }

        _userWidth = Width;
        DpiChanged += OnDpiChanged;
    }

    public event EventHandler? SettingsRequested;

    public bool HasCards => _board.Cards.Count > 0;

    public event EventHandler? ExitRequested;

    public void AddCard(ResponseCard card)
    {
        _board.Add(card);
        ShowWithoutFocus();
        Dispatcher.BeginInvoke(Scroller.ScrollToEnd, DispatcherPriority.Loaded);

        if (_settings.PlaySound)
        {
            _sound.Play();
        }
    }

    public void ShowWithoutFocus()
    {
        if (!IsVisible && HasCards)
        {
            ShowActivated = false;
            Show();
        }
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowWithoutFocus();
        }
    }

    public void ApplySettings()
    {
        Topmost = _settings.Topmost;
        _opacity.Value = _settings.Opacity;
        _board.MaxCards = _settings.MaxCards;
    }

    public void ClearCards() => _board.Clear();

    /// <summary>Lets the next close really close the window; until then, closing only hides it.</summary>
    public void AllowClose() => _closeAllowed = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 must not destroy the window: the app lives on in the tray and shows it again for the next card.
        if (!_closeAllowed)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _sound.Dispose();
        base.OnClosed(e);
    }

    // Raised on a system thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateMaxHeight);

    // The height follows the content up to most of the current monitor's work area; beyond that the cards scroll.
    private void UpdateMaxHeight()
    {
        if (_snapper.CurrentWorkArea is { } work)
        {
            MaxHeight = work.Height * MaxHeightRatio;
        }
    }

    private void RestorePosition()
    {
        if (_positioned)
        {
            return;
        }

        _positioned = true;
        if (_snapper.Bounds is not { } bounds)
        {
            return;
        }

        var position = WindowPlacement.Resolve(_settings, WindowSnapper.GetMonitors(), bounds.Width, bounds.Height);
        _snapper.IsAnchoredBottom = position.AnchorBottom;
        _snapper.MoveTo(position.Left, position.Top);
        UpdateMaxHeight();
    }

    // Moving to a monitor with another scale factor makes WPF (with the WPF UI window chrome) apply the change twice
    // to the window's size, so it shrinks or grows on every crossing. Put back the width the user chose.
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            Width = _userWidth;
            UpdateMaxHeight();
        });

    private void SavePosition()
    {
        if (_snapper.Bounds is not { } bounds)
        {
            return;
        }

        _settings.WindowWidth = _userWidth;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowMonitor = _snapper.CurrentMonitorName;
        _settings.AnchorBottom = _snapper.IsAnchoredBottom;
        _settingsStore.Save(_settings);
    }

    // The whole window drags. A press that is released without moving is a click,
    // and only a click on a thumbnail does anything. Scroll bars keep their own behavior.
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ScrollBar>(e.OriginalSource) is not null)
        {
            return;
        }

        _pressPoint = e.GetPosition(this);
        _pressTarget = e.OriginalSource as DependencyObject;
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pressPoint = null;
        _pressTarget = null;
        DragMove();
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressPoint is null)
        {
            return;
        }

        var target = _pressTarget;
        _pressPoint = null;
        _pressTarget = null;
        e.Handled = true;

        if (FindThumbnail(target) is { } thumbnail)
        {
            OpenPreview(thumbnail);
        }
    }

    // Reached from the keyboard only: the window takes every mouse press to tell a click from a drag.
    private void OnThumbnailClick(object sender, RoutedEventArgs e) => OpenPreview((FrameworkElement)sender);

    private void OpenPreview(FrameworkElement thumbnail)
    {
        if (thumbnail.DataContext is not ThumbnailImage image)
        {
            return;
        }

        var text = FindAncestor<FrameworkElement>(thumbnail, e => e.DataContext is ResponseCard)?.DataContext is ResponseCard card
            ? card.Text
            : "";
        new ImagePreviewWindow(image, text, this).Show();
    }

    private static FrameworkElement? FindThumbnail(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = GetParent(node))
        {
            if (node is FrameworkElement { Tag: "Thumbnail" } element)
            {
                return element;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(object? source, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (var node = source as DependencyObject; node is not null; node = GetParent(node))
        {
            if (node is T match && (predicate is null || predicate(match)))
            {
                return match;
            }
        }

        return null;
    }

    // Text elements such as Run are not Visuals, so fall back to the logical tree for them.
    private static DependencyObject? GetParent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void OnCopyTextClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ResponseCard { HasText: true } card)
        {
            try
            {
                Clipboard.SetText(card.Text);
            }
            catch (ExternalException)
            {
                // Another application holds the clipboard; the user can simply try again.
            }
        }
    }

    private void OnRemoveCardClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ResponseCard card)
        {
            _board.Remove(card);
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnClearClick(object sender, RoutedEventArgs e) => ClearCards();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
