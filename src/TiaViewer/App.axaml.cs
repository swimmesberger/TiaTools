using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace TiaViewer;

public sealed class App : Application {
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS()) {
            // The app menu. Without one, Avalonia puts "About Avalonia" there. It adds Services, Hide, Show All and Quit after these.
            var about = new NativeMenuItem("About " + AppInfo.Name);
            about.Click += (_, _) => AboutWindow.ShowFor((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
            NativeMenu.SetMenu(this, new NativeMenu { about });
        }
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // Closing the viewer ends it, also while the About window is open.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (OperatingSystem.IsMacOS()) {
                window.Opened += (_, _) => FixAppMenu();
            }
            // Windows passes a double-clicked file as an argument. macOS sends it as an event, also to a running app.
            if (desktop.Args is [var path, ..]) {
                window.OpenWhenShown(path);
            }
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable) {
                activatable.Activated += (_, activation) => {
                    if (activation is FileActivatedEventArgs { Files: [var file, ..] } && file.TryGetLocalPath() is { } opened) {
                        window.OpenWhenShown(opened);
                    }
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Brings the items Avalonia adds to the app menu in line with other Mac apps: it calls the last one just "Quit",
    /// where a Mac app says whom it quits, and gives Hide Others Option+Cmd+Q, which is Option+Cmd+H everywhere else.
    /// </summary>
    private void FixAppMenu() {
        var items = NativeMenu.GetMenu(this)?.Items.OfType<NativeMenuItem>().ToList() ?? [];
        if (items.LastOrDefault() is { Header: "Quit" } quit) {
            quit.Header = "Quit " + AppInfo.Name;
        }
        if (items.FirstOrDefault(item => item.Header == "Hide Others") is { } hideOthers) {
            hideOthers.Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt);
        }
    }
}
