using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

public sealed class TiaSourceComparisonTest {
    [Test]
    public void OurText_CutsLeadingCommentsAndTrailingComment() {
        const string dumped = "// DB number=1 language=DB know-how=no plc=P\n// folder: F\nDATA_BLOCK \"A\"\nBEGIN\n\nEND_DATA_BLOCK\n\n// The members of its type:\n//   x : Int;\n";

        TiaSourceComparison.OurText(dumped).Should().Be("DATA_BLOCK \"A\"\nBEGIN\n\nEND_DATA_BLOCK\n\n");
    }

    [Test]
    public void OurText_KeepsAnEndWordInsideTheBody() {
        const string dumped = "// FC\n// folder: F\nFUNCTION \"A\" : Void\nBEGIN\n\tEND_IF;\nEND_FUNCTION\n\n";

        TiaSourceComparison.OurText(dumped).Should().Be("FUNCTION \"A\" : Void\nBEGIN\n\tEND_IF;\nEND_FUNCTION\n\n");
    }

    [Test]
    public void TheirText_DropsTheByteOrderMarkAndCarriageReturns() {
        TiaSourceComparison.TheirText("﻿TYPE \"A\"\r\nEND_TYPE\r\n\r\n").Should().Be("TYPE \"A\"\nEND_TYPE\n\n");
    }

    [Test]
    public void Compare_SameTextIsIdentical() {
        TiaSourceComparison.Compare("a\nb\n", "a\nb\n").Match.Should().Be(TiaSourceMatch.Identical);
    }

    [Test]
    public void Compare_ReportsTrailingWhitespaceWhenThatIsAllThatDiffers() {
        var difference = TiaSourceComparison.Compare("a\nb\nc\n", "a\nb \nc\n");

        difference.Should().Be(new TiaSourceDifference(TiaSourceMatch.TrailingWhitespace, 2, "b", "b "));
    }

    [Test]
    public void Compare_RealDifferenceWinsOverAnEarlierWhitespaceOne() {
        var difference = TiaSourceComparison.Compare("a\nb\nc\n", "a \nb\nd\n");

        difference.Should().Be(new TiaSourceDifference(TiaSourceMatch.Different, 3, "c", "d"));
    }

    [Test]
    public void Compare_AMissingLineIsADifference() {
        var difference = TiaSourceComparison.Compare("a\n", "a\n\n");

        difference.Should().Be(new TiaSourceDifference(TiaSourceMatch.Different, 3, null, ""));
    }
}
