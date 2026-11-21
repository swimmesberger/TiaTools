namespace TiaFormat.Storage;

/// <summary>A core text's text in one language, by its LCID (1031 for de-DE); empty if the language holds none.</summary>
public readonly record struct TiaText(int Lcid, string Text);
