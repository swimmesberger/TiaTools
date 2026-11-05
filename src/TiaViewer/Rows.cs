using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>One line of the tree.</summary>
public sealed class TreeRow(TiaTreeNode node, TreeRow? parent) : INotifyPropertyChanged {
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TiaTreeNode Node { get; } = node;

    public TreeRow? Parent { get; } = parent;

    public string Name => Node.Name;

    public string Kind => Node.Item?.Kind ?? "";

    public bool IsClosed => Node.Item?.IsClosed ?? false;

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
}

/// <summary>One line of a list that leads to a place in the source.</summary>
public sealed class PlaceRow(TiaItem item, int line, string place, string text) {
    public TiaItem Item { get; } = item;

    public int Line { get; } = line;

    public string Place { get; } = place;

    public string Text { get; } = text;

    /// <summary>A line elsewhere that refers to the block on show.</summary>
    public static PlaceRow UsedBy(TiaReference reference) => new(reference.From, reference.Line, Describe(reference.Kind, reference.From, reference.Line), reference.Text);

    /// <summary>A line of the block on show that refers to another: it names the other, and leads to the line.</summary>
    public static PlaceRow Uses(TiaReference reference) => new(reference.From, reference.Line, Describe(reference.Kind, reference.To, reference.Line), reference.Text);

    public static PlaceRow Found(TiaSearchResult result) => new(result.Item, result.Line, $"{result.Item.Kind} {result.Item.Name}:{(result.Line + 1).ToString(CultureInfo.InvariantCulture)}", result.Text);

    private static string Describe(TiaReferenceKind kind, TiaItem item, int line) {
        var verb = kind == TiaReferenceKind.Call ? "call" : "use";
        return $"{verb}  {item.Kind} {item.Name}:{(line + 1).ToString(CultureInfo.InvariantCulture)}";
    }
}
