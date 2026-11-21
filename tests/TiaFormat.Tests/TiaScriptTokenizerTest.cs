using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0017, FR-007: the tokens that colour an HMI device's code.</summary>
public sealed class TiaScriptTokenizerTest {
    private static List<(TiaTokenKind Kind, string Text)> Tokens(TiaDocument document) =>
        [.. document.Tokens.Select(token => (token.Kind, document.Text.Substring(token.Start, token.Length)))];

    [Test]
    public void JavaScriptKeywordsCommentsStringsAndNumbersAreTokens() {
        var tokens = Tokens(TiaScriptTokenizer.JavaScript("// note\nlet a = \"x // y\"; /* b */ return 0x1F;"));

        tokens.Should().Equal(
            (TiaTokenKind.Comment, "// note"), (TiaTokenKind.Keyword, "let"), (TiaTokenKind.Literal, "\"x // y\""),
            (TiaTokenKind.Comment, "/* b */"), (TiaTokenKind.Keyword, "return"), (TiaTokenKind.Literal, "0x1F"));
    }

    [Test]
    public void AJavaScriptKeywordAfterADotIsAMember() {
        Tokens(TiaScriptTokenizer.JavaScript("map.delete(key)")).Should().BeEmpty();
    }

    [Test]
    public void VbScriptCommentsAndStringsWithDoubledQuotesAreTokens() {
        var tokens = Tokens(TiaScriptTokenizer.VbScript("If x Then ' check\n  y = \"say \"\"hi\"\"\"\nRem done\nEnd If"));

        tokens.Should().Equal(
            (TiaTokenKind.Keyword, "If"), (TiaTokenKind.Keyword, "Then"), (TiaTokenKind.Comment, "' check"),
            (TiaTokenKind.Literal, "\"say \"\"hi\"\"\""), (TiaTokenKind.Comment, "Rem done"), (TiaTokenKind.Keyword, "End"), (TiaTokenKind.Keyword, "If"));
    }
}
