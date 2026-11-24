using System.Windows;
using AgentImageViewer.Core.Settings;
using AgentImageViewer.Services;
using Wpf.Ui.Controls;

namespace AgentImageViewer.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;

    public SettingsWindow(AppSettings settings, SettingsStore store)
    {
        _settings = settings;
        _store = store;
        InitializeComponent();

        MaxCardsBox.Minimum = AppSettings.MinMaxCards;
        MaxCardsBox.Maximum = AppSettings.MaxMaxCards;
        MaxCardsBox.Value = settings.MaxCards;
        TopmostSwitch.IsChecked = settings.Topmost;
        SoundSwitch.IsChecked = settings.PlaySound;
        ThemeBox.SelectedIndex = (int)settings.Theme;
        OpacitySlider.Minimum = AppSettings.MinOpacity * 100;
        OpacitySlider.Value = settings.Opacity * 100;
        StartupSwitch.IsChecked = StartupRegistration.IsEnabled;
        VersionText.Text = $"バージョン {AppInfo.Version}";
        VersionText.ToolTip = VersionText.Text;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.MaxCards = AppSettings.ClampMaxCards((int)Math.Round(MaxCardsBox.Value ?? _settings.MaxCards));
        _settings.Topmost = TopmostSwitch.IsChecked == true;
        _settings.PlaySound = SoundSwitch.IsChecked == true;
        _settings.Theme = (AppTheme)Math.Max(ThemeBox.SelectedIndex, 0);
        _settings.Opacity = AppSettings.ClampOpacity(OpacitySlider.Value / 100);
        _store.Save(_settings);

        // Always written, so an entry for an exe that has since moved points to this one again.
        StartupRegistration.SetEnabled(StartupSwitch.IsChecked == true);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
