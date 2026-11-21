using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0009, ADR 0013: the statement form of a LAD or FBD network, one rule per test, on networks built here. The
/// reference project Languages checks the same rules against TIA's own export.
/// </summary>
public sealed class NetworkStatementsTest {
    /// <summary>Builds a network: operands, parts and wires, with uids handed out in order.</summary>
    private sealed class Net {
        private int _uid = 20;

        public TiaFlgNet Graph { get; } = new();

        public int Operand(string name) {
            Graph.Operands[++_uid] = name;
            return _uid;
        }

        public TiaFlgPart Part(string name, params string[] negated) {
            var part = new TiaFlgPart(++_uid, name);
            part.Negated.UnionWith(negated);
            Graph.Parts.Add(part);
            return part;
        }

        public void Rail(params (TiaFlgPart Part, string Pin)[] sinks) =>
            Graph.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Powerrail), .. sinks.Select(sink => new TiaFlgEnd(TiaFlgEndKind.Pin, sink.Part.Uid, sink.Pin))]);

        public void Wire(int operand, TiaFlgPart part, string pin) =>
            Graph.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Operand, operand), new TiaFlgEnd(TiaFlgEndKind.Pin, part.Uid, pin)]);

        public void Wire(TiaFlgPart from, string output, TiaFlgPart to, string input) =>
            Graph.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Pin, from.Uid, output), new TiaFlgEnd(TiaFlgEndKind.Pin, to.Uid, input)]);

        public void Wire(TiaFlgPart from, string output, int operand) =>
            Graph.Wires.Add([new TiaFlgEnd(TiaFlgEndKind.Pin, from.Uid, output), new TiaFlgEnd(TiaFlgEndKind.Operand, operand)]);

        public string Text => string.Join("\n", NetworkStatements.Write(Graph));
    }

    [Test]
    public void ContactsInSeriesAndInParallelBecomeAndAndOr() {
        var net = new Net();
        var (start, motorIn, stop, motor) = (net.Operand("#start"), net.Operand("#motor"), net.Operand("#stop"), net.Operand("#motor"));
        var (first, second, either, third, coil) = (net.Part("Contact"), net.Part("Contact"), net.Part("O"), net.Part("Contact", "operand"), net.Part("Coil"));
        net.Rail((first, "in"), (second, "in"));
        net.Wire(start, first, "operand");
        net.Wire(motorIn, second, "operand");
        net.Wire(first, "out", either, "in1");
        net.Wire(second, "out", either, "in2");
        net.Wire(either, "out", third, "in");
        net.Wire(stop, third, "operand");
        net.Wire(third, "out", coil, "in");
        net.Wire(motor, coil, "operand");

        net.Text.Should().Be("#motor := (#start OR #motor) AND NOT #stop;");
    }

    [Test]
    public void SetAndResetCoilsBecomeConditionalAssignments() {
        var net = new Net();
        var (start, latched) = (net.Operand("#start"), net.Operand("#latched"));
        var (contact, set, reset) = (net.Part("Contact"), net.Part("SCoil"), net.Part("RCoil"));
        net.Rail((contact, "in"), (reset, "in"));
        net.Wire(start, contact, "operand");
        net.Wire(contact, "out", set, "in");
        net.Wire(latched, set, "operand");
        net.Wire(latched, reset, "operand");

        net.Text.Should().Be("IF #start THEN #latched := TRUE; END_IF;\n#latched := FALSE;");
    }

    [Test]
    public void AnEdgeNamesItsSignalAndItsMemory() {
        var net = new Net();
        var (start, memory, lamp) = (net.Operand("#start"), net.Operand("#edgeMemory"), net.Operand("#lamp"));
        var (edge, coil) = (net.Part("PContact"), net.Part("Coil"));
        net.Rail((edge, "pre"));
        net.Wire(start, edge, "operand");
        net.Wire(memory, edge, "bit");
        net.Wire(edge, "out", coil, "in");
        net.Wire(lamp, coil, "operand");

        net.Text.Should().Be("#lamp := P(#start, #edgeMemory);");
    }

    [Test]
    public void ABoxWithAnEnableInputRunsInsideAnIf() {
        var net = new Net();
        var (run, level, copy) = (net.Operand("#run"), net.Operand("#level"), net.Operand("#copy"));
        var move = net.Part("Move");
        net.Wire(run, move, "en");
        net.Wire(level, move, "in");
        net.Wire(move, "out1", copy);

        net.Text.Should().Be("IF #run THEN\n    #copy := #level;\nEND_IF;");
    }

    [Test]
    public void ACallNamesItsParametersInTheOrderTheBlockDeclaresThem() {
        var net = new Net();
        var (high, level, clamped) = (net.Operand("\"Setpoint\""), net.Operand("#level"), net.Operand("#clamped"));
        var call = net.Part("\"Limit\"");
        call.IsCall = true;
        call.Parameters.AddRange([new("value", "Input"), new("high", "Input"), new("Ret_Val", "Return")]);
        net.Rail((call, "en"));
        net.Wire(high, call, "high");
        net.Wire(level, call, "value");
        net.Wire(call, "Ret_Val", clamped);

        net.Text.Should().Be("#clamped := \"Limit\"(value := #level, high := \"Setpoint\");");
    }

    [Test]
    public void AnInstructionCallsItsInstanceAndItsOutputsAreItsMembers() {
        var net = new Net();
        var (high, time, done) = (net.Operand("#high"), net.Operand("T#2S"), net.Operand("#done"));
        var timer = net.Part("TON");
        timer.IsInstruction = true;
        timer.Instance = "#delay";
        var coil = net.Part("Coil");
        net.Wire(time, timer, "PT");
        net.Wire(high, timer, "IN");
        net.Wire(timer, "Q", coil, "in");
        net.Wire(done, coil, "operand");

        net.Text.Should().Be("#delay(IN := #high, PT := T#2S);\n#done := #delay.Q;");
    }

    [Test]
    public void AJumpIsAConditionalGotoToItsLabel() {
        var net = new Net();
        var (door, label) = (net.Operand("#door"), net.Operand("skip"));
        var jump = net.Part("JumpNot");
        net.Wire(door, jump, "in");
        net.Wire(label, jump, "label");
        net.Graph.Labels.Add("start");

        net.Text.Should().Be("start:\nIF NOT #door THEN GOTO skip; END_IF;");
    }

    [Test]
    public void ANegatedOutputInvertsWhatItsWireCarries() {
        var net = new Net();
        var (a, b, fault) = (net.Operand("#a"), net.Operand("#b"), net.Operand("#fault"));
        var (gate, coil) = (net.Part("A", "out"), net.Part("Coil"));
        net.Wire(a, gate, "in1");
        net.Wire(b, gate, "in2");
        net.Wire(gate, "out", coil, "in");
        net.Wire(fault, coil, "operand");

        net.Text.Should().Be("#fault := NOT (#a AND #b);");
    }

    [Test]
    public void APartWithoutARuleIsACallNamingEveryConnectedPin() {
        var net = new Net();
        var (input, output) = (net.Operand("#x"), net.Operand("#y"));
        var part = net.Part("Swap");
        net.Wire(input, part, "IN");
        net.Wire(part, "out", output);

        net.Text.Should().Be("Swap(IN := #x, out => #y);");
    }
}
