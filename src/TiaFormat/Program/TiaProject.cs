using System.Collections.Concurrent;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>Something a project lists and can show as text: a block or a tag table.</summary>
public sealed class TiaItem {
    /// <summary>The kind of a PLC's OPC UA report, which is shown like an item but is no part of the program.</summary>
    public const string OpcUaReportKind = "OPC UA";

    /// <summary>The kind of a PLC's table of program alarms (spec 0014).</summary>
    public const string AlarmsKind = "Alarms";

    /// <summary>The kind of a PLC's table of text lists (spec 0014).</summary>
    public const string TextListsKind = "Text lists";

    internal TiaItem(string name, string kind, long? number, string? language, bool isClosed, string scope, bool isProject, TiaBlock? block, TiaTagTable? table) {
        Name = name;
        Kind = kind;
        Number = number;
        Language = language;
        IsClosed = isClosed;
        Scope = scope;
        IsProject = isProject;
        Block = block;
        Table = table;
    }

    public string Name { get; }

    /// <summary>FB, FC, OB, DB, UDT, Tags and so on.</summary>
    public string Kind { get; }

    public long? Number { get; }

    public string? Language { get; }

    /// <summary>Know-how protected: its source stays closed.</summary>
    public bool IsClosed { get; }

    /// <summary>Where a name means this item: its PLC, within the project or a master copy.</summary>
    public string Scope { get; }

    /// <summary>In the project itself, not in a library's master copy.</summary>
    public bool IsProject { get; }

    public TiaBlock? Block { get; }

    public TiaTagTable? Table { get; }

    /// <summary>A PLC's OPC UA report: not searched, referenced, counted or exported.</summary>
    public bool IsReport => Kind == OpcUaReportKind;

    /// <summary>
    /// An OB of the project itself where the program's cycle starts: its stored event class is <c>ProgramCycle</c>, or,
    /// if it stores none, its number is 1. An OB in a master copy is not the project's program.
    /// </summary>
    public bool IsProgramCycle => Kind == "OB" && IsProject && (Block?.EventClass is { } eventClass ? eventClass == "ProgramCycle" : Number == 1);

    /// <summary>
    /// Whether a filtered tree shows this item: its name holds the text, whatever its case, and its kind is one of those
    /// chosen. An empty text, or no kind chosen, leaves everything in.
    /// </summary>
    public bool Matches(string name, IReadOnlyCollection<string> kinds) =>
        (kinds.Count == 0 || kinds.Contains(Kind)) && (name.Length == 0 || Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>For a report, the PLC it is about; else null.</summary>
    public string? ReportPlc { get; internal init; }

    /// <summary>For a table of alarms or text lists, the PLC it lists; else null. Searched, but no block to export or refer to.</summary>
    public string? TablePlc { get; internal init; }

    /// <summary>A PLC's table of alarms or text lists.</summary>
    public bool IsTable => TablePlc is not null;
}

/// <summary>A node of a project's tree: a folder, or an item with no children.</summary>
public sealed class TiaTreeNode(string name, TiaItem? item = null) {
    public string Name { get; } = name;

    public TiaItem? Item { get; } = item;

    public List<TiaTreeNode> Children { get; } = [];
}

/// <summary>How one item refers to another.</summary>
public enum TiaReferenceKind : byte {
    /// <summary>Calls it, directly or through an instance.</summary>
    Call,

    /// <summary>Names it otherwise: reads or writes it, or has it as a type.</summary>
    Use,
}

/// <summary>One line of one item that refers to another item. Line counts from 0.</summary>
public readonly record struct TiaReference(TiaItem From, TiaItem To, TiaReferenceKind Kind, int Line, string Text);

/// <summary>A place in an item. Line counts from 0.</summary>
public readonly record struct TiaLocation(TiaItem Item, int Line);

/// <summary>One line that matched a search. Line counts from 0.</summary>
public readonly record struct TiaSearchResult(TiaItem Item, int Line, string Text);

/// <summary>
/// An opened archive as a viewer needs it: a tree of what is in it, each item's text, who refers to what, and search.
/// References come from source text only, so a closed block's calls are not among them. Safe for parallel reads.
/// </summary>
public sealed class TiaProject {
    /// <summary>The order kinds are offered in: the blocks as TIA lists them, then tag tables and the OPC UA report.</summary>
    private static readonly string[] KindOrder = ["OB", "FB", "FC", "DB", "UDT", "Tags", TiaItem.AlarmsKind, TiaItem.TextListsKind, TiaItem.OpcUaReportKind];

