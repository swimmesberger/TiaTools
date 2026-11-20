using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0006: the nodes of a PLC's standard SIMATIC server interface, on the invented project of <see cref="TiaOpcUaFixture"/>.</summary>
public sealed class TiaOpcUaNodesTest {
    private static readonly XNamespace Ua = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

    // Helpers --------------------------------------------------------------------------------------------------------

    private static TiaOpcUaNodeList Filler() =>
        TiaOpcUaNodes.Read(new TiaProgram(TiaOpcUaFixture.NewProject(new TiaOpcUaFixture.Options { HasTypes = true })), "Filler").Single();

    private static TiaOpcUaNode Node(string id) => Filler().Nodes.Single(node => node.NodeId == "ns=3;s=" + id);

    private static IEnumerable<string> Ids() => Filler().Nodes.Select(node => node.NodeId["ns=3;s=".Length..]);

    private static XElement NodeSet() {
        var output = new MemoryStream();
        TiaOpcUaNodeSet.Write(Filler(), output);
        return XDocument.Parse(Encoding.UTF8.GetString(output.ToArray())).Root!;
    }

    // See which nodes a PLC offers -----------------------------------------------------------------------------------

    [Test]
    public void ADataBlockIsAnObjectAndEachOpenMemberAVariableWithIdTypeAndAccess() {
        Node("\"Recipe\"").NodeClass.Should().Be(TiaOpcUaNodeClass.Object);
        var speed = Node("\"Recipe\".\"Speed\"");
        speed.NodeClass.Should().Be(TiaOpcUaNodeClass.Variable);
        speed.BrowsePath.Should().Be("Filler/DataBlocksGlobal/Recipe/Speed");
        (speed.SimaticType, speed.DataType, speed.DataTypeName, speed.IsWritable).Should().Be(("Bool", "i=1", "BOOL", true));
        Node("\"Recipe\".\"Limit\"").IsWritable.Should().BeFalse();
    }

    [Test]
    public void AClosedMemberOrOneUnderAClosedOneHasNoNode() =>
        Ids().Should().NotContain(["\"Recipe\".\"Internal\"", "\"Recipe\".\"Hidden\"", "\"Recipe\".\"Hidden\".\"Inside\""])
            .And.Contain("\"Recipe\".\"Unlisted\"");

    [Test]
    public void ADataBlockClosedToOpcUaOrWithEveryMemberClosedHasNoNode() =>
        Ids().Should().NotContain(["\"Service\"", "\"Sealed\""]);

    [Test]
    public void GlobalAndInstanceDataBlocksAreInTheirFoldersBelowThePlc() {
        Node("PLC").BrowseName.Should().Be("Filler");
        Node("DataBlocksGlobal").ParentNodeId.Should().Be("ns=3;s=PLC");
        Node("\"Recipe\"").ParentNodeId.Should().Be("ns=3;s=DataBlocksGlobal");
        Node("\"LineData\"").ParentNodeId.Should().Be("ns=3;s=DataBlocksInstance");
    }

    [Test]
    public void AnInstanceDataBlocksMembersAreInSectionObjects_ButTheirIdsLeaveTheSectionOut() {
        Node("\"LineData\".Inputs").ParentReference.Should().Be("HasInputVar");
        var start = Node("\"LineData\".\"start\"");
        (start.ParentNodeId, start.ParentReference).Should().Be(("ns=3;s=\"LineData\".Inputs", "HasInputVar"));
        var valve = Node("\"LineData\".\"valve\"");
        (valve.NodeClass, valve.ParentNodeId, valve.TypeDefinition).Should().Be((TiaOpcUaNodeClass.Object, "ns=3;s=\"LineData\".Static", "ns=3;s=OT_\"Valve\""));
        Node("\"LineData\".\"valve\".\"open\"").ParentNodeId.Should().Be("ns=3;s=\"LineData\".\"valve\".Inputs");
        Ids().Should().NotContain("\"LineData\".\"valve\".\"scratch\"");
    }

    [Test]
    public void AUdtStructOrDtlMemberIsAVariableWithItsMembersBelow() {
        Node("\"Doses\".\"active\"").DataType.Should().Be("ns=3;s=DT_\"Dose\"");
        Node("\"Doses\".\"active\".\"code\"").SimaticType.Should().Be("Byte");
        Node("\"Doses\".\"limits\"").DataType.Should().Be("ns=3;s=DT_\"Doses\".\"limits\"");
        Node("\"Doses\".\"limits\".\"low\"").ParentNodeId.Should().Be("ns=3;s=\"Doses\".\"limits\"");
        Node("\"Doses\".\"stamp\".YEAR").DataTypeName.Should().Be("UINT");
    }

