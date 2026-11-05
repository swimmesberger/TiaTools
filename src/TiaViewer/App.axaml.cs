using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace TiaViewer;

public sealed class App : Application {
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var window = new MainWindow();
            desktop.MainWindow = window;
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
}
