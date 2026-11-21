using System.Xml.Linq;
using TiaFormat.Program;

namespace TiaFormat.Tests;

/// <summary>
/// Reads the networks of TIA's SimaticML export of a block (spec 0009), so that the reference tests write TIA's own
/// version of each network through the same statement form as the archive's. SimaticML names what the stored form
/// numbers: an <c>Access</c> holds its operand's name where the stored <c>ORef</c> holds an identifier id.
/// </summary>
internal static class SimaticMlNetworks {
    /// <summary>A compile unit of the export: its language, title, comment and network source.</summary>
    public sealed record Unit(string Language, string Title, string Comment, XElement Source);

    /// <summary>The compile units of an export, each title and comment in the language <paramref name="preference"/> picks (spec 0014, FR-004).</summary>
    public static List<Unit> Units(string file, IReadOnlyList<string> preference) {
        var document = XDocument.Load(file);
        return [.. document.Descendants().Where(element => element.Name.LocalName == "SW.Blocks.CompileUnit").Select(unit => {
            var attributes = unit.Element("AttributeList")!;
            return new Unit(
                attributes.Element("ProgrammingLanguage")?.Value ?? "",
                Text(unit, "Title", preference),
                Text(unit, "Comment", preference),
                attributes.Element("NetworkSource")?.Elements().FirstOrDefault() ?? new XElement("Empty"));
        })];
    }

    private static string Text(XElement unit, string composition, IReadOnlyList<string> preference) =>
        SimaticMlTexts.Pick(unit.Element("ObjectList")?.Elements("MultilingualText").FirstOrDefault(text => (string?)text.Attribute("CompositionName") == composition), preference);

    /// <summary>A SimaticML <c>FlgNet</c> as the graph the statement form is written from.</summary>
    public static TiaFlgNet Read(XElement flgNet) {
        var net = new TiaFlgNet();
        foreach (var label in Children(flgNet, "Labels").SelectMany(labels => Children(labels, "LabelDeclaration"))) {
            net.Labels.Add((string?)Children(label, "Label").FirstOrDefault()?.Attribute("Name") ?? "?");
        }
        foreach (var element in Children(flgNet, "Parts").SelectMany(parts => parts.Elements())) {
            var uid = (int)element.Attribute("UId")!;
            switch (element.Name.LocalName) {
                case "Access":
                    net.Operands[uid] = Operand(element);
                    break;
                case "Part":
                    var part = new TiaFlgPart(uid, (string)element.Attribute("Name")!);
                    if (Children(element, "Instance").FirstOrDefault() is { } instance) {
                        part.IsInstruction = true;
                        part.Instance = Instance(instance);
                    }
                    Negate(part, element);
                    net.Parts.Add(part);
                    break;
                case "Call":
                    var info = Children(element, "CallInfo").Single();
                    var call = new TiaFlgPart(uid, "\"" + (string)info.Attribute("Name")! + "\"") { IsCall = true };
                    if (Children(info, "Instance").FirstOrDefault() is { } callInstance) {
                        call.Instance = Instance(callInstance);
                    }
                    call.Parameters.AddRange(Children(info, "Parameter").Select(parameter => new TiaParameter((string)parameter.Attribute("Name")!, (string?)parameter.Attribute("Section") ?? "")));
                    Negate(call, element);
                    net.Parts.Add(call);
                    break;
            }
        }
        foreach (var wire in Children(flgNet, "Wires").SelectMany(wires => Children(wires, "Wire"))) {
            net.Wires.Add([.. wire.Elements().Select(end => end.Name.LocalName switch {
                "Powerrail" => new TiaFlgEnd(TiaFlgEndKind.Powerrail),
                "IdentCon" => new TiaFlgEnd(TiaFlgEndKind.Operand, (int)end.Attribute("UId")!),
                "NameCon" => new TiaFlgEnd(TiaFlgEndKind.Pin, (int)end.Attribute("UId")!, (string)end.Attribute("Name")!),
                _ => new TiaFlgEnd(TiaFlgEndKind.Open, (int?)end.Attribute("UId") ?? 0),
            })]);
        }
        return net;
    }

