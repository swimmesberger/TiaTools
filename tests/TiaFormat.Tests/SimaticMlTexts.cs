using System.Xml.Linq;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>
/// The texts of TIA's SimaticML export, which lists a <c>MultilingualTextItem</c> per project language (spec 0014), read
/// in one language by the rule the reader follows (FR-004), so that a comparison of one language sees the same text.
/// </summary>
internal static class SimaticMlTexts {
    /// <summary>The culture names a program reads texts in, in order: the language written, the reference language, the others.</summary>
    public static IReadOnlyList<string> Preference(TiaProgram program) =>
        [.. program.TextPreference.Select(lcid => program.Languages.All.FirstOrDefault(language => language.Lcid == lcid)?.Name).OfType<string>()];

    /// <summary>Each language's text of a <c>MultilingualText</c>, empty ones too, by culture name; empty if there is none.</summary>
    public static IReadOnlyDictionary<string, string> All(XElement? text) =>
        (text?.Descendants().Where(element => element.Name.LocalName == "MultilingualTextItem") ?? [])
            .Select(item => (Culture: Child(item, "Culture"), Text: Child(item, "Text")))
            .ToDictionary(item => item.Culture, item => item.Text);

    /// <summary>
    /// A <c>MultilingualText</c> in one language: the first culture of <paramref name="preference"/> that has text, else
    /// the first language that has any, else empty.
    /// </summary>
    public static string Pick(XElement? text, IReadOnlyList<string> preference) {
        var texts = All(text);
        foreach (var culture in preference) {
            if (texts.TryGetValue(culture, out var value) && value.Length != 0) {
                return value;
            }
        }
        return texts.Values.FirstOrDefault(value => value.Length != 0) ?? "";
    }

    private static string Child(XElement item, string name) =>
        item.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? "";
}
