using System.Globalization;
using System.Text;
using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>One action of a GRAPH step: its qualifier, the event that starts it, whether the interlock guards it, and its text.</summary>
public sealed record TiaGraphAction(string Qualifier, string Event, bool Interlocked, string Text);

/// <summary>A GRAPH step with its actions, and the supervision and interlock conditions it is guarded by.</summary>
public sealed class TiaGraphStep(int number, string name, bool initial) {
    public int Number { get; } = number;
    public string Name { get; } = name;
    public bool Initial { get; } = initial;
    public List<TiaGraphAction> Actions { get; } = [];
    public TiaFlgNet? Supervision { get; set; }
    public TiaFlgNet? Interlock { get; set; }
}

/// <summary>A GRAPH transition and the condition that fires it.</summary>
public sealed record TiaGraphTransition(int Number, string Name, TiaFlgNet Condition);

/// <summary>A node a connection of a sequence joins: a step (S), a transition (T), a branch (B) or the sequence end (E).</summary>
public readonly record struct TiaGraphNode(char Kind, int Number);

/// <summary>A connection of a sequence, from one node to the next; a jump goes back to a step elsewhere.</summary>
public readonly record struct TiaGraphConnection(TiaGraphNode From, TiaGraphNode To, bool Jump);

/// <summary>
/// A GRAPH block: the permanent instructions before and after the sequence, and the sequence's steps, transitions and the
/// connections between them through branches. Read from the stored units (docs/FORMAT.md, section 13) or from TIA's
/// SimaticML, and written in statement form by <see cref="Write(TiaGraph)"/> (ADR 0013).
/// </summary>
public sealed class TiaGraph {
    public List<TiaFlgNet> Before { get; } = [];
    public List<TiaFlgNet> After { get; } = [];
    public List<TiaGraphStep> Steps { get; } = [];
    public List<TiaGraphTransition> Transitions { get; } = [];

    /// <summary>Each branch by number: SimBegin, SimEnd, AltBegin or AltEnd.</summary>
    public Dictionary<int, string> Branches { get; } = [];

    public List<TiaGraphConnection> Connections { get; } = [];

    /// <summary>
    /// Reads the stored units of a GRAPH block in their order: the alarm settings, the permanent instructions before the
    /// sequence, the sequence's structure, then per step its actions, supervision and interlock and per transition its
    /// condition, each naming its step or transition by an identifier, and last the permanent instructions after it.
    /// </summary>
    public static TiaGraph Read(IReadOnlyList<ReadOnlyMemory<byte>> units, TiaIdentTable idents) {
        var graph = new TiaGraph();
        var stepIds = new Dictionary<int, TiaGraphStep>();
        var transitionIds = new Dictionary<int, (int Number, string Name)>();
        var structureSeen = false;
        foreach (var unit in units) {
            var span = unit.Span;
            var root = RootOf(span, out var refId);
            switch (root) {
                case "GraphStructure":
                    ReadStructure(span, idents, graph, stepIds, transitionIds);
                    structureSeen = true;
                    break;
                case "Actions" when refId is { } step && stepIds.TryGetValue(step, out var owner):
                    owner.Actions.AddRange(ReadActions(span, idents));
                    break;
                case "FlgNet":
                    var net = TiaFlgNet.Read(span, idents);
                    var coil = net.Parts.FirstOrDefault(part => part.Name is "TrCoil" or "SvCoil" or "IlCoil")?.Name;
                    if (refId is { } id && coil == "SvCoil" && stepIds.TryGetValue(id, out var supervised)) {
                        supervised.Supervision = net;
                    } else if (refId is { } id2 && coil == "IlCoil" && stepIds.TryGetValue(id2, out var interlocked)) {
                        interlocked.Interlock = net;
                    } else if (refId is { } id3 && coil == "TrCoil" && transitionIds.TryGetValue(id3, out var transition)) {
                        graph.Transitions.Add(new TiaGraphTransition(transition.Number, transition.Name, net));
                    } else if (refId is null) {
                        (structureSeen ? graph.After : graph.Before).Add(net);
                    }
                    break;
            }
        }
        return graph;
    }

