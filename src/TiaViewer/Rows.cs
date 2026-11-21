using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>One line of the tree.</summary>
public sealed class TreeRow(TiaTreeNode node, TreeRow? parent) : INotifyPropertyChanged {
    /// <summary>The tree's last root, which is no PLC.</summary>
    public const string LibrariesName = "Libraries and system";

    private static readonly IBrush FolderFill = Brush.Parse("#E9B44C");
    private static readonly IBrush FolderLine = Brush.Parse("#C8922A");
    private static readonly IBrush PlcLine = Brush.Parse("#3B7BF0");
    private static readonly IBrush LibraryLine = Brush.Parse("#6B7280");

    /// <summary>The colour of the program cycle mark, a start button's green, readable in light and dark.</summary>
    public static IBrush StartFill { get; } = Brush.Parse("#16A34A");

    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TiaTreeNode Node { get; } = node;

    public TreeRow? Parent { get; } = parent;

    public string Name => Node.Name;

    public string Kind => Node.Item?.Kind ?? "";

    public bool IsItem => Node.Item is not null;

    public bool IsHolder => Node.Item is null;

    /// <summary>
    /// A PLC's OPC UA report or an HMI entry is shown like a block, but not exported; nor is an HMI device or folder,
    /// which holds nothing else.
    /// </summary>
    public bool CanExport => Node.Item is not { IsReport: true } && !(IsHolder && ItemsBelow() == 0 && HmiBelow() != 0);

    public string CopyLabel => Node.Item is { IsReport: true } ? "Copy report" : "Copy source";

    public bool IsClosed => Node.Item?.IsClosed ?? false;

    /// <summary>An OB where the program's cycle starts (spec 0007).</summary>
    public bool IsProgramCycle => Node.Item?.IsProgramCycle ?? false;

    /// <summary>A closed block is there, but there is less to see in it.</summary>
    public double NameOpacity => IsClosed ? 0.6 : 1;

    public string Number => Node.Item?.Number is { } number ? number.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// How many blocks and tag tables are below, with what the filters leave; a report is neither. Below an HMI device,
    /// how many of its entries.
    /// </summary>
    public string Count => !IsHolder ? "" : (ItemsBelow() is var items and not 0 ? items : HmiBelow()).ToString(CultureInfo.InvariantCulture);

    public Geometry Glyph => Parent is not null ? Glyphs.Folder : Name == LibrariesName ? Glyphs.Library : Glyphs.Plc;

    public IBrush? GlyphFill => Parent is not null ? FolderFill : null;

    public IBrush GlyphLine => Parent is not null ? FolderLine : Name == LibrariesName ? LibraryLine : PlcLine;

    public string ExportLabel => IsItem ? Kind switch {
            "Tags" => "Export this tag table…",
            "Watch" => "Export this watch table…",
            "Force" => "Export this force table…",
            TiaFormat.Program.TiaItem.TechnologyObjectKind => "Export this technology object…",
            _ => "Export this block…",
        }
        : Parent is null && Name != LibrariesName ? "Export this PLC…" : "Export this folder…";

    public ObservableCollection<TreeRow> Children { get; } = [];

    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (_isExpanded != value) {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    private int ItemsBelow() => Children.Sum(child => child.IsItem ? (child.CanExport ? 1 : 0) : child.ItemsBelow());

    private int HmiBelow() => Children.Sum(child => child.IsItem ? (child.Node.Item!.IsHmi ? 1 : 0) : child.HmiBelow());
}

/// <summary>One line of a list that leads to a place in the source.</summary>
public sealed class PlaceRow(TiaItem item, int line, TiaItem named, string name, string verb, string text) {
    /// <summary>The block the line is in.</summary>
    public TiaItem Item { get; } = item;

    public int Line { get; } = line;

    public string Kind { get; } = named.Kind;

    /// <summary>The item the row is about, the one who uses or the one used, with the tag or member it is about.</summary>
    public string Name { get; } = name;

    public string At { get; } = (line + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>How it is used: the access TIA's cross-reference list gives, or call or use from the text.</summary>
    public string Verb { get; } = verb;

    public bool HasVerb => Verb.Length != 0;

    public bool IsCall => Verb == "call";

    public string Text { get; } = text;

    /// <summary>A line elsewhere that refers to the item on show; on a tag table, it names the tag.</summary>
    public static PlaceRow UsedBy(TiaReference reference) =>
        new(reference.From, reference.Line, reference.From, reference.Target is { } target ? reference.From.Name + "  → " + target : reference.From.Name, VerbOf(reference), reference.Text);

    /// <summary>A line of the item on show that refers to another: it names the other, and leads to the line.</summary>
    public static PlaceRow Uses(TiaReference reference) =>
        new(reference.From, reference.Line, reference.To, TiaCrossReferenceWriter.Used(reference), VerbOf(reference), reference.Text);

    public static PlaceRow Found(TiaSearchResult result) => new(result.Item, result.Line, result.Item, result.Item.Name, "", result.Text);

    /// <summary>TIA's access in a word: read, write, r/w, call, inst, type, watch, modify, force.</summary>
    private static string VerbOf(TiaReference reference) => reference.Access switch {
        null => reference.Kind == TiaReferenceKind.Call ? "call" : "use",
        "RW" => "r/w",
        "InstanceDB" or "Multiinstance" => "inst",
        "Declaration" => "type",
        "Monitor" => "watch",
        var access => access.ToLowerInvariant(),
    };
}

/// <summary>One recently opened file on the start screen: its name, and below it the folder it is in.</summary>
public sealed class RecentRow(string path) {
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));

    /// <summary>The folder it is in, with the home folder shortened to <c>~</c> where that is the custom.</summary>
    public string Folder {
        get {
            var folder = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(Path)) ?? "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!OperatingSystem.IsWindows() && home.Length != 0 && (folder == home || folder.StartsWith(home + "/", StringComparison.Ordinal))) {
                return "~" + folder[home.Length..];
            }
            return folder;
        }
    }
}