    private readonly ConcurrentDictionary<TiaItem, TiaDocument> _documents = new();
    private readonly ConcurrentDictionary<TiaItem, TiaOpcUaPlc> _opcUa = new();
    private readonly ConcurrentDictionary<TiaItem, TiaOpcUaNodeList> _opcUaNodes = new();
    private readonly Dictionary<(string Scope, string Name), TiaLocation> _names = [];
    private readonly Dictionary<TiaItem, List<TiaReference>> _to = [];
    private readonly Dictionary<TiaItem, List<TiaReference>> _from = [];
    private readonly Dictionary<string, int> _closed = [];
    private bool _indexed;
    private TiaAlarms? _alarms;

    private TiaProject(string path, TiaProgram program) {
        Path = path;
        Program = program;
        var root = new TiaTreeNode(System.IO.Path.GetFileNameWithoutExtension(path));
        Root = root;
        BuildTree(root);
        Kinds = [.. Items.Concat(Reports).Select(item => item.Kind).Distinct()
            .OrderBy(kind => Array.IndexOf(KindOrder, kind) is var at and >= 0 ? at : KindOrder.Length)
            .ThenBy(kind => kind, StringComparer.Ordinal)];
    }

    public string Path { get; }

    public TiaProgram Program { get; }

    public TiaTreeNode Root { get; }

    /// <summary>The blocks and tag tables: what is searched, referenced and exported.</summary>
    public List<TiaItem> Items { get; } = [];

    /// <summary>One OPC UA report per PLC of the project, in the tree beside the PLC's folders.</summary>
    public List<TiaItem> Reports { get; } = [];

    /// <summary>The kinds of the items and reports in the tree, each once: OB, FB, FC, DB, UDT, Tags, OPC UA, then others by name.</summary>
    public IReadOnlyList<string> Kinds { get; }

    /// <summary>
    /// Opens a <c>.zap*</c> archive, a project or library file or folder, or a <c>PEData.plf</c>, with its titles,
    /// comments and alarm texts in <paramref name="language"/>, or in the editing language if none is given (spec 0014).
    /// Throws <see cref="TiaLanguageException"/> for a language that is not a project language.
    /// </summary>
    public static TiaProject Open(string path, string? language = null) => new(path, new TiaProgram(TiaDatabase.Open(path), language));

    /// <summary>The item's text, made once, when it is first asked for.</summary>
    public TiaDocument Document(TiaItem item) => _documents.GetOrAdd(item, static (key, project) =>
        key.ReportPlc is not null ? project.Report(key)
        : key.TablePlc is { } plc ? project.TextTable(key.Kind, plc)
        : key.Table is { } table ? TiaDocumentBuilder.Build(table)
        : TiaDocumentBuilder.Build(project.Program, key.Block!), this);

    /// <summary>A report's OPC UA configuration of its PLC, read once, when it is first asked for; null for any other item.</summary>
    public TiaOpcUaPlc? OpcUa(TiaItem item) => item.ReportPlc is null ? null : _opcUa.GetOrAdd(item, static (key, program) =>
        TiaOpcUa.Read(program, key.ReportPlc).Plcs.SingleOrDefault() ?? new TiaOpcUaPlc(key.ReportPlc!, null, null, [], []), Program);

    /// <summary>The nodes of a report's PLC (spec 0006), derived once, when first asked for; null for any other item.</summary>
    public TiaOpcUaNodeList? OpcUaNodes(TiaItem item) => item.ReportPlc is null ? null : _opcUaNodes.GetOrAdd(item, static (key, program) =>
        TiaOpcUaNodes.Read(program, key.ReportPlc).SingleOrDefault() ?? new TiaOpcUaNodeList(key.ReportPlc!, []), Program);

