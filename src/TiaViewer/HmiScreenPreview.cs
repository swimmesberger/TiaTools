using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TiaFormat.Program;

namespace TiaViewer;

/// <summary>
/// A schematic preview of a screen (spec 0017, FR-008): a box per screen item at its stored position and size, with its
/// name, type, text and HMI tags, scaled to fit. Not how the runtime draws it: no styles, no runtime values. A click on a
/// box tells which item it is.
/// </summary>
public sealed class HmiScreenPreview : Viewbox {
    public HmiScreenPreview(TiaHmiScreen screen, Action<TiaHmiScreenItem> clicked) {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        var width = screen.Width ?? Math.Max(320, screen.Items.Select(item => (item.Left ?? 0) + (item.Width ?? 0)).DefaultIfEmpty(0).Max());
        var height = screen.Height ?? Math.Max(200, screen.Items.Select(item => (item.Top ?? 0) + (item.Height ?? 0)).DefaultIfEmpty(0).Max());
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        var frame = new Border { Width = width, Height = height, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Viewer.Line"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Viewer.Panel"));
        canvas.Children.Add(frame);
        foreach (var item in screen.Items) {
            var box = new Border {
                Width = Math.Max(4, item.Width ?? 40), Height = Math.Max(4, item.Height ?? 20), BorderThickness = new Thickness(1),
                BorderBrush = Brush.Parse("#7C8DA6"), Background = new SolidColorBrush(Color.Parse("#337C8DA6")), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock {
                    Text = Label(item), FontSize = 11, Margin = new Thickness(3, 1), TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top,
                },
            };
            ToolTip.SetTip(box, item.Name + "  ·  " + item.Type + (item.Text is { Length: > 0 } text ? "\n" + text : "") + (item.Tags.Count == 0 ? "" : "\nHMI tags: " + string.Join(", ", item.Tags)));
            Canvas.SetLeft(box, item.Left ?? 0);
            Canvas.SetTop(box, item.Top ?? 0);
            box.PointerPressed += (_, e) => {
                clicked(item);
                e.Handled = true;
            };
            canvas.Children.Add(box);
        }
        Child = canvas;
    }

    /// <summary>What a box says: the item's text or its tags, else its name, then its type.</summary>
    private static string Label(TiaHmiScreenItem item) {
        var shown = item.Text is { Length: > 0 } text ? "“" + text + "”" : item.Tags.Count != 0 ? string.Join(", ", item.Tags) : item.Name;
        return shown + "\n" + (item.Type.StartsWith("Hmi", StringComparison.Ordinal) ? item.Type[3..] : item.Type);
    }
}
