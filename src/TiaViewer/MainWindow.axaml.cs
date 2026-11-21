using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Search;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaViewer;

public sealed partial class MainWindow : Window {
    private const int ResultLimit = 2000;

    /// <summary>What the follow gesture is called here.</summary>
    private static readonly string Command = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
    private static readonly string Option = OperatingSystem.IsMacOS() ? "Option" : "Alt";

    private static readonly IBrush LightSelection = Brush.Parse("#332563EB");
    private static readonly IBrush DarkSelection = Brush.Parse("#554C86F5");
    private static readonly IBrush LightCurrentLine = Brush.Parse("#0A000000");
    private static readonly IBrush DarkCurrentLine = Brush.Parse("#0FFFFFFF");
    private static readonly IBrush LightLineNumbers = Brush.Parse("#9CA3AF");
    private static readonly IBrush DarkLineNumbers = Brush.Parse("#6B7280");

    private readonly TokenColorizer _colorizer = new();
    private readonly Stack<TiaLocation> _back = new();
    private readonly Stack<TiaLocation> _forward = new();
    private readonly Dictionary<TiaItem, TreeRow> _rows = [];
    private readonly ObservableCollection<TreeRow> _roots = [];
    private readonly Dictionary<TiaItem, string> _paths = [];
    private TiaProject? _project;
    private TiaItem? _current;
    private GridLength _panelsHeight = new(230);
    private bool _selectingFromCode;
    private string? _pending;
    private string? _exported;

    public MainWindow() : this(new RecentFiles(RecentFiles.DefaultFile)) {
    }

    /// <param name="recent">The list of recently opened files, which <see cref="OpenAsync"/> adds to.</param>
    public MainWindow(RecentFiles recent) {
        _recent = recent;
        InitializeComponent();
        Tree.ItemsSource = _roots;
        using (var icon = AssetLoader.Open(new Uri("avares://TiaViewer/Assets/icon.png"))) {
            WelcomeIcon.Source = new Bitmap(icon);
        }
        using (var icon = AssetLoader.Open(new Uri("avares://TiaViewer/Assets/icon.png"))) {
            Icon = new WindowIcon(icon);
        }
        ToolTip.SetTip(BackButton, $"Back ({Option}+Left)");
        ToolTip.SetTip(ForwardButton, $"Forward ({Option}+Right)");
        ToolTip.SetTip(OpenButton, $"Open an archive or project ({Command}+O)");
        ToolTip.SetTip(SearchBox, $"Search all source ({Command}+Shift+F)");
        Hint.Text = "Read-only";
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.SelectionCornerRadius = 2;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        // Room around the line numbers, which otherwise touch the edge and the text.
        Editor.TextArea.LeftMargins.Insert(0, new Border { Width = 6 });
        Editor.TextArea.LeftMargins.Add(new Border { Width = 8 });
        Editor.TextArea.Caret.PositionChanged += (_, _) => ShowCaret();
        Editor.IsVisible = false;
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        SearchPanel.Install(Editor);
        BuildMenu();

        OpenButton.Click += async (_, _) => await PickAndOpenAsync();
        WelcomeOpen.Click += async (_, _) => await PickAndOpenAsync();
        ExportAllButton.Click += async (_, _) => await PickAndExportAsync(null);
        ExportBlockButton.Click += (_, _) => ExportCurrent();
        CopyButton.Click += async (_, _) => {
            if (_current is { } item) {
                await CopySourceAsync(item);
            }
        };
        ShowFolderButton.Click += async (_, _) => {
            if (_exported is { } folder) {
                await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
            }
        };
        ToolTip.SetTip(ExportAllButton, "Write every block and tag table as readable files, in their folders");
        BackButton.Click += (_, _) => GoBack();
        ForwardButton.Click += (_, _) => GoForward();
        Tree.SelectionChanged += OnTreeSelection;
        TreeFilter.TextChanged += (_, _) => FillTree();
        SearchBox.KeyDown += OnSearchKey;
        UsedByList.SelectionChanged += OnPlaceSelected;
        UsesList.SelectionChanged += OnPlaceSelected;
        ResultsList.SelectionChanged += OnPlaceSelected;
        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnWindowKey;
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Opened += (_, _) => {
            ApplyTheme();
            if (_pending is { } path) {
                _pending = null;
                _ = OpenAsync(path);
            }
        };

        OpcUaPanel.Open += OpenFromOpcUa;
        DevicesPanel.Open += OpenConstant;
        SafetyPanel.Open += OpenFromOpcUa;
        OpcUaPanel.Said += text => {
            Status.Text = text;
            ShowFolderButton.IsVisible = false;
        };

        var text = Editor.TextArea;
        text.AddHandler(PointerPressedEvent, OnEditorPressed, RoutingStrategies.Tunnel);
        text.AddHandler(PointerMovedEvent, OnEditorMoved, RoutingStrategies.Tunnel);
    }