    /// <summary>The report <c>tiadump opcua --plc</c> writes for a report's PLC, as plain text.</summary>
    private TiaDocument Report(TiaItem item) {
        var output = new StringWriter { NewLine = "\n" };
        TiaOpcUaWriter.WriteMarkdown(new TiaOpcUaReport([OpcUa(item)!]), TiaOpcUaWriter.Title(Path), output);
        return new TiaDocument(output.ToString(), []);
    }

    /// <summary>A PLC's alarms or text lists as a table in the program's language.</summary>
    private TiaDocument TextTable(string kind, string plc) {
        var output = new StringWriter { NewLine = "\n" };
        if (kind == TiaItem.AlarmsKind) {
            TiaTexts.ShowAlarms([.. Alarms.Alarms().Where(alarm => alarm.Plc == plc)], Program.TextPreference, output);
        } else {
            TiaTexts.ShowTextLists([.. Alarms.TextLists().Where(list => list.Plc == plc)], Program.TextPreference, output);
        }
        return new TiaDocument(output.ToString(), []);
    }

    /// <summary>The program's alarms, alarm classes and text lists.</summary>
    public TiaAlarms Alarms => _alarms ??= new TiaAlarms(Program);

    /// <summary>Makes all texts and the references between them. Without it references are empty; searching makes the texts itself.</summary>
    public void BuildIndex() {
        if (_indexed) {
            return;
        }
        Parallel.ForEach(Items, item => Document(item));
        foreach (var item in Items) {
            if (item.Table is not null) {
                AddTagNames(item);
            } else if (!item.IsTable && !_names.ContainsKey((item.Scope, item.Name))) {
                _names[(item.Scope, item.Name)] = new TiaLocation(item, 0);
            }
            if (item.IsClosed) {
                _closed[item.Scope] = _closed.GetValueOrDefault(item.Scope) + 1;
            }
        }
        foreach (var item in Items) {
            if (item.Block is not null && !item.IsClosed) {
                AddReferences(item);
            }
        }
        _indexed = true;
    }

    /// <summary>What a name in this item means, or null: a block, or a tag or constant at its line.</summary>
    public TiaLocation? Resolve(TiaItem from, string name) =>
        _names.TryGetValue((from.Scope, name), out var location) ? location : null;

    /// <summary>Every open line that refers to this item.</summary>
    public IReadOnlyList<TiaReference> ReferencesTo(TiaItem item) => _to.TryGetValue(item, out var list) ? list : [];

    /// <summary>Every line of this item that refers to another.</summary>
    public IReadOnlyList<TiaReference> ReferencesFrom(TiaItem item) => _from.TryGetValue(item, out var list) ? list : [];

    /// <summary>How many blocks around this item are closed: what they refer to is not known.</summary>
    public int ClosedBlocksAround(TiaItem item) => item.IsReport ? 0 : _closed.GetValueOrDefault(item.Scope);

    /// <summary>Lines holding the text, whatever its case, the project's own before a library's.</summary>
    public IReadOnlyList<TiaSearchResult> Search(string text, int limit = 2000) {
        var results = new List<TiaSearchResult>();
        if (text.Length == 0) {
            return results;
        }
        foreach (var item in Items.OrderBy(item => item.IsProject ? 0 : 1)) {
            var document = Document(item);
            var at = document.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            var lastLine = -1;
            while (at >= 0) {
                var line = document.LineOf(at);
                if (line != lastLine) {
                    results.Add(new TiaSearchResult(item, line, document.LineText(line).Trim()));
                    lastLine = line;
                    if (results.Count >= limit) {
                        return results;
                    }
                }
                at = document.Text.IndexOf(text, at + text.Length, StringComparison.OrdinalIgnoreCase);
            }
        }
        return results;
    }

