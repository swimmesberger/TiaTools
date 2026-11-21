using TiaFormat.Program;

namespace TiaViewer;

/// <summary>A choice of the View menu's Language list: a project language, how it is labelled, and whether it is the one shown.</summary>
public readonly record struct LanguageChoice(string Name, string Label, bool IsShown);

/// <summary>
/// The languages the viewer offers (spec 0014, FR-016): the project languages in the project's order, the editing and
/// the reference language marked, and the one the texts are shown in checked. A file that names none offers none.
/// </summary>
public static class ViewerLanguages {
    public static IReadOnlyList<LanguageChoice> Choices(TiaProgram program) =>
        [.. program.Languages.All.Select(language => new LanguageChoice(language.Name, program.Languages.Describe(language), language == program.Language))];
}
