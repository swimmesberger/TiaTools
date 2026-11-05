namespace TiaFormat.Program;

/// <summary>How a source file writes a member's name: plain if it can be, in quotes if it is not an identifier or is reserved.</summary>
public static class TiaIdentifiers {
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) {
        // Data types.
        "Any", "Array", "Bool", "Byte", "Char", "Counter", "Date", "Date_And_Time", "DInt", "DT", "DWord", "Int",
        "LInt", "LReal", "LWord", "Pointer", "Real", "S5Time", "SInt", "String", "Struct",
        "Time", "Time_Of_Day", "Timer", "TOD", "UDInt", "UInt", "ULInt", "USInt", "Variant", "Void", "WChar", "Word", "WString",
        // Words of a source file.
        "Author", "Family", "Name", "Title", "Type", "Version",
        "And", "Begin", "By", "Case", "Const", "Constant", "Continue", "Div", "Do", "Else", "Elsif", "End_Case", "End_For", "End_If",
        "End_Repeat", "End_Region", "End_Struct", "End_Type", "End_Var", "End_While", "Exit", "False", "For", "Function", "Function_Block",
        "Goto", "If", "Mod", "Not", "Of", "Or", "Region", "Repeat", "Retain", "Return", "Then", "To", "True", "Until", "Var", "Var_In_Out",
        "Var_Input", "Var_Output", "Var_Temp", "While", "Xor",
    };

    /// <summary>The name as a source file writes it.</summary>
    public static string Quoted(string name) => IsPlain(name) ? name : '"' + name + '"';

    private static bool IsPlain(string name) {
        if (name.Length == 0 || char.IsDigit(name[0]) || Reserved.Contains(name)) {
            return false;
        }
        foreach (var c in name) {
            if (!char.IsLetterOrDigit(c) && c != '_') {
                return false;
            }
        }
        return true;
    }
}
