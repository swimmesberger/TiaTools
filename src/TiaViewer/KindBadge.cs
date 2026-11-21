using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TiaViewer;

/// <summary>A block's kind as a small coloured label, the way the names FB, FC or DB are known to whoever reads PLC code.</summary>
public sealed class KindBadge : Border {
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<KindBadge, string?>(nameof(Kind));

    private readonly TextBlock _label = new() {
        FontSize = 9,
        FontWeight = FontWeight.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public KindBadge() {
        Width = 26;
        Height = 15;
        CornerRadius = new CornerRadius(3);
        VerticalAlignment = VerticalAlignment.Center;
        Child = _label;
    }

    public string? Kind {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) {
            _label.Text = Kind switch {
                "Tags" => "TAG",
                TiaFormat.Program.TiaItem.OpcUaReportKind => "OPC",
                TiaFormat.Program.TiaItem.SafetyReportKind => "F",
                _ => Kind,
            };
            Background = Glyphs.KindBrush(Kind);
        }
    }
}
