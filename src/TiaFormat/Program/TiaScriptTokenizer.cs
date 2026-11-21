namespace TiaFormat.Program;

/// <summary>
/// The tokens of an HMI device's code for showing it (spec 0017): keywords, comments, strings and numbers of JavaScript
/// and VBScript. Enough to colour the text; it does not parse, and reading never runs any of it.
/// </summary>
public static class TiaScriptTokenizer {
    private static readonly HashSet<string> JavaScriptKeywords = new(StringComparer.Ordinal) {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "null", "of",
        "return", "static", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "with", "yield",
    };

    private static readonly HashSet<string> VbKeywords = new(StringComparer.OrdinalIgnoreCase) {
        "And", "As", "ByRef", "ByVal", "Call", "Case", "Const", "Dim", "Do", "Each", "Else", "ElseIf", "Empty", "End", "Erase", "Error",
        "Exit", "Explicit", "False", "For", "Function", "If", "In", "Is", "Loop", "Mod", "Next", "Not", "Nothing", "Null", "On", "Option",
        "Or", "Preserve", "Private", "Public", "ReDim", "Resume", "Select", "Set", "Step", "Sub", "Then", "To", "True", "Until", "Wend",
        "While", "With", "Xor",
    };

    /// <summary>JavaScript: <c>//</c> and <c>/* */</c> comments, strings in <c>'</c>, <c>"</c> or <c>`</c>, numbers and keywords.</summary>
    public static TiaDocument JavaScript(string text) {
        var tokens = new List<TiaToken>();
        var i = 0;
        while (i < text.Length) {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') {
                i = Add(tokens, i, LineEnd(text, i), TiaTokenKind.Comment);
            } else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = Add(tokens, i, end < 0 ? text.Length : end + 2, TiaTokenKind.Comment);
            } else if (c is '"' or '\'' or '`') {
                var end = i + 1;
                while (end < text.Length && text[end] != c && (c == '`' || text[end] != '\n')) {
                    end += text[end] == '\\' ? 2 : 1;
                }
                i = Add(tokens, i, Math.Min(text.Length, end + 1), TiaTokenKind.Literal);
            } else {
                i = Word(text, i, tokens, JavaScriptKeywords);
            }
        }
        return new TiaDocument(text, tokens);
    }

    /// <summary>VBScript: <c>'</c> and <c>Rem</c> comments, strings in <c>"</c> with <c>""</c> inside, numbers and keywords.</summary>
    public static TiaDocument VbScript(string text) {
        var tokens = new List<TiaToken>();
        var i = 0;
        while (i < text.Length) {
            var c = text[i];
            if (c == '\'' || (IsWordStart(text, i) && string.Compare(text, i, "Rem", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
                && (i + 3 == text.Length || !char.IsLetterOrDigit(text[i + 3])))) {
                i = Add(tokens, i, LineEnd(text, i), TiaTokenKind.Comment);
            } else if (c == '"') {
                var end = i + 1;
                while (end < text.Length && text[end] != '\n' && !(text[end] == '"' && (end + 1 == text.Length || text[end + 1] != '"'))) {
                    end += text[end] == '"' ? 2 : 1;
                }
                i = Add(tokens, i, Math.Min(text.Length, end + 1), TiaTokenKind.Literal);
            } else {
                i = Word(text, i, tokens, VbKeywords);
            }
        }
        return new TiaDocument(text, tokens);
    }

    /// <summary>A keyword, a number, a name, or one other character; returns where the next token starts.</summary>
    private static int Word(string text, int i, List<TiaToken> tokens, HashSet<string> keywords) {
        if (!char.IsLetterOrDigit(text[i]) && text[i] is not ('_' or '$')) {
            return i + 1;
        }
        var end = i;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '$' || (char.IsDigit(text[i]) && text[end] == '.'))) {
            end++;
        }
        if (char.IsDigit(text[i])) {
            Add(tokens, i, end, TiaTokenKind.Literal);
        } else if (IsWordStart(text, i) && keywords.Contains(text[i..end])) {
            Add(tokens, i, end, TiaTokenKind.Keyword);
        }
        return end;
    }

    /// <summary>Not after a dot: <c>x.delete</c> is a member, not a keyword.</summary>
    private static bool IsWordStart(string text, int i) => i == 0 || text[i - 1] != '.';

    private static int LineEnd(string text, int i) {
        var end = text.IndexOf('\n', i);
        return end < 0 ? text.Length : end;
    }

    private static int Add(List<TiaToken> tokens, int start, int end, TiaTokenKind kind) {
        tokens.Add(new TiaToken(start, end - start, kind));
        return end;
    }
}
