using Avalonia.Media;

namespace TiaViewer;

/// <summary>The icons, drawn here on a 16 pixel grid: no font to ship and no licence to carry.</summary>
public static class Glyphs {
    public static Geometry Back { get; } = Geometry.Parse("M13 8H3M7 4L3 8L7 12");

    public static Geometry Forward { get; } = Geometry.Parse("M3 8H13M9 4L13 8L9 12");

    public static Geometry Open { get; } = Geometry.Parse("M2 12.5V3.5C2 3 2.4 2.5 3 2.5H6L7.5 4.5H12C12.6 4.5 13 5 13 5.5V7M2 12.5L3.8 7.6C3.9 7.2 4.3 7 4.7 7H14C14.4 7 14.7 7.4 14.6 7.8L13.2 12C13 12.3 12.7 12.5 12.4 12.5H2Z");

    public static Geometry Search { get; } = Geometry.Parse("M7 2.5A4.5 4.5 0 1 0 7 11.5A4.5 4.5 0 1 0 7 2.5ZM10.4 10.4L13.5 13.5");

    public static Geometry Filter { get; } = Geometry.Parse("M2 3H14L9.5 8.5V13L6.5 11.5V8.5Z");

    public static Geometry Lock { get; } = Geometry.Parse("M4 7.5H12C12.3 7.5 12.5 7.7 12.5 8V13C12.5 13.3 12.3 13.5 12 13.5H4C3.7 13.5 3.5 13.3 3.5 13V8C3.5 7.7 3.7 7.5 4 7.5ZM5.5 7.5V5A2.5 2.5 0 0 1 10.5 5V7.5");

    public static Geometry Folder { get; } = Geometry.Parse("M1.5 12V4C1.5 3.2 2.1 2.5 3 2.5H6L7.5 4.5H13C13.8 4.5 14.5 5.2 14.5 6V12C14.5 12.8 13.8 13.5 13 13.5H3C2.1 13.5 1.5 12.8 1.5 12Z");

    public static Geometry Plc { get; } = Geometry.Parse("M4.5 3H11.5C12.3 3 13 3.7 13 4.5V11.5C13 12.3 12.3 13 11.5 13H4.5C3.7 13 3 12.3 3 11.5V4.5C3 3.7 3.7 3 4.5 3ZM6 6H10V10H6ZM6 1V3M10 1V3M6 13V15M10 13V15M1 6H3M1 10H3M13 6H15M13 10H15");

    public static Geometry Library { get; } = Geometry.Parse("M3 2.5V13.5M6 2.5V13.5M9 3L12.5 13.2");

    public static Geometry Copy { get; } = Geometry.Parse("M6.5 5.5H12.5C13 5.5 13.5 6 13.5 6.5V13C13.5 13.5 13 14 12.5 14H6.5C6 14 5.5 13.5 5.5 13V6.5C5.5 6 6 5.5 6.5 5.5ZM10.5 5.5V3.5C10.5 3 10 2.5 9.5 2.5H3.5C3 2.5 2.5 3 2.5 3.5V10C2.5 10.5 3 11 3.5 11H5.5");

    public static Geometry Export { get; } = Geometry.Parse("M8 10V2.5M5 5.5L8 2.5L11 5.5M2.5 9.5V12.5C2.5 13.1 2.9 13.5 3.5 13.5H12.5C13.1 13.5 13.5 13.1 13.5 12.5V9.5");

    /// <summary>Where the program's cycle starts: a filled triangle, as a start button has.</summary>
    public static Geometry Start { get; } = Geometry.Parse("M4.5 2.8L13 8L4.5 13.2Z");

    public static Geometry UsedBy { get; } = Geometry.Parse("M14 8H5M8.5 4.5L5 8L8.5 11.5M2 3V13");

    public static Geometry Uses { get; } = Geometry.Parse("M2 8H11M7.5 4.5L11 8L7.5 11.5M14 3V13");

    /// <summary>The colour a kind of block has, the same in light and dark. It carries white text.</summary>
    public static IBrush KindBrush(string? kind) => kind switch {
        "OB" => Ob,
        "FB" or "SFB" => Fb,
        "FC" or "SFC" => Fc,
        "UDT" => Udt,
        "Tags" => Tags,
        "Watch" or "Force" => Watch,
        TiaFormat.Program.TiaItem.TechnologyObjectKind => TechnologyObject,
        TiaFormat.Program.TiaItem.OpcUaReportKind => OpcUa,
        TiaFormat.Program.TiaItem.HardwareReportKind => Hardware,
        TiaFormat.Program.TiaItem.SafetyReportKind => Safety,
        _ when kind?.StartsWith("DB", StringComparison.Ordinal) == true => Db,
        _ => Other,
    };

    private static readonly IBrush Ob = Brush.Parse("#7C3AED");

    /// <summary>Safety yellow is too light for white text; this amber carries it.</summary>
    private static readonly IBrush Safety = Brush.Parse("#B45309");
    private static readonly IBrush Fb = Brush.Parse("#2563EB");
    private static readonly IBrush Fc = Brush.Parse("#0B8A7D");
    private static readonly IBrush Db = Brush.Parse("#B45F06");
    private static readonly IBrush Udt = Brush.Parse("#C2307A");
    private static readonly IBrush Tags = Brush.Parse("#3F8B2E");
    private static readonly IBrush OpcUa = Brush.Parse("#0E7490");
    private static readonly IBrush Hardware = Brush.Parse("#475569");
    private static readonly IBrush Watch = Brush.Parse("#A16207");
    private static readonly IBrush TechnologyObject = Brush.Parse("#B91C1C");
    private static readonly IBrush Other = Brush.Parse("#6B7280");
}
