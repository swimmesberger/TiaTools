using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>
/// The preview of a custom web control (spec 0017, FR-009 to FR-012): nothing of the control runs until the person
/// asks, after a notice that it runs the control's own code without runtime values. Its package is then written to a
/// temporary folder that holds only that control's files, and its start page opened; the folders go when the viewer
/// closes. Opens in the system browser, the spec's way where no embedded web view is available.
/// </summary>
public static class HmiWebControlPreview {
    private static readonly List<string> Folders = [];

    static HmiWebControlPreview() => AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveFolders();

    /// <summary>The bar above a control's files: a preview button, then the notice and the button that runs it.</summary>
    public static Control Bar(TiaHmiCustomWebControl control, Action<string> say) {
        var bar = new StackPanel { Spacing = 6 };
        var ask = new Button { Content = "Preview…", IsEnabled = control.HasPackage };
        ToolTip.SetTip(ask, control.HasPackage ? "Run the control's start page in the browser, after a notice" : "The archive does not hold the control's package");
        var notice = new Border {
            IsVisible = false, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#B45F06"), Background = new SolidColorBrush(Color.Parse("#22B45F06")),
        };
        var text = new TextBlock {
            TextWrapping = TextWrapping.Wrap, FontSize = 13,
            Text = "The preview runs the control's own code, as its author wrote it, in your web browser: without the HMI runtime, "
                + "so with no tag values, and with what the browser allows a local page, which may include reaching the network. "
                + "Its files are written to a temporary folder that holds nothing else, removed when the viewer closes.",
        };
        var run = new Button { Content = "Run it in the browser" };
        var cancel = new Button { Content = "Cancel" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(run);
        buttons.Children.Add(cancel);
        var inner = new StackPanel();
        inner.Children.Add(text);
        inner.Children.Add(buttons);
        notice.Child = inner;
        ask.Click += (_, _) => notice.IsVisible = true;
        cancel.Click += (_, _) => notice.IsVisible = false;
        run.Click += async (_, _) => {
            notice.IsVisible = false;
            try {
                var start = Unpack(control);
                var launcher = TopLevel.GetTopLevel(bar)?.Launcher;
                if (launcher is null || !await launcher.LaunchFileInfoAsync(new FileInfo(start))) {
                    say("Could not open the control's start page: " + start);
                } else {
                    say("Opened " + (control.DisplayName ?? control.Guid) + " in the browser, from " + Path.GetDirectoryName(start));
                }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) {
                say("Could not write the control's files: " + e.Message);
            }
        };
        bar.Children.Add(ask);
        bar.Children.Add(notice);
        return bar;
    }

    /// <summary>Writes the package to a new temporary folder and returns its start page's path.</summary>
    public static string Unpack(TiaHmiCustomWebControl control) {
        var folder = Path.Combine(Path.GetTempPath(), "TiaViewer-control-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        lock (Folders) {
            Folders.Add(folder);
        }
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var file in control.Files) {
            var target = Path.GetFullPath(Path.Combine(folder, file.Path));
            // A package's paths stay inside its folder.
            if (!target.StartsWith(root, StringComparison.Ordinal)) {
                throw new InvalidDataException("The package names a file outside its folder: " + file.Path);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, file.Content);
        }
        return Path.GetFullPath(Path.Combine(folder, StartPage(control)));
    }

    /// <summary>The start page the manifest names (<c>control.identity.start</c>), else <c>control/index.html</c>.</summary>
    public static string StartPage(TiaHmiCustomWebControl control) {
        var manifest = control.Files.FirstOrDefault(file => file.Path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifest is not null) {
            try {
                using var json = JsonDocument.Parse(manifest.Content);
                if (json.RootElement.TryGetProperty("control", out var part) && part.TryGetProperty("identity", out var identity)
                    && identity.TryGetProperty("start", out var start) && start.GetString() is { Length: > 0 } path) {
                    return path.Replace('\\', '/').TrimStart('.', '/');
                }
            } catch (JsonException) {
                // A manifest that is not JSON names no start page.
            }
        }
        return "control/index.html";
    }

    private static void RemoveFolders() {
        lock (Folders) {
            foreach (var folder in Folders) {
                try {
                    Directory.Delete(folder, recursive: true);
                } catch (IOException) {
                    // A browser may still hold a file; the system's temporary folder is cleaned in time.
                } catch (UnauthorizedAccessException) {
                }
            }
            Folders.Clear();
        }
    }
}
