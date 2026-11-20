using System.Text;

namespace TiaFormat.Program;

/// <summary>
/// What a member's <c>MFlags</c> say about its attributes: bits read off the source TIA Portal V21 writes for members
/// given each combination, and checked against TIA's source of a whole project; see the format page.
/// </summary>
internal static class TiaMemberFlags {
    // Both are set whenever the access bits below are stored; a value without them says nothing about access.
    private const int AccessStored = 4 | 8;
    private const int Accessible = 1;
    private const int Visible = 2;
    private const int NotWritable = 64;
    private const int SetPoint = 512;

    /// <summary>Whether the member is closed to outside access: stated by its flags, or else as its parent is.</summary>
    public static bool IsHidden(int? flags, bool hiddenFromParent) =>
        hiddenFromParent || StoresAccess(flags, out var stored) && (stored & Accessible) == 0;

    /// <summary>Whether the member is open to outside access but not writable: <c>ExternalWritable := 'False'</c>.</summary>
    public static bool IsReadOnly(int? flags) =>
        StoresAccess(flags, out var stored) && (stored & Accessible) != 0 && (stored & NotWritable) != 0;

    /// <summary>Whether the member is open to outside access but not visible in HMI engineering: <c>ExternalVisible := 'False'</c>.</summary>
    public static bool IsNotVisible(int? flags) =>
        StoresAccess(flags, out var stored) && (stored & Accessible) != 0 && (stored & Visible) == 0;

    private static bool StoresAccess(int? flags, out int stored) {
        stored = flags ?? 0;
        return (stored & AccessStored) == AccessStored;
    }

    /// <summary>The attributes in braces, or null. A set point is a value to keep, so a parameter and a plain struct show none.</summary>
    public static string? Attributes(int? flags, bool hidden, string section, string type, string? instructionVersion) {
        // An instruction's own attributes come first, and only they start right after the brace.
        var text = new StringBuilder();
        if (instructionVersion is not null) {
            text.Append("InstructionName := '").Append(ElementType(type)).Append("'; LibVersion := '").Append(instructionVersion).Append('\'');
        }
        if (hidden) {
            Separate(text);
            text.Append("ExternalAccessible := 'False'; ExternalVisible := 'False'; ExternalWritable := 'False'");
        } else {
            if (IsNotVisible(flags)) {
                Separate(text);
                text.Append("ExternalVisible := 'False'");
            }
            if (IsReadOnly(flags)) {
                Separate(text);
                text.Append("ExternalWritable := 'False'");
            }
        }
        if (flags is { } shown && (shown & SetPoint) != 0 && section is not ("Input" or "Output" or "InOut") && !type.Equals("Struct", StringComparison.OrdinalIgnoreCase)) {
            Separate(text);
            text.Append("S7_SetPoint := '").Append(instructionVersion is null && IsElementary(type) ? "True" : "False").Append('\'');
        }
        return text.Length == 0 ? null : "{" + text + "}";
    }

    private static void Separate(StringBuilder text) => text.Append(text.Length == 0 ? " " : "; ");

    private static string ElementType(string type) {
        var of = type.LastIndexOf(" of ", StringComparison.Ordinal);
        return of < 0 ? type : type[(of + 4)..];
    }

    private static bool IsElementary(string type) {
        var element = ElementType(type);
        return !element.StartsWith('"') && !element.Equals("Struct", StringComparison.OrdinalIgnoreCase);
    }
}
