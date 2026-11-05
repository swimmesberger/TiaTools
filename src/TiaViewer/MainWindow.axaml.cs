using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Search;
using TiaFormat.Program;

namespace TiaViewer;

public sealed partial class MainWindow : Window {
    private const int ResultLimit = 2000;

    private readonly TokenColorizer _colorizer = new();
    private readonly Stack<TiaLocation> _back = new();
    private readonly Stack<TiaLocation> _forward = new();
    private readonly Dictionary<TiaItem, TreeRow> _rows = [];
    private readonly ObservableCollection<TreeRow> _roots = [];
    private TiaProject? _project;
    private TiaItem? _current;
    private bool _selectingFromCode;
    private string? _pending;

    public MainWindow() {
        InitializeComponent();
        Tree.ItemsSource = _roots;
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        SearchPanel.Install(Editor);

        OpenButton.Click += async (_, _) => await PickAndOpenAsync();
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
        Status.Text = "Opening…";
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
            TreeFilter.Text = "";
            FillTree();
            Heading.Text = "";
            Welcome.IsVisible = true;
            Welcome.Text = "Choose a block on the left.\nCtrl or Cmd + click a name in the source to follow it.";
            Editor.Document = new TextDocument();
            var closed = project.Items.Count(item => item.IsClosed);
            Status.Text = string.Create(CultureInfo.InvariantCulture, $"{project.Items.Count} items, {closed} closed");
            ShowPlaces(null);
            ResultsList.ItemsSource = null;
            UpdateNavigation();
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or TiaFormat.TiaFormatException) {
            Status.Text = "Could not open it: " + e.Message;
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
            if (!keepLists) {
                ShowPlaces(item);
            }
        }
        Welcome.IsVisible = false;
        Heading.Text = HeadingOf(item);
        var clamped = Math.Clamp(line, 0, document.LineCount - 1);
        Editor.ScrollToLine(clamped + 1);
        // Line 0 means the block as such, not a place in it.
        if (clamped == 0) {
            Editor.Select(0, 0);
            Editor.ScrollToHome();
        } else {
            Editor.Select(document.LineStart(clamped), document.LineText(clamped).Length);
        }
        Editor.CaretOffset = document.LineStart(clamped);
        Reveal(item);
        UpdateNavigation();
    }

    private void ShowPlaces(TiaItem? item) {
        var project = _project;
        var usedBy = item is null || project is null ? [] : project.ReferencesTo(item);
        var uses = item is null || project is null ? [] : project.ReferencesFrom(item);
        UsedByList.ItemsSource = usedBy.OrderBy(reference => reference.Kind).ThenBy(reference => reference.From.Name, StringComparer.OrdinalIgnoreCase).Select(PlaceRow.UsedBy).ToList();
        UsesList.ItemsSource = uses.OrderBy(reference => reference.Kind).ThenBy(reference => reference.To.Name, StringComparer.OrdinalIgnoreCase).Select(PlaceRow.Uses).ToList();
        // A header treats an underscore as an access key, and block names are full of them.
        var name = item is null ? "" : item.Name.Replace("_", "__", StringComparison.Ordinal) + " ";
        UsedByTab.Header = string.Create(CultureInfo.InvariantCulture, $"{name}is used by ({usedBy.Count})");
        UsesTab.Header = string.Create(CultureInfo.InvariantCulture, $"{name}uses ({uses.Count})");
        var closed = item is null || project is null ? 0 : project.ClosedBlocksAround(item);
        ClosedNote.Text = closed == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"{closed} blocks on this PLC are closed. What they call or use is not known, so this list may be incomplete.");
        ClosedNote.IsVisible = closed != 0;
    }

    private static string HeadingOf(TiaItem item) {
        var number = item.Number is { } value ? " " + value.ToString(CultureInfo.InvariantCulture) : "";
        var language = item.Language is null ? "" : "  ·  " + item.Language;
        return $"{item.Kind}{number}  {item.Name}{language}{(item.IsClosed ? "  ·  closed" : "")}";
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

    private void FillTree() {
        _roots.Clear();
        _rows.Clear();
        if (_project is not { } project) {
            return;
        }
        var filter = TreeFilter.Text?.Trim() ?? "";
        foreach (var child in project.Root.Children) {
            if (Row(child, null, filter) is { } row) {
                row.IsExpanded = filter.Length != 0 || child.Name != "Libraries and system";
                _roots.Add(row);
            }
        }
    }

    /// <summary>The node as a row, with only what the filter leaves; null if that is nothing.</summary>
    private TreeRow? Row(TiaTreeNode node, TreeRow? parent, string filter) {
        var row = new TreeRow(node, parent);
        foreach (var child in node.Children) {
            if (Row(child, row, filter) is { } childRow) {
                row.Children.Add(childRow);
            }
        }
        var matches = filter.Length == 0 || (node.Item is not null && node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (!matches && row.Children.Count == 0) {
            return null;
        }
        row.IsExpanded = filter.Length != 0 && row.Children.Count != 0;
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
        ResultsTab.Header = results.Count >= ResultLimit
            ? string.Create(CultureInfo.InvariantCulture, $"Search results (first {ResultLimit})")
            : string.Create(CultureInfo.InvariantCulture, $"Search results ({results.Count})");
        Panels.SelectedItem = ResultsTab;
    }

    private void OnWindowKey(object? sender, KeyEventArgs e) {
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Left) {
            GoBack();
            e.Handled = true;
        } else if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Right) {
            GoForward();
            e.Handled = true;
        } else if (TokenColorizer.IsFollowGesture(e.KeyModifiers) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.F) {
            SearchBox.Focus();
            SearchBox.SelectAll();
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

    private async Task PickAndOpenAsync() {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open a TIA Portal archive or project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("TIA Portal archive or project") { Patterns = ["*.zap*", "*.ap*", "*.al*", "PEData.plf"] }, FilePickerFileTypes.All],
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
        _colorizer.IsDark = ActualThemeVariant == ThemeVariant.Dark;
        Editor.TextArea.TextView.Redraw();
    }
}