    private static string RootOf(ReadOnlySpan<byte> xml, out int? refId) {
        var reader = new Utf8XmlReader(xml);
        refId = null;
        while (reader.Read()) {
            if (!reader.IsEnd) {
                refId = reader.GetInt32("RefId"u8);
                return XmlText.ToString(reader.Name);
            }
        }
        return "";
    }

    private static void ReadStructure(ReadOnlySpan<byte> xml, TiaIdentTable idents, TiaGraph graph, Dictionary<int, TiaGraphStep> stepIds, Dictionary<int, (int, string)> transitionIds) {
        var reader = new Utf8XmlReader(xml);
        var inConnection = false;
        var jump = false;
        var ends = new List<TiaGraphNode>();
        while (reader.Read()) {
            var name = reader.Name;
            if (reader.IsEnd) {
                if (name.SequenceEqual("Connection"u8) && ends.Count == 2) {
                    graph.Connections.Add(new TiaGraphConnection(ends[0], ends[1], jump));
                }
                if (name.SequenceEqual("Connection"u8)) {
                    inConnection = false;
                }
                continue;
            }
            if (name.SequenceEqual("Step"u8)) {
                // A step's number is its id less 100000, a transition's its id less 200000: what connections call No.
                var number = (reader.GetInt32("UId"u8) ?? 0) - 100000;
                var step = new TiaGraphStep(number, Plain(idents.Name(reader.GetInt32("RefId"u8))), reader.GetString("Init"u8) == "true");
                graph.Steps.Add(step);
                if (reader.GetInt32("RefId"u8) is { } stepId) {
                    stepIds[stepId] = step;
                }
            } else if (name.SequenceEqual("Transition"u8) && reader.GetInt32("RefId"u8) is { } transitionId) {
                transitionIds[transitionId] = ((reader.GetInt32("UId"u8) ?? 0) - 200000, Plain(idents.Name(transitionId)));
            } else if (name.SequenceEqual("Branch"u8)) {
                graph.Branches[reader.GetInt32("No"u8) ?? 0] = reader.GetString("Type"u8) ?? "";
            } else if (name.SequenceEqual("Connection"u8)) {
                inConnection = true;
                jump = false;
                ends.Clear();
            } else if (inConnection) {
                // A jump is marked on the step it goes to.
                jump |= reader.GetString("LinkType"u8) == "Jump";
                if (name.SequenceEqual("StepRef"u8)) {
                    ends.Add(new TiaGraphNode('S', reader.GetInt32("No"u8) ?? 0));
                } else if (name.SequenceEqual("TransitionRef"u8)) {
                    ends.Add(new TiaGraphNode('T', reader.GetInt32("No"u8) ?? 0));
                } else if (name.SequenceEqual("BranchRef"u8)) {
                    ends.Add(new TiaGraphNode('B', reader.GetInt32("No"u8) ?? 0));
                } else if (name.SequenceEqual("EndConnection"u8)) {
                    ends.Add(new TiaGraphNode('E', 0));
                }
            }
        }
    }

    private static IEnumerable<TiaGraphAction> ReadActions(ReadOnlySpan<byte> xml, TiaIdentTable idents) {
        var reader = new Utf8XmlReader(xml);
        string? qualifier = null, startEvent = null;
        var interlocked = false;
        var text = new StringBuilder();
        var actions = new List<TiaGraphAction>();
        var depth = 0;
        while (reader.Read()) {
            var name = reader.Name;
            if (reader.IsEnd) {
                if (name.SequenceEqual("Action"u8)) {
                    if (!string.IsNullOrEmpty(qualifier) || text.Length != 0) {
                        actions.Add(new TiaGraphAction(qualifier ?? "", startEvent ?? "", interlocked, text.ToString().TrimEnd('\n')));
                    }
                    qualifier = null;
                    text.Clear();
                }
                if (name.SequenceEqual("CallInfo"u8)) {
                    depth--;
                }
                continue;
            }
            if (name.SequenceEqual("Action"u8)) {
                startEvent = reader.GetString("Event"u8);
                interlocked = reader.GetString("Interlock"u8) == "true";
            } else if (name.SequenceEqual("Operator"u8)) {
                qualifier = reader.GetString("Operation"u8);
            } else if (name.SequenceEqual("CallInfo"u8)) {
                if (!reader.IsEmpty) {
                    depth++;
                }
            } else if (depth == 0 && name.SequenceEqual("OpdAccess"u8)) {
                text.Append(idents.Name(reader.GetInt32("RefId"u8)));
            } else if (depth == 0 && name.SequenceEqual("Token"u8)) {
                text.Append(reader.GetString("Text"u8));
            } else if (depth == 0 && name.SequenceEqual("Blank"u8)) {
                text.Append(' ', reader.GetInt32("Num"u8) ?? 1);
            }
        }
        return actions;
    }

