namespace TiaFormat;

/// <summary>The bytes do not have the shape this reader knows.</summary>
public sealed class TiaFormatException : Exception {
    public TiaFormatException(string message) : base(message) { }
    public TiaFormatException(string message, Exception inner) : base(message, inner) { }
}
