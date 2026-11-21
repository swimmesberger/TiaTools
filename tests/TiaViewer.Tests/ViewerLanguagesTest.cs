using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaViewer.Tests;

/// <summary>
/// Spec 0014, FR-016: the View menu's Language list offers the project languages, the editing and reference language
/// marked, with the one shown checked. Read from the reference project Texts, which TIA Portal built.
/// </summary>
public sealed class ViewerLanguagesTest {
    private static string Archive() {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent) {
            if (File.Exists(Path.Combine(folder.FullName, "TiaTools.slnx"))) {
                return Path.Combine(folder.FullName, "tests", "reference", "Texts", "Texts.zap21");
            }
        }
        throw new InvalidOperationException("No TiaTools.slnx above " + AppContext.BaseDirectory);
    }

    [Test]
    public void The_language_list_offers_the_project_languages_with_the_editing_language_checked() {
        var choices = ViewerLanguages.Choices(new TiaProgram(TiaDatabase.Open(Archive())));

        choices.Should().Equal(
            new LanguageChoice("en-US", "en-US (reference)", false),
            new LanguageChoice("de-DE", "de-DE (editing)", true),
            new LanguageChoice("fr-FR", "fr-FR", false));
    }

    [Test]
    public void The_language_chosen_is_the_one_checked() {
        var choices = ViewerLanguages.Choices(new TiaProgram(TiaDatabase.Open(Archive()), "fr-FR"));

        choices.Single(choice => choice.IsShown).Name.Should().Be("fr-FR");
    }
}