    /// <summary>What is open, or null.</summary>
    public TiaProject? Project => _project;

    /// <summary>Opens a file now, or as soon as the window is up: it may be asked to before that.</summary>
    public void OpenWhenShown(string path) {
        if (IsLoaded) {
            _ = OpenAsync(path);
        } else {
            _pending = path;
        }
    }

    public async Task OpenAsync(string path) {
        Status.Text = "Opening " + Path.GetFileName(path) + "…";
        Busy.IsVisible = true;
        try {
            var project = await Task.Run(() => {
                var opened = TiaProject.Open(path);
                opened.BuildIndex();
                return opened;
            });
            _project = project;
            _current = null;
            _back.Clear();
            _forward.Clear();
            Title = $"{project.Root.Name} - TIA Viewer";
            _paths.Clear();
            RememberPaths(project.Root, "");
            ShowKindSwitches(project.Kinds);
            TreeFilter.Text = "";
            FillTree();
            HeadingBar.IsVisible = false;
            Editor.IsVisible = false;
            OpcUaPanel.IsVisible = false;
            DevicesPanel.IsVisible = false;
            SafetyPanel.IsVisible = false;
            ShowPanels(true);
            Editor.Document = new TextDocument();
            ShowWelcome(project.Root.Name, $"Choose a block on the left.\n{Command} + click a name in the source to follow it.", canOpen: false);
            var closed = project.Items.Count(item => item.IsClosed);
            Status.Text = string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}  ·  {project.Items.Count} items, {closed} closed");
            Hint.Text = $"Read-only  ·  {Command} + click a name to follow it";
            ExportAllButton.IsEnabled = true;
            ShowFolderButton.IsVisible = false;
            ToolTip.SetTip(Status, null);
            CaretPlace.Text = "";
            ShowPlaces(null);
            ResultsList.ItemsSource = null;
            ResultsChip.IsVisible = false;
            UpdateNavigation();
            UpdateMenu();
            RememberOpened(path);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or TiaFormat.TiaFormatException) {
            Status.Text = "Could not open it: " + e.Message;
            // A recent file that is gone or broken would fail again.
            _recent.Remove(path);
            // With nothing else open, this is what there is to see.
            if (_project is null) {
                ShowWelcome("Could not open it", e.Message, canOpen: true);
            }
        } finally {
            Busy.IsVisible = false;
        }
    }

    /// <summary>Shows a place, remembering where it came from.</summary>
    public void Navigate(TiaItem item, int line, bool keepLists = false) {
        if (_current is { } current && (current != item || CurrentLine() != line)) {
            _back.Push(new TiaLocation(current, CurrentLine()));
            _forward.Clear();
        }
        Show(item, line, keepLists);
    }

    /// <summary>
    /// Shows or hides the lists below the source. The OPC UA view refers to nothing and nothing to it, so it takes their
    /// room; the height they had comes back with the next block.
    /// </summary>
    private void ShowPanels(bool show) {
        var row = SourceArea.RowDefinitions[3];
        if (show == Panels.IsVisible) {
            return;
        }
        if (!show) {
            _panelsHeight = row.Height;
        }
        row.Height = show ? _panelsHeight : new GridLength(0);
        Panels.IsVisible = show;
        PanelsLine.IsVisible = show;
        PanelsSplitter.IsVisible = show;
    }

    /// <summary>Opens a data block or tag the OPC UA view names, as following it in the source would.</summary>
    private void OpenFromOpcUa(string name) {
        if (_project is { } project && _current is { } current && project.Resolve(current, name) is { } location) {
            Navigate(location.Item, location.Line);
        }
    }

    /// <summary>Opens a system constant the devices view names, in its PLC's tag table.</summary>
    private void OpenConstant(string plc, string name) {
        if (_project?.Resolve(plc, name) is { } location) {
            Navigate(location.Item, location.Line);
        } else {
            Status.Text = name + " is in no tag table of " + plc + ".";
        }
    }

    /// <summary>Follows the name at a position in the text, if it leads somewhere.</summary>
    public bool FollowAt(int offset) {
        if (_project is not { } project || _current is not { } current) {
            return false;
        }
        var document = project.Document(current);
        if (document.TokenAt(offset) is not { } token) {
            return false;
        }
        if (token.Target is { } target && project.Resolve(current, target) is { } location) {
            Navigate(location.Item, location.Line);
            return true;
        }
        if (token.Kind == TiaTokenKind.Local && document.DeclarationLine(document.Text.Substring(token.Start, token.Length)) is { } line) {
            Navigate(current, line);
            return true;
        }
        return false;
    }

    private void Show(TiaItem item, int line, bool keepLists = false) {
        var project = _project!;
        var document = project.Document(item);
        if (_current != item) {
            _colorizer.Show(document);
            Editor.Document = new TextDocument(document.Text);
            _current = item;
            if (project.OpcUa(item) is { } opcUa) {
                OpcUaPanel.Show(opcUa, () => project.OpcUaNodes(item)!);
            }
            if (project.Hardware(item) is { } hardware) {
                DevicesPanel.Show(hardware);
            }
            if (project.Safety(item) is { } safety) {
                SafetyPanel.Show(safety);
            }
            if (!keepLists) {
                ShowPlaces(item);
            }
        }
        Welcome.IsVisible = false;
        // A PLC's OPC UA configuration and safety program and the project's hardware have views of their own; their text stays in the editor for copying.
        // An HMI entry is its text (spec 0011).
        Editor.IsVisible = !item.IsReport || item.IsHmi;
        OpcUaPanel.IsVisible = item.Kind == TiaItem.OpcUaReportKind;
        SafetyPanel.IsVisible = item.Kind == TiaItem.SafetyReportKind;
        DevicesPanel.IsVisible = item.IsHardware;
        ShowPanels(!item.IsReport);
        ShowHeading(item);
        var clamped = Math.Clamp(line, 0, document.LineCount - 1);
        Editor.CaretOffset = document.LineStart(clamped);
        // Line 0 means the block as such, not a place in it.
        if (clamped == 0) {
            Editor.Select(0, 0);
            Editor.ScrollToHome();
        } else {
            Editor.Select(document.LineStart(clamped), document.LineText(clamped).Length);
            // Once the new text is laid out: only then does the view know its height, and can put the line in the middle.
            Dispatcher.UIThread.Post(() => {
                if (_current == item) {
                    Editor.ScrollToLine(clamped + 1);
                }
            }, DispatcherPriority.Loaded);
        }
        Reveal(item);
        UpdateNavigation();
        UpdateMenu();
    }

    private void ShowPlaces(TiaItem? item) {
        var project = _project;
        var usedBy = item is null || project is null ? [] : project.ReferencesTo(item);
        var uses = item is null || project is null ? [] : project.ReferencesFrom(item);
        UsedByList.ItemsSource = usedBy.OrderBy(reference => reference.Kind).ThenBy(reference => reference.From.Name, StringComparer.OrdinalIgnoreCase).Select(PlaceRow.UsedBy).ToList();
        UsesList.ItemsSource = uses.OrderBy(reference => reference.Kind).ThenBy(reference => reference.To.Name, StringComparer.OrdinalIgnoreCase).Select(PlaceRow.Uses).ToList();
        // These lists stay while walking through them, so they say whose they are.
        UsedByHeader.Text = item is null ? "Used by" : item.Name + " is used by";
        UsesHeader.Text = item is null ? "Uses" : item.Name + " uses";
        UsedByCount.Text = usedBy.Count.ToString(CultureInfo.InvariantCulture);
        UsesCount.Text = uses.Count.ToString(CultureInfo.InvariantCulture);
        var closed = item is null || project is null ? 0 : project.ClosedBlocksAround(item);
        // Spec 0015, FR-009: references found in the text say so.
        var note = project is null || !project.ReferencesFromText ? ""
            : "These come from the text: the archive holds no cross-reference list of TIA's that belongs to it, so some uses may be missed.";
        if (closed != 0) {
            note += string.Create(CultureInfo.InvariantCulture, $" {closed} blocks on this PLC are closed. What they call or use is not known, so this list may be incomplete.");
        }
        ClosedNote.Text = note.Trim();
        ClosedNote.IsVisible = note.Length != 0;
    }

    private void ShowHeading(TiaItem item) {
        HeadingBar.IsVisible = true;
        HeadingBadge.Kind = item.Kind;
        var number = item.Number is { } value ? "  " + value.ToString(CultureInfo.InvariantCulture) : "";
        Heading.Text = item.Name + number;
        LanguageText.Text = item.Language;
        LanguageChip.IsVisible = item.Language is not null;
        ClosedChip.IsVisible = item.IsClosed;
        ExportBlockButton.IsVisible = !item.IsReport;
        ToolTip.SetTip(CopyButton, item.IsReport ? "Copy this report" : "Copy this block's source");
        Breadcrumb.Text = _paths.GetValueOrDefault(item, "");
    }

    private void ShowWelcome(string title, string text, bool canOpen) {
        Welcome.IsVisible = true;
        WelcomeTitle.Text = title;
        WelcomeText.Text = text;
        WelcomeOpen.IsVisible = canOpen;
        WelcomeHint.IsVisible = canOpen;
        RecentPanel.IsVisible = canOpen && _recent.Paths.Count != 0;
    }

    private void ShowCaret() {
        var caret = Editor.TextArea.Caret;
        CaretPlace.Text = string.Create(CultureInfo.InvariantCulture, $"Line {caret.Line}, column {caret.Column}");
    }

    private void RememberPaths(TiaTreeNode node, string path) {
        foreach (var child in node.Children) {
            if (child.Item is { } item) {
                _paths[item] = path;
            } else {
                RememberPaths(child, path.Length == 0 ? child.Name : path + "  ›  " + child.Name);
            }
        }
    }

    private int CurrentLine() => Math.Max(0, Editor.TextArea.Caret.Line - 1);

    private void GoBack() {
        if (_back.TryPop(out var location)) {
            _forward.Push(new TiaLocation(_current!, CurrentLine()));
            Show(location.Item, location.Line);
        }
    }

    private void GoForward() {
        if (_forward.TryPop(out var location)) {
            _back.Push(new TiaLocation(_current!, CurrentLine()));
            Show(location.Item, location.Line);
        }
    }

    private void UpdateNavigation() {
        BackButton.IsEnabled = _back.Count != 0;
        ForwardButton.IsEnabled = _forward.Count != 0;
    }

    /// <summary>One switch per kind of the project, all off (spec 0007, FR-001 and FR-006).</summary>
    private void ShowKindSwitches(IReadOnlyList<string> kinds) {
        KindSwitches.Children.Clear();
        foreach (var kind in kinds) {
            var toggle = new ToggleButton {
                Classes = { "kind" },
                Tag = kind,
                Content = new KindBadge { Kind = kind },
                Margin = new Thickness(0, 0, 2, 0),
            };
            ToolTip.SetTip(toggle, $"Show only {KindName(kind)}; with other kinds on, any of them");
            toggle.IsCheckedChanged += (_, _) => {
                ShowChosenKinds();
                FillTree();
            };
            KindSwitches.Children.Add(toggle);
        }
        KindSwitches.IsVisible = kinds.Count > 1;
        ShowChosenKinds();
    }

    /// <summary>What a kind's items are called in a tip.</summary>
    private static string KindName(string kind) => kind switch {
        "OB" => "organization blocks",
        "FB" => "function blocks",
        "FC" => "functions",
        "DB" => "data blocks",
        "UDT" => "PLC data types",
        "Tags" => "tag tables",
        "Watch" => "watch tables",
        "Force" => "force tables",
        TiaItem.TechnologyObjectKind => "technology objects",
        TiaItem.OpcUaReportKind => "the OPC UA entries",
        TiaItem.SafetyReportKind => "the safety entries",
        TiaItem.HmiKind => "the HMI devices' entries",
        _ => kind + " blocks",
    };

    /// <summary>The kinds whose switch is on: an empty set leaves every kind in.</summary>
    private HashSet<string> ChosenKinds() =>
        [.. KindSwitches.Children.OfType<ToggleButton>().Where(toggle => toggle.IsChecked == true).Select(toggle => (string)toggle.Tag!)];

    /// <summary>While some kinds are chosen, the badges of the others are dimmed: they are left out of the tree.</summary>
    private void ShowChosenKinds() {
        var any = KindSwitches.Children.OfType<ToggleButton>().Any(toggle => toggle.IsChecked == true);
        foreach (var toggle in KindSwitches.Children.OfType<ToggleButton>()) {
            toggle.Opacity = !any || toggle.IsChecked == true ? 1 : 0.45;
        }
    }

    private void FillTree() {
        _roots.Clear();
        _rows.Clear();
        if (_project is not { } project) {
            return;
        }
        var name = TreeFilter.Text?.Trim() ?? "";
        var kinds = ChosenKinds();
        var filtering = name.Length != 0 || kinds.Count != 0;
        foreach (var child in project.Root.Children) {
            if (Row(child, null, name, kinds, filtering) is { } row) {
                row.IsExpanded = filtering || child.Name != TreeRow.LibrariesName;
                _roots.Add(row);
            }
        }
    }

    /// <summary>The node as a row, with only what the filters leave; null if that is nothing.</summary>
    private TreeRow? Row(TiaTreeNode node, TreeRow? parent, string name, HashSet<string> kinds, bool filtering) {
        var row = new TreeRow(node, parent);
        foreach (var child in node.Children) {
            if (Row(child, row, name, kinds, filtering) is { } childRow) {
                row.Children.Add(childRow);
            }
        }
        var matches = node.Item is { } shown ? shown.Matches(name, kinds) : !filtering;
        if (!matches && row.Children.Count == 0) {
            return null;
        }
        row.IsExpanded = filtering && row.Children.Count != 0;
        if (node.Item is { } item) {
            _rows[item] = row;
        }
        return row;
    }

    private void Reveal(TiaItem item) {
        if (!_rows.TryGetValue(item, out var row)) {
            return;
        }
        for (var parent = row.Parent; parent is not null; parent = parent.Parent) {
            parent.IsExpanded = true;
        }
        _selectingFromCode = true;
        Tree.SelectedItem = row;
        _selectingFromCode = false;
    }

    private void OnTreeSelection(object? sender, SelectionChangedEventArgs e) {
        if (!_selectingFromCode && Tree.SelectedItem is TreeRow { Node.Item: { } item } && item != _current) {
            Navigate(item, 0);
        }
    }

    private void OnPlaceSelected(object? sender, SelectionChangedEventArgs e) {
        // Walking through a list of places must not replace the list. A search result leads to a block of its own.
        if (sender is ListBox { SelectedItem: PlaceRow row } list) {
            Navigate(row.Item, row.Line, keepLists: list != ResultsList);
        }
    }

    private void OnSearchKey(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            _ = SearchAsync(SearchBox.Text?.Trim() ?? "");
            e.Handled = true;
        }
    }

    public async Task SearchAsync(string text) {
        if (_project is not { } project || text.Length == 0) {
            return;
        }
        var results = await Task.Run(() => project.Search(text, ResultLimit));
        ResultsList.ItemsSource = results.Select(PlaceRow.Found).ToList();
        ResultsCount.Text = results.Count >= ResultLimit
            ? string.Create(CultureInfo.InvariantCulture, $"first {ResultLimit}")
            : results.Count.ToString(CultureInfo.InvariantCulture);
        ResultsChip.IsVisible = true;
        Panels.SelectedItem = ResultsTab;
    }

    private void OnWindowKey(object? sender, KeyEventArgs e) {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) {
            GoBack();
            e.Handled = true;
        } else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right) {
            GoForward();
            e.Handled = true;
        } else if (RunMenuShortcut(e)) {
            e.Handled = true;
        }
    }

    private void OnEditorPressed(object? sender, PointerPressedEventArgs e) {
        if (TokenColorizer.IsFollowGesture(e.KeyModifiers) && OffsetUnder(e) is { } offset && FollowAt(offset)) {
            e.Handled = true;
        }
    }

    private void OnEditorMoved(object? sender, PointerEventArgs e) {
        TiaToken? hovered = null;
        if (TokenColorizer.IsFollowGesture(e.KeyModifiers) && _project is { } project && _current is { } current && OffsetUnder(e) is { } offset
            && project.Document(current).TokenAt(offset) is { } token && LeadsSomewhere(project, current, token)) {
            hovered = token;
        }
        if (hovered != _colorizer.Hovered) {
            _colorizer.Hovered = hovered;
            Editor.TextArea.Cursor = hovered is null ? null : new Cursor(StandardCursorType.Hand);
            Editor.TextArea.TextView.Redraw();
        }
    }

    private static bool LeadsSomewhere(TiaProject project, TiaItem current, TiaToken token) =>
        (token.Target is { } target && project.Resolve(current, target) is not null) || token.Kind == TiaTokenKind.Local;

    private int? OffsetUnder(PointerEventArgs e) {
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        return position is { } found ? Editor.Document.GetOffset(found.Location) : null;
    }

    /// <summary>Puts a block's text, as shown, on the clipboard.</summary>
    public async Task CopySourceAsync(TiaItem item) {
        if (_project is not { } project || Clipboard is not { } clipboard) {
            return;
        }
        await clipboard.SetTextAsync(project.Document(item).Text);
        Status.Text = "Copied the source of " + item.Name;
        ShowFolderButton.IsVisible = false;
    }

    private async Task PickAndExportAsync(TiaTreeNode? node) {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = "Choose a folder to export into",
            AllowMultiple = false,
        });
        if (folders is [var folder, ..] && folder.TryGetLocalPath() is { } parent) {
            await ExportToAsync(node, parent);
        }
    }

    /// <summary>
    /// Writes a node with everything below it, or with null the whole program, as the command line's dump does.
    /// It goes into a new folder inside the one given, so nothing already there is overwritten. Returns that folder, or null if it failed.
    /// </summary>
    public async Task<string?> ExportToAsync(TiaTreeNode? node, string parent) {
        if (_project is not { } project) {
            return null;
        }
        var target = NewFolder(Path.GetFullPath(parent), TiaSourceExport.Safe(node is null ? project.Root.Name : $"{project.Root.Name} - {node.Name}"));
        HashSet<TiaObjectKey>? chosen = null;
        if (node is not null) {
            chosen = [];
            Collect(node, chosen);
        }
        Busy.IsVisible = true;
        Status.Text = "Exporting…";
        ShowFolderButton.IsVisible = false;
        try {
            var result = await Task.Run(() => new TiaSourceExport(project.Program, target) {
                Blocks = chosen is null ? null : block => chosen.Contains(block.Frame.Key),
                TagTables = chosen is null ? null : table => chosen.Contains(table.Frame.Key),
                WatchTables = chosen is null ? null : table => chosen.Contains(table.Frame.Key),
                TechnologyObjects = chosen is null ? null : technologyObject => chosen.Contains(technologyObject.Frame.Key),
            }.Run());
            // The status bar has room for the gist; the tip has the rest.
            var text = string.Create(CultureInfo.InvariantCulture, $"Exported {result.BlockFiles + result.TagFiles + result.WatchFiles + result.TechnologyObjectFiles} files to \"{Path.GetFileName(target)}\"");
            if (result.Failures.Count != 0) {
                text += string.Create(CultureInfo.InvariantCulture, $", {result.Failures.Count} blocks failed");
            }
            var details = new List<string> { target };
            if (result.Empty != 0) {
                details.Add(string.Create(CultureInfo.InvariantCulture, $"{result.Empty} closed blocks have nothing stored, so index.tsv only lists them."));
            }
            details.AddRange(result.Failures);
            Status.Text = text;
            ToolTip.SetTip(Status, string.Join('\n', details));
            _exported = target;
            ShowFolderButton.IsVisible = true;
            return target;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            Status.Text = "Could not export: " + e.Message;
            return null;
        } finally {
            Busy.IsVisible = false;
        }
    }

    private static void Collect(TiaTreeNode node, HashSet<TiaObjectKey> chosen) {
        if (node.Item?.Block is { } block) {
            chosen.Add(block.Frame.Key);
        }
        if (node.Item?.Table is { } table) {
            chosen.Add(table.Frame.Key);
        }
        if (node.Item?.WatchTable is { } watch) {
            chosen.Add(watch.Frame.Key);
        }
        if (node.Item?.TechnologyObject is { } technologyObject) {
            chosen.Add(technologyObject.Frame.Key);
        }
        foreach (var child in node.Children) {
            Collect(child, chosen);
        }
    }

    private static string NewFolder(string parent, string name) {
        var path = Path.Combine(parent, name);
        for (var number = 2; Directory.Exists(path) || File.Exists(path); number++) {
            path = Path.Combine(parent, string.Create(CultureInfo.InvariantCulture, $"{name} {number}"));
        }
        return path;
    }

    private void OnExportRow(object? sender, RoutedEventArgs e) {
        if (sender is MenuItem { DataContext: TreeRow row }) {
            _ = PickAndExportAsync(row.Node);
        }
    }

    private void OnCopyRow(object? sender, RoutedEventArgs e) {
        if (sender is MenuItem { DataContext: TreeRow { Node.Item: { } item } }) {
            _ = CopySourceAsync(item);
        }
    }

    private async Task PickAndOpenAsync() {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open a TIA Portal archive or project",
            AllowMultiple = false,
            FileTypeFilter = [TiaFileTypes.PickerType(wildcards: !OperatingSystem.IsMacOS()), FilePickerFileTypes.All],
        });
        if (files is [var file, ..] && file.TryGetLocalPath() is { } path) {
            await OpenAsync(path);
        }
    }

    private void OnDrop(object? sender, DragEventArgs e) {
        if (e.DataTransfer.TryGetFiles() is [var file, ..] && file.TryGetLocalPath() is { } path) {
            _ = OpenAsync(path);
        }
    }

    private void ApplyTheme() {
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        _colorizer.IsDark = isDark;
        // The selection has to leave the colours of the text readable: it is how a followed line is marked.
        Editor.TextArea.SelectionBrush = isDark ? DarkSelection : LightSelection;
        Editor.TextArea.TextView.CurrentLineBackground = isDark ? DarkCurrentLine : LightCurrentLine;
        Editor.LineNumbersForeground = isDark ? DarkLineNumbers : LightLineNumbers;
        Editor.TextArea.TextView.Redraw();
    }
}