    /// <summary>A step's or transition's name: its static member, without the <c>#</c> the identifier table gives it.</summary>
    private static string Plain(string name) => name.StartsWith('#') ? name[1..].Trim('"') : name;

    /// <summary>
    /// The block in statement form: the permanent instructions before the sequence, each step with its actions and
    /// conditions, each transition with the steps it leaves and activates (branches resolved) and its condition, and the
    /// permanent instructions after. An empty supervision or interlock is left out.
    /// </summary>
    public static IReadOnlyList<string> Write(TiaGraph graph) {
        var lines = new List<string>();
        foreach (var net in graph.Before) {
            lines.Add("// permanent instructions before the sequence");
            lines.AddRange(NetworkStatements.Write(net));
        }
        var names = graph.Steps.ToDictionary(step => step.Number, step => step.Name);
        foreach (var step in graph.Steps.OrderBy(step => step.Number)) {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"STEP {step.Number} {step.Name}{(step.Initial ? " (initial)" : "")}"));
            foreach (var action in step.Actions) {
                var head = (action.Event.Length == 0 ? "" : action.Event + " ") + action.Qualifier + (action.Interlocked ? " interlocked" : "");
                foreach (var (line, index) in action.Text.Split('\n').Select((line, index) => (line, index))) {
                    lines.Add("    " + (index == 0 ? head + ": " : new string(' ', head.Length + 2)) + line);
                }
            }
            if (step.Supervision is { } supervision && NetworkStatements.Condition(supervision) is var watched && watched != "TRUE") {
                lines.Add("    supervision: " + watched);
            }
            if (step.Interlock is { } interlock && NetworkStatements.Condition(interlock) is var guarded && guarded != "TRUE") {
                lines.Add("    interlock: " + guarded);
            }
        }
        foreach (var transition in graph.Transitions.OrderBy(transition => transition.Number)) {
            var node = new TiaGraphNode('T', transition.Number);
            var from = Sources(graph, node, []).Select(step => names.GetValueOrDefault(step, "?")).ToList();
            var to = Targets(graph, node, []).Select(target => target.Step == 0 ? "end" : names.GetValueOrDefault(target.Step, "?") + (target.Jump ? " (jump)" : "")).ToList();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"TRANSITION {transition.Number} {transition.Name}: {string.Join(", ", from)} -> {string.Join(", ", to)} WHEN {NetworkStatements.Condition(transition.Condition)}"));
        }
        foreach (var net in graph.After) {
            lines.Add("// permanent instructions after the sequence");
            lines.AddRange(NetworkStatements.Write(net));
        }
        return lines;
    }

    /// <summary>The steps a node is reached from, through branches.</summary>
    private static List<int> Sources(TiaGraph graph, TiaGraphNode node, HashSet<TiaGraphNode> seen) {
        var result = new List<int>();
        foreach (var connection in graph.Connections.Where(connection => connection.To == node && !connection.Jump)) {
            if (connection.From.Kind == 'S') {
                result.Add(connection.From.Number);
            } else if (connection.From.Kind == 'B' && seen.Add(connection.From)) {
                result.AddRange(Sources(graph, connection.From, seen));
            }
        }
        return result;
    }

    /// <summary>The steps a node leads to, through branches; a step of 0 is the end of the sequence.</summary>
    private static List<(int Step, bool Jump)> Targets(TiaGraph graph, TiaGraphNode node, HashSet<TiaGraphNode> seen) {
        var result = new List<(int, bool)>();
        foreach (var connection in graph.Connections.Where(connection => connection.From == node)) {
            switch (connection.To.Kind) {
                case 'S':
                    result.Add((connection.To.Number, connection.Jump));
                    break;
                case 'E':
                    result.Add((0, false));
                    break;
                case 'B' when seen.Add(connection.To):
                    result.AddRange(Targets(graph, connection.To, seen));
                    break;
            }
        }
        return result;
    }
}