    private void AddTagNames(TiaItem table) {
        var document = Document(table);
        for (var line = 0; line < document.LineCount; line++) {
            var text = document.LineText(line);
            var close = text.StartsWith('"') ? text.IndexOf('"', 1) : -1;
            if (close > 0) {
                _names.TryAdd((table.Scope, text[1..close]), new TiaLocation(table, line));
            }
        }
    }

    private void AddReferences(TiaItem item) {
        var document = Document(item);
        Dictionary<string, string>? instances = null;
        var seen = new HashSet<(TiaItem, int, TiaReferenceKind)>();
        foreach (var token in document.Tokens) {
            string? target = token.Target;
            if (token.IsCall && token.Kind == TiaTokenKind.Local) {
                // A call through a member: what it calls is the member's type.
                instances ??= InstanceTypes(item);
                target = instances.GetValueOrDefault(document.Text.Substring(token.Start + 1, token.Length - 1).Trim('"'));
            }
            if (target is null || target == item.Name || Resolve(item, target) is not { } location) {
                continue;
            }
            var kind = token.IsCall ? TiaReferenceKind.Call : TiaReferenceKind.Use;
            var line = document.LineOf(token.Start);
            Add(item, location.Item, kind, line, document, seen);
            // A call through an instance data block calls that block's function block too.
            if (token.IsCall && location.Item.Block is { } called && Program.GeneratedFrom(called) is { } type && Resolve(item, type) is { } callee) {
                Add(item, callee.Item, TiaReferenceKind.Call, line, document, seen);
            }
        }
    }

    private void Add(TiaItem from, TiaItem to, TiaReferenceKind kind, int line, TiaDocument document, HashSet<(TiaItem, int, TiaReferenceKind)> seen) {
        if (!seen.Add((to, line, kind))) {
            return;
        }
        var reference = new TiaReference(from, to, kind, line, document.LineText(line).Trim());
        (_to.TryGetValue(to, out var into) ? into : _to[to] = []).Add(reference);
        (_from.TryGetValue(from, out var outOf) ? outOf : _from[from] = []).Add(reference);
    }

    private Dictionary<string, string> InstanceTypes(TiaItem item) {
        var types = new Dictionary<string, string>();
        foreach (var member in Program.ReadInterface(item.Block!)) {
            var type = member.Type[(member.Type.LastIndexOf(" of ", StringComparison.Ordinal) + 1)..];
            if (type.StartsWith("of ", StringComparison.Ordinal)) {
                type = type[3..];
            }
            types[member.Name] = type.Trim('"');
        }
        return types;
    }