    /// <summary>A SimaticML <c>Graph</c> as the sequence its statement form is written from.</summary>
    public static TiaGraph ReadGraph(XElement graphElement) {
        var graph = new TiaGraph();
        foreach (var net in Children(graphElement, "PreOperations").SelectMany(operations => Children(operations, "PermanentOperation")).SelectMany(operation => Children(operation, "FlgNet"))) {
            graph.Before.Add(Read(net));
        }
        foreach (var net in Children(graphElement, "PostOperations").SelectMany(operations => Children(operations, "PermanentOperation")).SelectMany(operation => Children(operation, "FlgNet"))) {
            graph.After.Add(Read(net));
        }
        var sequence = Children(graphElement, "Sequence").Single();
        foreach (var element in Children(sequence, "Steps").SelectMany(steps => Children(steps, "Step"))) {
            var step = new TiaGraphStep((int)element.Attribute("Number")!, (string)element.Attribute("Name")!, (string?)element.Attribute("Init") == "true");
            foreach (var action in Children(element, "Actions").SelectMany(actions => Children(actions, "Action"))) {
                var qualifier = (string?)action.Attribute("Qualifier") ?? "";
                var text = string.Concat(Children(action, "Token").Select(token => (string)token.Attribute("Text")!)).TrimEnd('\n');
                if (qualifier.Length != 0 || text.Length != 0) {
                    step.Actions.Add(new TiaGraphAction(qualifier, (string?)action.Attribute("Event") ?? "", (string?)action.Attribute("Interlock") == "true", text));
                }
            }
            step.Supervision = Children(element, "Supervisions").SelectMany(item => Children(item, "Supervision")).SelectMany(item => Children(item, "FlgNet")).Select(Read).FirstOrDefault();
            step.Interlock = Children(element, "Interlocks").SelectMany(item => Children(item, "Interlock")).SelectMany(item => Children(item, "FlgNet")).Select(Read).FirstOrDefault();
            graph.Steps.Add(step);
        }
        foreach (var element in Children(sequence, "Transitions").SelectMany(transitions => Children(transitions, "Transition"))) {
            graph.Transitions.Add(new TiaGraphTransition((int)element.Attribute("Number")!, (string)element.Attribute("Name")!, Read(Children(element, "FlgNet").Single())));
        }
        foreach (var branch in Children(sequence, "Branches").SelectMany(branches => Children(branches, "Branch"))) {
            graph.Branches[(int)branch.Attribute("Number")!] = (string)branch.Attribute("Type")!;
        }
        foreach (var connection in Children(sequence, "Connections").SelectMany(connections => Children(connections, "Connection"))) {
            graph.Connections.Add(new TiaGraphConnection(
                Node(Children(connection, "NodeFrom").Single()),
                Node(Children(connection, "NodeTo").Single()),
                Children(connection, "LinkType").FirstOrDefault()?.Value == "Jump"));
        }
        return graph;
    }

    private static TiaGraphNode Node(XElement end) {
        var node = end.Elements().Single();
        var number = (int?)node.Attribute("Number") ?? 0;
        return node.Name.LocalName switch {
            "StepRef" => new TiaGraphNode('S', number),
            "TransitionRef" => new TiaGraphNode('T', number),
            "BranchRef" => new TiaGraphNode('B', number),
            _ => new TiaGraphNode('E', 0),
        };
    }

    private static void Negate(TiaFlgPart part, XElement element) {
        foreach (var negated in Children(element, "Negated")) {
            part.Negated.Add((string)negated.Attribute("Name")!);
        }
    }

    private static IEnumerable<XElement> Children(XElement element, string name) => element.Elements().Where(child => child.Name.LocalName == name);

    /// <summary>An operand as a source names it: <c>#member.path</c>, <c>"Tag"</c>, a constant as written, or a label.</summary>
    private static string Operand(XElement access) {
        var scope = (string?)access.Attribute("Scope");
        if (Children(access, "Constant").FirstOrDefault() is { } constant) {
            return Children(constant, "ConstantValue").FirstOrDefault()?.Value ?? "?";
        }
        if (Children(access, "Label").FirstOrDefault() is { } label) {
            return (string)label.Attribute("Name")!;
        }
        var components = Children(access, "Symbol").SelectMany(symbol => Children(symbol, "Component")).Select(component => (string)component.Attribute("Name")!).ToList();
        return Path(scope, components);
    }

    private static string Instance(XElement instance) =>
        Path((string?)instance.Attribute("Scope"), [.. Children(instance, "Component").Select(component => (string)component.Attribute("Name")!)]);

    private static string Path(string? scope, List<string> components) {
        if (components.Count == 0) {
            return "?";
        }
        var rest = string.Concat(components.Skip(1).Select(segment => "." + Quote(segment)));
        return scope == "GlobalVariable" ? "\"" + components[0] + "\"" + rest : "#" + Quote(components[0]) + rest;
    }

    private static string Quote(string segment) =>
        segment.Length != 0 && !char.IsAsciiDigit(segment[0]) && segment.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? segment : "\"" + segment + "\"";
}
