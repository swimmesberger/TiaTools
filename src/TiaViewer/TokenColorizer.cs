using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>Colours the text from the document's tokens: what each piece is comes from the stored source, not from a grammar.</summary>
internal sealed class TokenColorizer : DocumentColorizingTransformer {
    private static readonly Dictionary<TiaTokenKind, IBrush> Light = Palette("#0033B3", "#6B7A6B", "#067D17", "#00627A", "#B0B0B0", "#1F1F1F", "#871094", "#3D3D3D", "#7A4A00", "#00627A", "#7A1FA2");
    private static readonly Dictionary<TiaTokenKind, IBrush> Dark = Palette("#569CD6", "#7F9F7F", "#CE9178", "#4EC9B0", "#5F5F5F", "#D4D4D4", "#D7A0E8", "#C8C8C8", "#E0B080", "#4FC1FF", "#DCDCAA");

    private TiaDocument? _document;

    public bool IsDark { get; set; }

    /// <summary>The token under the pointer while a click on it would follow it.</summary>
    public TiaToken? Hovered { get; set; }

    public void Show(TiaDocument document) {
        _document = document;
        Hovered = null;
    }

    protected override void ColorizeLine(DocumentLine line) {
        if (_document is not { } document) {
            return;
        }
        var palette = IsDark ? Dark : Light;
        var tokens = document.Tokens;
        var end = line.Offset + line.Length;
        for (var i = FirstAtOrAfter(tokens, line.Offset); i < tokens.Count && tokens[i].Start < end; i++) {
            var token = tokens[i];
            var brush = palette[token.Kind];
            var isHovered = Hovered is { } hovered && hovered.Start == token.Start;
            ChangeLinePart(Math.Max(token.Start, line.Offset), Math.Min(token.Start + token.Length, end), element => {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (isHovered) {
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                }
            });
        }
    }

    /// <summary>The first token that reaches into the position: a block comment may start on an earlier line.</summary>
    private static int FirstAtOrAfter(IReadOnlyList<TiaToken> tokens, int position) {
        var low = 0;
        var high = tokens.Count;
        while (low < high) {
            var middle = (low + high) / 2;
            if (tokens[middle].Start + tokens[middle].Length <= position) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }
        return low;
    }

    private static Dictionary<TiaTokenKind, IBrush> Palette(string keyword, string comment, string literal, string type, string attribute, string local, string global, string member, string parameter, string call, string instruction) => new() {
        [TiaTokenKind.Keyword] = Brush.Parse(keyword),
        [TiaTokenKind.Comment] = Brush.Parse(comment),
        [TiaTokenKind.Literal] = Brush.Parse(literal),
        [TiaTokenKind.Type] = Brush.Parse(type),
        [TiaTokenKind.Attribute] = Brush.Parse(attribute),
        [TiaTokenKind.Local] = Brush.Parse(local),
        [TiaTokenKind.Global] = Brush.Parse(global),
        [TiaTokenKind.Member] = Brush.Parse(member),
        [TiaTokenKind.Parameter] = Brush.Parse(parameter),
        [TiaTokenKind.Call] = Brush.Parse(call),
        [TiaTokenKind.Instruction] = Brush.Parse(instruction),
    };

    internal static bool IsFollowGesture(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
}