    private void BuildTree(TiaTreeNode root) {
        var plcs = new Dictionary<string, TiaTreeNode>();
        var projectPlcs = new Dictionary<string, string>();
        var libraries = new TiaTreeNode("Libraries and system");
        foreach (var block in Program.Blocks) {
            if (block.IsStub) {
                continue;
            }
            var item = ItemOf(block);
            Items.Add(item);
            var holder = HolderOf(block.Frame, root, libraries, plcs);
            NoteProjectPlc(block.Frame, projectPlcs);
            Place(holder, Program.FolderPath(block.Frame), item);
        }
        foreach (var table in Program.ReadTagTables()) {
            if (table.Tags.Count == 0) {
                continue;
            }
            var environment = Program.Environment(table.Frame);
            var item = new TiaItem(table.Name, "Tags", null, null, false, ScopeOf(table.Frame), environment.Kind == "ProjectData", null, table);
            Items.Add(item);
            Place(HolderOf(table.Frame, root, libraries, plcs), Program.FolderPath(table.Frame), item);
            NoteProjectPlc(table.Frame, projectPlcs);
        }
        var alarmPlcs = Alarms.Alarms().Select(alarm => alarm.Plc).ToHashSet(StringComparer.Ordinal);
        var listPlcs = Alarms.TextLists().Select(list => list.Plc).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, holder) in plcs) {
            if (projectPlcs.TryGetValue(key, out var plc)) {
                var report = new TiaItem(TiaItem.OpcUaReportKind, TiaItem.OpcUaReportKind, null, null, false, key, true, null, null) { ReportPlc = plc };
                Reports.Add(report);
                holder.Children.Add(new TiaTreeNode(report.Name, report));
                // Spec 0014: a PLC's alarms and text lists, each a table in the chosen language.
                foreach (var kind in new[] { TiaItem.AlarmsKind, TiaItem.TextListsKind }.Where(kind => (kind == TiaItem.AlarmsKind ? alarmPlcs : listPlcs).Contains(plc))) {
                    var table = new TiaItem(kind, kind, null, null, false, key, true, null, null) { TablePlc = plc };
                    Items.Add(table);
                    holder.Children.Add(new TiaTreeNode(table.Name, table));
                }
            }
        }
        Sort(root);
        Sort(libraries);
        if (libraries.Children.Count != 0) {
            // The project comes first: most of this tree is the system's own.
            root.Children.Add(libraries);
        }
    }

    /// <summary>Remembers a PLC of the project itself by its holder's key: it gets an OPC UA report.</summary>
    private void NoteProjectPlc(in TiaFrame frame, Dictionary<string, string> projectPlcs) {
        if (Program.Environment(frame).Kind == "ProjectData" && Program.PlcName(frame) is { } plc) {
            projectPlcs.TryAdd(ScopeOf(frame), plc);
        }
    }

    private TiaItem ItemOf(TiaBlock block) {
        var environment = Program.Environment(block.Frame);
        return new TiaItem(block.Name, block.Kind, block.Number, block.Language, block.IsKnowHowProtected, ScopeOf(block.Frame), environment.Kind == "ProjectData", block, null);
    }

    private string ScopeOf(in TiaFrame frame) => Program.Environment(frame).Id + "/" + Program.PlcName(frame);

    private TiaTreeNode HolderOf(in TiaFrame frame, TiaTreeNode root, TiaTreeNode libraries, Dictionary<string, TiaTreeNode> plcs) {
        var environment = Program.Environment(frame);
        var plc = Program.PlcName(frame) ?? "No PLC";
        var key = ScopeOf(frame);
        if (plcs.TryGetValue(key, out var holder)) {
            return holder;
        }
        if (environment.Kind == "ProjectData") {
            holder = new TiaTreeNode(plc);
            root.Children.Add(holder);
        } else {
            holder = new TiaTreeNode(environment.Name is { Length: > 0 } name ? name : environment.Kind + " " + environment.Id);
            libraries.Children.Add(holder);
        }
        plcs[key] = holder;
        return holder;
    }

    private static void Place(TiaTreeNode holder, string folder, TiaItem item) {
        var node = holder;
        foreach (var part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            var label = FolderLabel(part);
            var child = node.Children.Find(candidate => candidate.Item is null && candidate.Name == label);
            if (child is null) {
                child = new TiaTreeNode(label);
                node.Children.Add(child);
            }
            node = child;
        }
        node.Children.Add(new TiaTreeNode(item.Name, item));
    }

    /// <summary>TIA stores its own folders under technical names.</summary>
    private static string FolderLabel(string name) => name switch {
        "ProgramBlocksFolder" => "Program blocks",
        "ControllerDataTypeFolder" => "PLC data types",
        "SystemDataTypeFolder" => "System data types",
        "ControllerTagsFolder" or "PLCTagsFolder" => "PLC tags",
        "SystemBlocksFolder" => "System blocks",
        "SafetyResourcesFolder" => "Safety resources",
        "SafetyCompilerBlocksFolder" => "Safety compiler blocks",
        _ => name,
    };

    private static int Rank(TiaTreeNode node) => node.Item is null ? 1 : node.Item.IsReport || node.Item.IsTable ? 0 : 2;

    private static void Sort(TiaTreeNode node) {
        // A PLC's OPC UA report and its tables of alarms and text lists first, then folders, then items.
        node.Children.Sort((a, b) => {
            var folders = Rank(a).CompareTo(Rank(b));
            return folders != 0 ? folders : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var child in node.Children) {
            Sort(child);
        }
    }
}
