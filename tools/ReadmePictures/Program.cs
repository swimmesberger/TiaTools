using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using TiaFormat.Program;
using TiaViewer;

// Takes the README's pictures of the viewer from the demo project, in light and dark, rendered without a screen.
// Usage: ReadmePictures <demo archive> <output folder>
if (args.Length != 2) {
    Console.Error.WriteLine("usage: ReadmePictures <demo archive> <output folder>");
    return 2;
}
var archive = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);

// The window's client area. The README shows the pictures at this size, so a change here changes every picture.
const int width = 1280;
const int height = 792;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// The viewer adds what it opens to its recent files; keep the demo out of the user's list.
var recentFile = Path.Combine(Path.GetTempPath(), $"tia-readme-pictures-{Environment.ProcessId}.txt");
try {
    foreach (var (theme, suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") }) {
        var window = Open(theme);

        // The line, scrolled down to its calls, with what it uses below and a name under the pointer.
        var fillingLine = Item(window, "FillingLine");
        var callLine = window.Project!.Document(fillingLine).Text.Split('\n').ToList().FindIndex(l => l.Contains("#filler(start", StringComparison.Ordinal));
        window.Navigate(fillingLine, 0);
        Pump();
        window.FindControl<TextEditor>("Editor")!.ScrollToLine(callLine + 1);
        Pump();
        window.FindControl<TabControl>("Panels")!.SelectedIndex = 1;
        Pump();
        Hover(window, fillingLine, "Recipes");
        Shot(window, "viewer-" + suffix);

        // Who uses the motor block, walked to the first place.
        window.Navigate(Item(window, "Motor"), 0);
        Pump();
        window.FindControl<TabControl>("Panels")!.SelectedIndex = 0;
        window.FindControl<ListBox>("UsedByList")!.SelectedIndex = 0;
        Pump();
        Shot(window, "references-" + suffix);

        // Search across all source, with one result open.
        window.FindControl<TextBox>("SearchBox")!.Text = "fault";
        Wait(window.SearchAsync("fault"));
        window.FindControl<ListBox>("ResultsList")!.SelectedIndex = 2;
        Pump();
        Shot(window, "search-" + suffix);

        window.Close();
        Pump();
    }
} finally {
    File.Delete(recentFile);
}
return 0;

MainWindow Open(ThemeVariant theme) {
    Application.Current!.RequestedThemeVariant = theme;
    var window = new MainWindow(new RecentFiles(recentFile)) { Width = width, Height = height };
    window.Show();
    Pump();
    Wait(window.OpenAsync(archive));
    if (window.Project is null) {
        throw new InvalidOperationException($"The viewer could not open {archive}.");
    }
    return window;
}

static TiaItem Item(MainWindow window, string name) =>
    window.Project!.Items.FirstOrDefault(i => i.Name == name && i.IsProject)
    ?? throw new InvalidOperationException($"No block {name}: is this the demo project?");

// Moves the pointer onto a name in the source with Ctrl or Cmd held, which underlines it as a link.
static void Hover(MainWindow window, TiaItem block, string target) {
    var editor = window.FindControl<TextEditor>("Editor")!;
    var token = window.Project!.Document(block).Tokens.First(t => t.Target == target);
    var textView = editor.TextArea.TextView;
    textView.EnsureVisualLines();
    var location = editor.Document.GetLocation(token.Start + 2);
    var visual = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineMiddle);
    var point = textView.TranslatePoint(new Point(visual.X - textView.ScrollOffset.X, visual.Y - textView.ScrollOffset.Y), window)!.Value;
    window.MouseMove(point, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
    Pump();
}

void Shot(Window window, string name) {
    Pump();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
    Pump();
    var path = Path.Combine(output, name + ".png");
    window.CaptureRenderedFrame()!.Save(path, PngBitmapEncoderOptions.Default);
    Console.WriteLine(path);
}

// Runs the UI thread's work until the condition holds, or for one round without one.
static void Pump(Func<bool>? until = null) {
    var clock = Stopwatch.StartNew();
    do {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    } while (until is not null && !until() && clock.Elapsed < TimeSpan.FromSeconds(30));
    Dispatcher.UIThread.RunJobs();
}

static void Wait(Task task) {
    Pump(() => task.IsCompleted);
    task.GetAwaiter().GetResult();
}
