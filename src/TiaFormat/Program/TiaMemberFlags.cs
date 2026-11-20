using System.Text;

namespace TiaFormat.Program;

/// <summary>
/// What a member's <c>MFlags</c> say about its attributes. Read off TIA's own source of one project, so a table of the
/// values seen rather than a meaning for each bit; see the format page.
/// </summary>
internal static class TiaMemberFlags {
    private const int SetPointShown = 512;
    private const int SetPointValue = 1;

    /// <summary>Whether the member is closed to outside access: stated by its flags, or else as its parent is.</summary>
    public static bool IsHidden(int? flags, bool hiddenFromParent) => flags switch {
        14 or 526 or 76 or 588 or 77 => true,
        _ => hiddenFromParent,
    };

    /// <summary>Whether the member is open to outside access but not writable: <c>ExternalWritable := 'False'</c>.</summary>
    public static bool IsReadOnly(int? flags) => flags == 79;

    /// <summary>The attributes in braces, or null. A set point is a value to keep, so a parameter and a plain struct show none.</summary>
    public static string? Attributes(int? flags, bool hidden, string section, string type, string? instructionVersion) {
        // An instruction's own attributes come first, and only they start right after the brace.
        var text = new StringBuilder();
        if (instructionVersion is not null) {
            text.Append("InstructionName := '").Append(ElementType(type)).Append("'; LibVersion := '").Append(instructionVersion).Append('\'');
        }
        if (IsReadOnly(flags)) {
            Separate(text);
            text.Append("ExternalWritable := 'False'");
        } else if (hidden) {
            Separate(text);
            text.Append("ExternalAccessible := 'False'; ExternalVisible := 'False'; ExternalWritable := 'False'");
        }
        if (flags is { } shown && (shown & SetPointShown) != 0 && section is not ("Input" or "Output" or "InOut") && !type.Equals("Struct", StringComparison.OrdinalIgnoreCase)) {
            Separate(text);
            text.Append("S7_SetPoint := '").Append((shown & SetPointValue) != 0 && instructionVersion is null && IsElementary(type) ? "True" : "False").Append('\'');
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
