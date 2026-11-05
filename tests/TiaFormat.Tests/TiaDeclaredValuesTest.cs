using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

public sealed class TiaDeclaredValuesTest {
    private static TiaMemberNode Node(string id, string type, string path, List<TiaMemberNode>? children = null, List<TiaMemberNode>? typeMembers = null, Dictionary<string, int>? constants = null) =>
        new(id, type, path, "", null, null, null, children ?? []) { Id = id, TypeMembers = typeMembers ?? [], Constants = constants ?? [] };

    private static string? Text(TiaMemberNode member, params (string Path, string Value)[] values) =>
        new TiaDeclaredValues(values.ToDictionary(value => value.Path, value => value.Value)).Text(member, member.Path);

    [Test]
    public void AScalarIsItsValue() =>
        Text(Node("51", "Int", "51"), ("51", "5")).Should().Be("5");

    [Test]
    public void NothingSetMeansNoValue() =>
        Text(Node("51", "Int", "51"), ("52", "5")).Should().BeNull();

    [Test]
    public void AUdtGathersItsPartsAndShowsWhatIsNotSetAsEmpty() =>
        Text(Node("51", "\"T\"", "51", typeMembers: [Node("1", "Int", "51:1"), Node("2", "Int", "51:2"), Node("3", "Int", "51:3")]), ("51:2", "7"))
            .Should().Be("((), 7, ())");

    [Test]
    public void AnArrayInsideAUdtWithNothingSetIsAnEmptyList() =>
        Text(Node("51", "\"T\"", "51", typeMembers: [Node("1", "Array[1..2] of Char", "51:1"), Node("2", "Int", "51:2")]), ("51:2", "7"))
            .Should().Be("([()], 7)");

    [Test]
    public void AStructInsideAUdtStaysClosedEvenWithValues() =>
        Text(Node("51", "\"T\"", "51", typeMembers: [Node("1", "Struct", "51:1", children: [Node("9", "Int", "51:1:9")]), Node("2", "Int", "51:2")]), ("51:1:9", "1"), ("51:2", "7"))
            .Should().Be("((), 7)");

    [Test]
    public void OnlyValuesInsideAClosedStructMeanNoValueAtAll() =>
        Text(Node("51", "\"T\"", "51", typeMembers: [Node("1", "Struct", "51:1", children: [Node("9", "Int", "51:1:9")])]), ("51:1:9", "1"))
            .Should().BeNull();

    [Test]
    public void APlainArrayCountsEqualNeighboursAndStopsAtItsLastValue() =>
        Text(Node("51", "Array[0..9] of Bool", "51"), ("51[3]", "true"), ("51[4]", "true"))
            .Should().Be("[3(false), 2(true)]");

    [Test]
    public void AnArrayOfAUdtWritesAllItsElementsOut() =>
        Text(Node("51", "Array[1..3] of \"T\"", "51", typeMembers: [Node("1", "Int", "51:1")]), ("51[1]:1", "7"), ("51[2]:1", "7"))
            .Should().Be("[(7), (7), (())]");

    [Test]
    public void ArrayBoundsMayBeConstants() =>
        Text(Node("51", "Array[0..\"LAST\"] of \"T\"", "51", typeMembers: [Node("1", "Int", "51:1")], constants: new Dictionary<string, int> { ["\"LAST\""] = 1 }), ("51[0]:1", "7"))
            .Should().Be("[(7), (())]");

    [Test]
    public void AMultiDimensionalArrayCountsInReadingOrder() =>
        Text(Node("51", "Array[1..2, 1..2] of Int", "51"), ("51[2,1]", "5"))
            .Should().Be("[2(0), 5]");

    [Test]
    public void AStructsOwnLinesLeaveItWithoutAValue() =>
        Text(Node("51", "Struct", "51", children: [Node("1", "Int", "51:1")]), ("51:1", "7")).Should().BeNull();

    [Test]
    public void TheFirstElementOfAnArrayOfStructsSpeaksForItsMembers() {
        var values = new TiaDeclaredValues(new Dictionary<string, string> { ["51[1]:1"] = "T#2s", ["51[2]:2"] = "9" });
        var array = Node("51", "Array[1..2] of Struct", "51");
        var first = TiaDeclaredValues.FirstElement(array, "51") + ":";

        values.Text(Node("1", "Time", "51:1"), first + "1").Should().Be("T#2s");
        values.Text(Node("2", "Int", "51:2"), first + "2").Should().BeNull();
    }
}

public sealed class TiaIdentifiersTest {
    [Test]
    [Arguments("Speed", "Speed")]
    [Arguments("Größe_1", "Größe_1")]
    [Arguments("1st_Value", "\"1st_Value\"")]
    [Arguments("a.b", "\"a.b\"")]
    [Arguments("Byte", "\"Byte\"")]
    [Arguments("name", "\"name\"")]
    [Arguments("LTime", "LTime")]
    public void ANameIsQuotedOnlyIfItMustBe(string name, string written) =>
        TiaIdentifiers.Quoted(name).Should().Be(written);
}

public sealed class TiaMemberFlagsTest {
    private const string Closed = "ExternalAccessible := 'False'; ExternalVisible := 'False'; ExternalWritable := 'False'";

    [Test]
    public void ThreeBitsCloseAMemberToOutsideAccess() =>
        TiaMemberFlags.Attributes(14, TiaMemberFlags.IsHidden(14, false), "Static", "Int", null).Should().Be("{ " + Closed + "}");

    [Test]
    public void AMemberWithoutFlagsIsAsItsParent() {
        TiaMemberFlags.IsHidden(null, hiddenFromParent: true).Should().BeTrue();
        TiaMemberFlags.IsHidden(null, hiddenFromParent: false).Should().BeFalse();
    }

    [Test]
    public void ASetPointIsShownOnStaticMembers() =>
        TiaMemberFlags.Attributes(527, false, "Static", "Bool", null).Should().Be("{ S7_SetPoint := 'True'}");

    [Test]
    public void ASetPointOfAStructuredMemberIsFalse() =>
        TiaMemberFlags.Attributes(527, false, "Static", "\"T\"", null).Should().Be("{ S7_SetPoint := 'False'}");

    [Test]
    public void AParameterShowsNoSetPoint() =>
        TiaMemberFlags.Attributes(527, false, "Input", "\"T\"", null).Should().BeNull();

    [Test]
    public void AnInstructionsAttributesComeFirstWithNoSpace() =>
        TiaMemberFlags.Attributes(14, true, "Static", "Array[1..2] of TON_TIME", "1.0")
            .Should().Be("{InstructionName := 'TON_TIME'; LibVersion := '1.0'; " + Closed + "}");
}
