using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>
/// The menu bar: on a Mac at the top of the screen, elsewhere a bar in the window. Its items run what the toolbar and the
/// context menus run, and act on the text that has the focus where that is what the platform's Edit menu does.
/// </summary>
public sealed partial class MainWindow {
    private static readonly bool IsMac = OperatingSystem.IsMacOS();

    private readonly RecentFiles _recent;
    private readonly NativeMenu _recentMenu = new();
    private readonly List<(NativeMenuItem Item, Action Run)> _menuCommands = [];
    private NativeMenuItem? _exportAllItem;
    private NativeMenuItem? _exportBlockItem;
    private NativeMenuItem? _findItem;

    private void BuildMenu() {
        var command = IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        var file = new NativeMenu {
            Item("_Open…", new KeyGesture(Key.O, command), () => _ = PickAndOpenAsync()),
            new NativeMenuItem(Label("Open Recent", "Open _recent")) { Menu = _recentMenu },
            new NativeMenuItemSeparator(),
            (_exportAllItem = Item(Label("Export All…", "_Export all…"), null, () => _ = PickAndExportAsync(null))),
            (_exportBlockItem = Item(Label("Export Block…", "Export _block…"), null, ExportCurrent)),
            new NativeMenuItemSeparator(),
            // A Mac app quits from its app menu, which Avalonia fills; its File menu closes the window.
            IsMac ? Item("Close Window", new KeyGesture(Key.W, command), Close) : Item("E_xit", null, Close),
        };
        var edit = new NativeMenu {
            Item("_Copy", new KeyGesture(Key.C, command), CopySelection),
            Item(Label("Select All", "Select _all"), new KeyGesture(Key.A, command), SelectAllText),
            new NativeMenuItemSeparator(),
            (_findItem = Item(Label("Find in Block…", "_Find in block…"), new KeyGesture(Key.F, command), FindInBlock)),
            Item(Label("Search All Source…", "_Search all source…"), new KeyGesture(Key.F, command | KeyModifiers.Shift), SearchAllSource),
        };
        var menu = new NativeMenu {
            new NativeMenuItem(Label("File", "_File")) { Menu = file },
            new NativeMenuItem(Label("Edit", "_Edit")) { Menu = edit },
        };
        // A Mac has About in the app menu (see App); Windows has it under Help.
        if (!IsMac) {
            menu.Add(new NativeMenuItem("_Help") { Menu = new NativeMenu { Item("_About " + AppInfo.Name, null, () => AboutWindow.ShowFor(this)) } });
        }
        NativeMenu.SetMenu(this, menu);
        _recent.Changed += (_, _) => ShowRecent();
        ShowRecent();
        UpdateMenu();
    }

    private static string Label(string mac, string other) => IsMac ? mac : other;

    private NativeMenuItem Item(string header, KeyGesture? gesture, Action run) {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) => run();
        _menuCommands.Add((item, run));
        return item;
    }

    /// <summary>
    /// Runs the menu item a key belongs to. A menu in the window shows shortcuts but does not run them. A Mac's menu runs them
    /// before the window sees the key, except a few it does not recognise, such as Shift+Cmd+F: those arrive here.
    /// </summary>
    private bool RunMenuShortcut(KeyEventArgs e) {
        foreach (var (item, run) in _menuCommands) {
            if (item.IsEnabled && item.Gesture is { } gesture && gesture.Matches(e)) {
                run();
                return true;
            }
        }
        return false;
    }

    private void UpdateMenu() {
        _exportAllItem!.IsEnabled = _project is not null;
        _exportBlockItem!.IsEnabled = _current is not null;
        _findItem!.IsEnabled = Editor.IsVisible;
    }

    private void ExportCurrent() {
        if (_current is { } item) {
            _ = PickAndExportAsync(new TiaTreeNode(item.Name, item));
        }
    }

    /// <summary>
    /// Copies what is selected in the text that has the focus. Elsewhere, such as in the tree, it copies the source of the block shown,
    /// as the Copy button does.
    /// </summary>
    private void CopySelection() {
        if (FocusManager?.GetFocusedElement() is TextBox box) {
            box.Copy();
        } else if (Editor.TextArea.IsKeyboardFocusWithin) {
            // The editor's own copy, which takes the whole line when nothing is selected.
            Editor.Copy();
        } else if (_current is { } item) {
            _ = CopySourceAsync(item);
        }
    }

    private void SelectAllText() {
        if (FocusManager?.GetFocusedElement() is TextBox box) {
            box.SelectAll();
        } else if (Editor.TextArea.IsKeyboardFocusWithin) {
            Editor.SelectAll();
        }
    }

    /// <summary>The editor's own find bar, as its own Cmd or Ctrl + F opens it, also when the focus is elsewhere.</summary>
    private void FindInBlock() {
        if (Editor.IsVisible) {
            AvaloniaEdit.ApplicationCommands.Find.Execute(null, Editor.TextArea);
        }
    }

    private void SearchAllSource() {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Fills File, Open Recent and the start screen's list from the recent files.</summary>
    private void ShowRecent() {
        var paths = _recent.Paths;
        var labels = RecentFiles.Labels(paths);
        _recentMenu.Items.Clear();
        for (var index = 0; index < paths.Count; index++) {
            var path = paths[index];
            // An underscore would mark an access key, and a Mac's menu would drop it: doubled, it is shown as it is.
            var item = new NativeMenuItem(labels[index].Replace("_", "__", StringComparison.Ordinal)) { ToolTip = path };
            item.Click += (_, _) => _ = OpenAsync(path);
            _recentMenu.Items.Add(item);
        }
        if (paths.Count != 0) {
            _recentMenu.Items.Add(new NativeMenuItemSeparator());
        }
        var clear = new NativeMenuItem(Label("Clear Menu", "_Clear menu")) { IsEnabled = paths.Count != 0 };
        clear.Click += (_, _) => ClearRecent();
        _recentMenu.Items.Add(clear);

        RecentList.ItemsSource = paths.Select(path => new RecentRow(path)).ToList();
        RecentPanel.IsVisible = WelcomeOpen.IsVisible && paths.Count != 0;
    }

    private void RememberOpened(string path) {
        _recent.Add(path);
        if (OperatingSystem.IsMacOS()) {
            MacRecentDocuments.Note(Path.GetFullPath(path));
        }
    }

    private void ClearRecent() {
        _recent.Clear();
        if (OperatingSystem.IsMacOS()) {
            MacRecentDocuments.Clear();
        }
    }

    private void OnRecentClick(object? sender, RoutedEventArgs e) {
        if (sender is Button { DataContext: RecentRow row }) {
            _ = OpenAsync(row.Path);
        }
    }
}