    [Test]
    public void AnIecTimerIsAVariableWithItsMembers_AndReadOnlyAsTwoOfThemAre() {
        var timer = Node("\"LineData\".\"valve\".\"moveTimer\"");
        (timer.DataType, timer.IsWritable).Should().Be(("ns=3;s=DT_IEC_TIMER", false));
        Node("\"LineData\".\"valve\".\"moveTimer\".PT").IsWritable.Should().BeTrue();
        Node("\"LineData\".\"valve\".\"moveTimer\".Q").IsWritable.Should().BeFalse();
    }

    [Test]
    public void AnArrayIsAVariableWithItsDimensions_AndEachElementCountedFromZeroIsOne() {
        var list = Node("\"Doses\".\"list\"");
        list.DataType.Should().Be("ns=3;s=DT_\"Dose\"");
        list.ArrayDimensions.Should().Equal(2);
        Node("\"Doses\".\"list\"[1]").BrowseName.Should().Be("1");
        Node("\"Doses\".\"list\"[1].\"volume\"").DataTypeName.Should().Be("REAL");
        Node("\"Doses\".\"grid\"").ArrayDimensions.Should().Equal(2, 2);
        Ids().Should().Contain("\"Doses\".\"grid\"[1,1]").And.NotContain("\"Doses\".\"list\"[2]");
    }

    [Test]
    public void AStructWithAReadOnlyMemberIsReadOnly() =>
        Node("\"Doses\".\"limits\"").IsWritable.Should().BeFalse();

    [Test]
    public void AccessibleTagsAreVariablesInTheFolderOfTheirAddressArea_AndOthersAreNot() {
        Node("\"Start\"").ParentNodeId.Should().Be("ns=3;s=Inputs");
        Node("\"Level\"").IsWritable.Should().BeFalse();
        Ids().Should().NotContain(["\"Spare\"", "\"MaxBottles\""]);
    }

    [Test]
    public void AKnowHowProtectedDataBlockIsListedWithMembersUnknown() {
        Node("\"Locked\"").MembersUnknown.Should().BeTrue();
        Ids().Should().NotContain("\"Locked\".\"Inside\"");
    }

    [Test]
    public void TheListSaysItIsDerivedAndThatTheNamespaceIndexIsTheDefault() {
        var text = new StringWriter();
        TiaOpcUaNodes.WriteText([Filler()], text);

        text.ToString().Should().StartWith("# Derived by TiaTools from the program").And.Contain("Namespace index 3 is the default");
    }

    // Use it from the command line -----------------------------------------------------------------------------------

    [Test]
    public void TheTextHasAHeaderLineAndOneTabSeparatedLinePerNode() {
        var text = new StringWriter();
        TiaOpcUaNodes.WriteText([Filler()], text);

        var lines = text.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        lines[1].Should().StartWith("PLC\tNodeId\tNodeClass\tBrowsePath");
        lines.Should().HaveCount(Filler().Nodes.Count + 2).And.Contain("Filler\tns=3;s=\"Recipe\".\"Limit\"\tVariable\tFiller/DataBlocksGlobal/Recipe/Limit\tBool\tBOOL\t\tread-only\tRecipe");
    }

    // Use the export in a client -------------------------------------------------------------------------------------

    [Test]
    public void TheNodeSetIsUaNodeSetXmlWithTheSiemensNamespaceAndTheNodesAsTiaExportsThem() {
        var root = NodeSet();

        root.Name.Should().Be(Ua + "UANodeSet");
        root.Element(Ua + "NamespaceUris")!.Elements(Ua + "Uri").Select(uri => uri.Value).Should().Equal("", "http://opcfoundation.org/UA/DI/", TiaOpcUaNodes.Namespace);
        var limit = root.Elements(Ua + "UAVariable").Single(node => (string?)node.Attribute("NodeId") == "ns=3;s=\"Recipe\".\"Limit\"");
        ((string?)limit.Attribute("DataType"), (string?)limit.Attribute("AccessLevel"), (string?)limit.Attribute("BrowseName")).Should().Be(("BOOL", null, "3:Limit"));
        root.Element(Ua + "Aliases")!.Elements().Should().Contain(alias => (string?)alias.Attribute("Alias") == "BOOL" && alias.Value == "i=1");
        var grid = root.Elements(Ua + "UAVariable").Single(node => (string?)node.Attribute("NodeId") == "ns=3;s=\"Doses\".\"grid\"");
        ((string?)grid.Attribute("ValueRank"), (string?)grid.Attribute("ArrayDimensions"), (string?)grid.Attribute("AccessLevel")).Should().Be(("2", "2,2", "3"));
        root.Elements(Ua + "UAObject").Single(node => (string?)node.Attribute("NodeId") == "ns=3;s=\"LineData\".Inputs")
            .Descendants(Ua + "Reference").Should().Contain(reference => (string?)reference.Attribute("ReferenceType") == "HasInputVar" && reference.Value == "ns=3;s=\"LineData\"");
    }

    [Test]
    public void TheNodeSetSaysItIsDerived() =>
        NodeSet().Document!.Nodes().OfType<XComment>().Single().Value.Should().Contain("not read from TIA");
}
