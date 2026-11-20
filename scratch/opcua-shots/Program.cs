using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TiaFormat.Tests;
using TiaViewer;

// Usage: OpcUaShots <output folder>. Builds the invented project of the tests and takes the viewer's OPC UA entry.
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var folder = Directory.CreateTempSubdirectory("opcua-shots").FullName;
Directory.CreateDirectory(Path.Combine(folder, "System"));
var file = TiaOpcUaFixture.NewFile(new TiaOpcUaFixture.Options { HasInterface = true });
file.Commit();
File.WriteAllBytes(Path.Combine(folder, "System", "PEData.plf"), file.ToArray());
var project = Path.Combine(folder, "Filling line.ap21");
File.WriteAllText(project, "<Project />");

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var recent = Path.Combine(folder, "recent.txt");
foreach (var (theme, suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") }) {
    Application.Current!.RequestedThemeVariant = theme;
    var window = new MainWindow(new RecentFiles(recent)) { Width = 1280, Height = 792 };
    window.Show();
    Pump();
    Wait(window.OpenAsync(project));
    var report = window.Project!.Reports.Single(item => item.ReportPlc == "Filler");
    window.Navigate(report, 0);
    Pump();
    Shot(window, "opcua-server-" + suffix);
    if (theme == ThemeVariant.Light) {
        var tabs = window.FindControl<OpcUaView>("OpcUaPanel")!.GetVisualDescendants().OfType<TabControl>().First();
        foreach (var (index, name) in new[] { (1, "opcua-roles-light"), (2, "opcua-datablocks-light"), (3, "opcua-tags-light") }) {
            tabs.SelectedIndex = index;
            Pump();
            Shot(window, name);
        }
        // Open a data block from the view, then come back.
        var open = typeof(MainWindow).GetMethod("OpenFromOpcUa", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        open.Invoke(window, ["Recipe"]);
        Pump();
        Console.WriteLine($"after open: heading={window.FindControl<TextBlock>("Heading")!.Text} editor={window.FindControl<Control>("Editor")!.IsVisible} view={window.FindControl<Control>("OpcUaPanel")!.IsVisible} panels={window.FindControl<Control>("Panels")!.IsVisible}");
        Shot(window, "opcua-opened-light");
        typeof(MainWindow).GetMethod("GoBack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, []);
        Pump();
        Console.WriteLine($"after back: heading={window.FindControl<TextBlock>("Heading")!.Text} editor={window.FindControl<Control>("Editor")!.IsVisible} view={window.FindControl<Control>("OpcUaPanel")!.IsVisible} panels={window.FindControl<Control>("Panels")!.IsVisible}");
        open.Invoke(window, ["Level"]);
        Pump();
        Console.WriteLine($"tag: heading={window.FindControl<TextBlock>("Heading")!.Text}");
    }
    window.Close();
    Pump();
}
Directory.Delete(folder, recursive: true);
return 0;

void Shot(Window window, string name) {
    Pump();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
    Pump();
    var path = Path.Combine(output, name + ".png");
    window.CaptureRenderedFrame()!.Save(path, PngBitmapEncoderOptions.Default);
    Console.WriteLine(path);
}

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
