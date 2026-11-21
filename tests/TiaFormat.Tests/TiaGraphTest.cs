using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>Spec 0009: a GRAPH block in statement form, on sequences built here; the reference project checks TIA's.</summary>
public sealed class TiaGraphTest {
    private static TiaFlgNet Condition(string coil, string? operand) {
        var net = new TiaFlgNet();
        var end = new TiaFlgPart(30, coil);
        if (operand is null) {
            net.Parts.Add(end);
            net.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Powerrail), new TiaFlgEnd(TiaFlgEndKind.Pin, 30, "in")]);
            return net;
        }
        net.Operands[21] = operand;
        var contact = new TiaFlgPart(22, "Contact");
        net.Parts.AddRange([contact, end]);
        net.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Powerrail), new TiaFlgEnd(TiaFlgEndKind.Pin, 22, "in")]);
        net.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Operand, 21), new TiaFlgEnd(TiaFlgEndKind.Pin, 22, "operand")]);
        net.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Pin, 22, "out"), new TiaFlgEnd(TiaFlgEndKind.Pin, 30, "in")]);
        return net;
    }

    private static TiaGraphConnection Link(char fromKind, int from, char toKind, int to, bool jump = false) =>
        new(new TiaGraphNode(fromKind, from), new TiaGraphNode(toKind, to), jump);

    /// <summary>Idle, then Fill and Heat at once, then Mix, and back to Idle.</summary>
    private static TiaGraph Sequence() {
        var graph = new TiaGraph();
        var fill = new TiaGraphStep(2, "Fill", false) { Supervision = Condition("SvCoil", "#overflow"), Interlock = Condition("IlCoil", null) };
        fill.Actions.Add(new TiaGraphAction("N", "", false, "#valve"));
        fill.Actions.Add(new TiaGraphAction("D", "", false, "#lamp, T#2S"));
        graph.Steps.AddRange([new TiaGraphStep(1, "Idle", true), fill, new TiaGraphStep(3, "Heat", false), new TiaGraphStep(4, "Mix", false)]);
        graph.Transitions.AddRange([new TiaGraphTransition(1, "Go", Condition("TrCoil", "#start")), new TiaGraphTransition(2, "Ready", Condition("TrCoil", "#full")),
            new TiaGraphTransition(3, "Back", Condition("TrCoil", null))]);
        graph.Branches[1] = "SimBegin";
        graph.Branches[2] = "SimEnd";
        graph.Connections.AddRange([Link('S', 1, 'T', 1), Link('T', 1, 'B', 1), Link('B', 1, 'S', 2), Link('B', 1, 'S', 3), Link('S', 2, 'B', 2), Link('S', 3, 'B', 2),
            Link('B', 2, 'T', 2), Link('T', 2, 'S', 4), Link('S', 4, 'T', 3), Link('T', 3, 'S', 1, jump: true)]);
        return graph;
    }

    [Test]
    public void ATransitionNamesTheStepsItLeavesAndActivatesThroughBranches() {
        var lines = TiaGraph.Write(Sequence());

        lines.Should().Contain("TRANSITION 1 Go: Idle -> Fill, Heat WHEN #start");
        lines.Should().Contain("TRANSITION 2 Ready: Fill, Heat -> Mix WHEN #full");
    }

    [Test]
    public void AJumpBackIsMarkedAndAnEmptyConditionIsTrue() {
        TiaGraph.Write(Sequence()).Should().Contain("TRANSITION 3 Back: Mix -> Idle (jump) WHEN TRUE");
    }

    [Test]
    public void AStepListsItsActionsWithTheirQualifiersAndOnlyTheConditionsItHas() {
        var lines = TiaGraph.Write(Sequence());

        lines.SkipWhile(line => line != "STEP 2 Fill").Skip(1).TakeWhile(line => line.StartsWith(' ')).Should()
            .Equal("    N: #valve", "    D: #lamp, T#2S", "    supervision: #overflow");
        lines.Should().Contain("STEP 1 Idle (initial)");
    }
}
