using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TiaViewer;

/// <summary>Name, version, icon, what the app is, and its licence.</summary>
public sealed partial class AboutWindow : Window {
    private static AboutWindow? _open;

    public AboutWindow() {
        InitializeComponent();
        using (var icon = AssetLoader.Open(new Uri("avares://TiaViewer/Assets/icon.png"))) {
            AppIcon.Source = new Bitmap(icon);
        }
        AppName.Text = AppInfo.Name;
        AppVersion.Text = "Version " + AppInfo.VersionWithCommit;
        RepositoryLink.Content = AppInfo.Repository.Host + AppInfo.Repository.AbsolutePath;
        ToolTip.SetTip(RepositoryLink, AppInfo.Repository.ToString());
        RepositoryLink.Click += async (_, _) => await Launcher.LaunchUriAsync(AppInfo.Repository);
        CloseButton.IsVisible = !OperatingSystem.IsMacOS();
        CloseButton.Click += (_, _) => Close();
        KeyDown += (_, e) => {
            // The menu bar's Close Window belongs to the main window, so a Mac's Cmd+W reaches this one as a key.
            if (e.Key == Key.Escape || (OperatingSystem.IsMacOS() && e.Key == Key.W && e.KeyModifiers == KeyModifiers.Meta)) {
                Close();
                e.Handled = true;
            }
        };
        Closed += (_, _) => _open = null;
    }

    /// <summary>Shows the window over another, or brings it forward if it is open already.</summary>
    public static void ShowFor(Window? owner) {
        if (_open is { } open) {
            open.Activate();
            return;
        }
        _open = new AboutWindow();
        // A Mac's About window stands beside the app's; on Windows it is a dialog that has to be closed first.
        if (owner is { IsVisible: true } && !OperatingSystem.IsMacOS()) {
            _ = _open.ShowDialog(owner);
        } else if (owner is { IsVisible: true }) {
            _open.Show(owner);
        } else {
            _open.Show();
        }
    }
}
