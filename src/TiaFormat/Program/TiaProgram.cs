using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using TiaFormat.Schema;
using TiaFormat.Storage;
using TiaFormat.Xml;

namespace TiaFormat.Program;

/// <summary>The PLC layer of a database: blocks, their interfaces and source, tag tables. Safe for parallel reads.</summary>
public sealed class TiaProgram {
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private const string General = "Siemens.Simatic.Lang.Model.Blocks.IGeneralBlockSourceData";
    private const string PlcHeader = "Siemens.Simatic.Lang.Model.Blocks.IPlcHeaderData";
    private const string CompileUnit = "Siemens.Simatic.Lang.Model.Blocks.ICompileUnitData";
    private const string InterfacePart = "Siemens.Simatic.Lang.Model.Structures.IInterfacePartData";
    private const string IdentPart = "Siemens.Simatic.Lang.Model.Idents.IIdentPartData";
    private const string DataBlockLoad = "Siemens.Simatic.Lang.Model.Blocks.ILoadablePlusDataBlockData";
    private const string UserTypeName = "Siemens.Simatic.Lang.Model.Blocks.UserTypeData";
    private const string InterfaceLibrary = "Siemens.Simatic.Lang.Model.Structures.IInterfaceLibraryData";
    private const string StructureItem = "Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem";

    private readonly TiaDatabase _db;
    private readonly TiaAttributeHandle _name, _comment, _subtype, _protected, _blockType, _number, _language;
    private readonly TiaAttributeHandle _unitData, _unitRef, _unitLanguage, _partPayload, _partKind, _identPayload, _text, _image, _libraryVersion;
    private readonly TiaAttributeHandle _tagAddress, _tagType, _tagValue, _tagAccessible, _tagVisible, _tagReadOnly;
    private readonly TiaAttributeHandle _author, _family, _headerName, _version, _optimized;
    private readonly TiaRelationHandle _sources, _currentInterface, _usedParts, _comments, _blockComment, _unitComment, _identContainer, _identParts, _folder, _target, _environment, _loadables, _generatedFrom, _isGeneratedFrom, _tagContent;
    private readonly Lazy<Dictionary<int, string>> _typeCodes;
    private readonly IReadOnlyList<int> _preference;

    /// <summary>
    /// Reads the program with its titles and comments in <paramref name="language"/>, a project language such as de-DE,
    /// or in the editing language if none is given (spec 0014). Throws <see cref="TiaLanguageException"/> for a language
    /// that is not a project language.
    /// </summary>
    public TiaProgram(TiaDatabase database, string? language = null) {
        _db = database;
        Languages = TiaLanguages.Read(database);
        Language = language is null ? Languages.Editing : Languages.Find(language);
        // A file that names no languages keeps the old order, English first.
        _preference = Languages.All.Count == 0 ? [1033] : Languages.Preference(Language);
        var s = database.Schema;
        _name = s.Attribute(Core, "Name");
        _comment = s.Attribute(Core, "Comment");
        _subtype = s.Attribute(Core, "Subtype");
        _protected = s.Attribute(Core, "IsKnowHowProtected");
        _blockType = s.Attribute(General, "BlockType");
        _number = s.Attribute(General, "Number");
        _language = s.Attribute(General, "BlockLanguage");
        _optimized = s.Attribute(General, "OnlySymbolicAccess");
        _author = s.Attribute(PlcHeader, "HeaderAuthor");
        _family = s.Attribute(PlcHeader, "HeaderFamily");
        _headerName = s.Attribute(PlcHeader, "HeaderName");
        _version = s.Attribute(PlcHeader, "HeaderVersion");
        _unitData = s.Attribute(CompileUnit, "Data");
        _unitRef = s.Attribute(CompileUnit, "RefID");
        _unitLanguage = s.Attribute(CompileUnit, "ProgrammingLanguage");
        _partPayload = s.Attribute(InterfacePart, "Payload");
        _partKind = s.Attribute(InterfacePart, "Kind");
        _identPayload = s.Attribute(IdentPart, "PayLoad");
        _text = s.Attribute("Siemens.Automation.ObjectFrame.ICoreTextRepository", "Text");
        _image = s.Attribute(DataBlockLoad, "VolatileBuffer");
        _libraryVersion = s.Attribute(InterfaceLibrary, "Version");
        _tagAddress = s.Attribute("Siemens.Automation.DomainModel.ITagAddress", "LogicalAddress");
        _tagType = s.Attribute(StructureItem, "DisplayTypeName");
        _tagValue = s.Attribute("Siemens.Automation.DomainServices.CommonTypeSystem.IDefaultStrategyData", "DefaultValue");
        _tagAccessible = s.Attribute(StructureItem, "HmiStructureTypeRelevant");
        _tagVisible = s.Attribute(StructureItem, "HmiVisible");
        _tagReadOnly = s.Attribute(StructureItem, "HmiReadOnly");
        _sources = s.Relation("Sources");
        _currentInterface = s.Relation("CurrentInterface");
        _usedParts = s.Relation("UsedParts");
        _comments = s.Relation("InterfaceComments");
        _blockComment = s.Relation("BlockComment");
        _unitComment = s.Relation("CompileUnitComment");
        _identContainer = s.Relation("CoreObject2IdentContainer");
        _identParts = s.Relation("IdentParts");
        _folder = s.Relation("AggregatingFolder");
        _target = s.Relation("Target");
        _environment = s.Relation("Environment");
        _loadables = s.Relation("LoadableBinaries");
        _generatedFrom = s.Relation("GeneratedFromMaster");
        _isGeneratedFrom = s.Relation("IsGeneratedFrom");
        _tagContent = s.Relation("Content");
        _typeCodes = new Lazy<Dictionary<int, string>>(HarvestTypeCodes);
        Blocks = ReadBlocks();
    }

    public TiaDatabase Database => _db;

    /// <summary>The project languages, and which are the editing and the reference language.</summary>
    public TiaLanguages Languages { get; }

    /// <summary>The language titles and comments are read in; null if the file names no languages.</summary>
    public TiaLanguage? Language { get; }

    /// <summary>The LCIDs a text is looked for in, in order, before any other language that has text (FR-004).</summary>
    public IReadOnlyList<int> TextPreference => _preference;

    /// <summary>Every block object, stubs and protected blocks included.</summary>
    public IReadOnlyList<TiaBlock> Blocks { get; }

    /// <summary>The name of the PLC a block belongs to, or null.</summary>
    public string? PlcName(in TiaFrame frame) => _db.TryGetTarget(frame, _target, out var plc) ? _db.ReadString(plc, _name) : null;

    /// <summary>What holds the block: its type's short name and its name, such as a project or a library master copy.</summary>
    public (string Kind, string? Name, ulong Id) Environment(in TiaFrame frame) {
        if (!_db.TryGetTarget(frame, _environment, out var environment)) {
            return ("", null, 0);
        }
        var type = _db.Schema.FindObjectType(environment.TypeId)?.FullName ?? "";
        return (type[(type.LastIndexOf('.') + 1)..], _db.ReadString(environment, _name), environment.ObjectId);
    }

    /// <summary>The folder path from the top, separated by '/'; empty if the object is in no folder.</summary>
    public string FolderPath(in TiaFrame frame) {
        var names = new List<string>();
        var current = frame;
        for (var i = 0; i < 64 && _db.TryGetTarget(current, _folder, out var parent); i++) {
            names.Add(_db.ReadString(parent, _name) ?? "");
            current = parent;
        }
        names.Reverse();
        return string.Join('/', names);
    }

    /// <summary>The block comment, or null.</summary>
    public string? BlockComment(TiaBlock block) =>
        _db.TryGetTarget(block.Frame, _blockComment, out var text) ? _db.ReadCoreText(text, _text, _preference) : null;

    /// <summary>The block's header fields, as a source file's AUTHOR, FAMILY, NAME and VERSION show them.</summary>
    public TiaBlockHeader Header(TiaBlock block) => new(
        _db.ReadString(block.Frame, _author),
        _db.ReadString(block.Frame, _family),
        _db.ReadString(block.Frame, _headerName),
        _db.ReadString(block.Frame, _version),
        _db.ReadBoolean(block.Frame, _optimized));

    /// <summary>The name of the UDT or FB a data block was generated from, or null.</summary>
    public string? GeneratedFrom(TiaBlock block) =>
        _db.TryGetTarget(block.Frame, _generatedFrom, out var master) || _db.TryGetTarget(block.Frame, _isGeneratedFrom, out master)
            ? _db.ReadString(master, _name)
            : null;

    /// <summary>
    /// For a data block of a system type, such as an IEC_TIMER that a TON uses, the version that type states about itself,
    /// as its source writes it in <c>LibVersion</c>; null for any other block. The type's root is the block's first used part.
    /// </summary>
    public string? InstructionVersion(TiaBlock block) {
        if (!block.Kind.StartsWith("DB", StringComparison.Ordinal) || !_db.TryGetTarget(block.Frame, _currentInterface, out var root)) {
            return null;
        }
        var parts = PositionalParts(root);
        return parts.Count == 0 ? null : LibraryVersion(0, new InterfaceContext { Parts = parts });
    }

    /// <summary>A data block's own settings: what a source file writes as NON_RETAIN and as block attributes.</summary>
    public TiaDataBlockSettings DataBlockSettings(TiaBlock block) => new(
        _db.ReadExpandoBoolean(block.Frame, "NonRetain") ?? true,
        _db.ReadExpandoBoolean(block.Frame, "DBAccessibleFromOPCUA") ?? true,
        _db.ReadExpandoBoolean(block.Frame, "DBAccessibleFromWebserver") ?? true);

    /// <summary>
    /// The start values set in a data block itself, in member order, named as a source file names them:
    /// <c>Config.Limits[1,2].Max</c>, following members into the types they have. A value whose path no member explains
    /// comes last, with its stored path.
    /// </summary>
    public IReadOnlyList<TiaSetValue> ReadSetValues(TiaBlock block) {
        if (!_db.TryGetTarget(block.Frame, _currentInterface, out var root)) {
            return [];
        }
        var values = new Dictionary<string, string>();
        using (var payload = _db.ReadBlob(root, _partPayload)) {
            CollectValues(payload.Span, values);
        }
        foreach (var key in PositionalParts(root)) {
            if (_db.TryGetFrame(key, out var part) && _db.ReadString(part, _partKind) == "Values") {
                using var payload = _db.ReadBlob(part, _partPayload);
                CollectValues(payload.Span, values);
            }
        }
        var names = new Dictionary<string, (int Order, string Name)>();
        Index(ParseRoot(root, new InterfaceContext { Parts = PositionalParts(root) }), names);
        var known = new List<(List<int> Order, TiaSetValue Value)>();
        var unknown = new List<TiaSetValue>();
        foreach (var (path, value) in values) {
            if (TryName(path, names, out var name, out var order)) {
                known.Add((order, new TiaSetValue(name, value, true)));
            } else {
                unknown.Add(new TiaSetValue(path, value, false));
            }
        }
        known.Sort((a, b) => CompareOrder(a.Order, b.Order));
        return [.. known.SelectMany(entry => WithDateParts(entry.Value)), .. unknown];
    }

    /// <summary>A source file lists a <c>DTL</c>'s parts before the value itself. None of them is stored.</summary>
    private static IEnumerable<TiaSetValue> WithDateParts(TiaSetValue value) {
        const string Prefix = "DTL#";
        if (value.Value.StartsWith(Prefix, StringComparison.Ordinal)
            && DateTime.TryParseExact(value.Value[Prefix.Length..], ["yyyy-MM-dd-HH:mm:ss", "yyyy-MM-dd-HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
            var nanoseconds = date.Ticks % TimeSpan.TicksPerSecond * 100;
            foreach (var (part, number) in new (string, long)[] {
                ("YEAR", date.Year), ("MONTH", date.Month), ("DAY", date.Day), ("WEEKDAY", (int)date.DayOfWeek + 1),
                ("HOUR", date.Hour), ("MINUTE", date.Minute), ("SECOND", date.Second), ("NANOSECOND", nanoseconds),
            }) {
                yield return new TiaSetValue(value.Name + "." + part, number.ToString(CultureInfo.InvariantCulture), true);
            }
        }
        yield return value;
    }

    private static void Index(List<TiaMemberNode> members, Dictionary<string, (int Order, string Name)> names) {
        foreach (var member in members) {
            names[member.Path] = (names.Count, TiaIdentifiers.Quoted(member.Name));
            Index(member.Inner, names);
        }
    }

    /// <summary>A stored path is ids with optional indexes, <c>53[0]:51[1,2]</c>; it becomes <c>Name[0].Other[1,2]</c>.</summary>
    private static bool TryName(string path, Dictionary<string, (int Order, string Name)> names, out string name, out List<int> order) {
        name = "";
        order = [];
        var prefix = "";
        var result = new StringBuilder();
        foreach (var segment in path.Split(':')) {
            var bracket = segment.IndexOf('[');
            var id = bracket < 0 ? segment : segment[..bracket];
            prefix = prefix.Length == 0 ? id : prefix + ":" + id;
            if (!names.TryGetValue(prefix, out var member)) {
                return false;
            }
            order.Add(member.Order);
            if (result.Length != 0) {
                result.Append('.');
            }
            result.Append(member.Name);
            if (bracket >= 0) {
                result.Append(segment.AsSpan(bracket));
                foreach (var index in segment[(bracket + 1)..].TrimEnd(']').Split(',')) {
                    order.Add(int.TryParse(index, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0);
                }
            }
        }
        name = result.ToString();
        return true;
    }

    private static int CompareOrder(List<int> a, List<int> b) {
        for (var i = 0; i < a.Count && i < b.Count; i++) {
            var compared = a[i].CompareTo(b[i]);
            if (compared != 0) {
                return compared;
            }
        }
        return a.Count.CompareTo(b.Count);
    }

    /// <summary>The compile units in network order, with their form: SCLSource, FlgNet, EncryptedData, empty or other.</summary>
    public List<TiaNetwork> Networks(TiaBlock block) {
        var units = CompileUnits(block);
        var result = new List<TiaNetwork>(units.Count);
        foreach (var unit in units) {
            using var data = _db.ReadBlob(unit, _unitData);
            result.Add(new TiaNetwork(
                (int)(_db.ReadInteger(unit, _unitRef) ?? 0),
                _db.ReadString(unit, _unitLanguage) ?? "",
                _db.ReadCoreText(unit, _comment, _preference),
                FormOf(data.Span),
                _db.TryGetTarget(unit, _unitComment, out var comment) ? _db.ReadCoreText(comment, _text, _preference) : null));
        }
        return result;
    }

    /// <summary>
    /// Writes one compile unit's source. Protected source is never opened: it writes a marker and returns false.
    /// Returns the count of SCL tags without a rule through <paramref name="unknownTags"/>.
    /// </summary>
    public void WriteNetworkSource(TiaBlock block, int index, PooledByteWriter output, out int unknownTags, List<TiaToken>? tokens = null) {
        unknownTags = 0;
        var units = CompileUnits(block);
        using var data = _db.ReadBlob(units[index], _unitData);
        var form = FormOf(data.Span);
        switch (form) {
            case "":
                output.Write("[no source stored]"u8);
                break;
            case "EncryptedData":
                output.Write(TiaSourceWriter.ProtectedMarker);
                break;
            case "SCLSource":
                unknownTags = SclRenderer.Render(data.Span, output, tokens);
                break;
            case "FlgNet":
                FlgNetRenderer.Render(data.Span, ReadIdents(block), output);
                break;
            case "Statements":
                StlRenderer.Render(data.Span, ReadIdents(block), output);
                break;
            default:
                output.Write("[unknown source form, raw follows]\n"u8);
                output.Write(data.Span);
                break;
        }
    }

    /// <summary>A GRAPH block's sequence, read from all its compile units together (docs/FORMAT.md, section 13).</summary>
    public TiaGraph ReadGraph(TiaBlock block) {
        var units = CompileUnits(block);
        var data = new List<ReadOnlyMemory<byte>>(units.Count);
        foreach (var unit in units) {
            using var blob = _db.ReadBlob(unit, _unitData);
            data.Add(blob.Span.ToArray());
        }
        return TiaGraph.Read(data, ReadIdents(block));
    }

    /// <summary>Writes a compile unit exactly as stored, unless it is encrypted.</summary>
    /// <summary>
    /// Every title and comment of a block in every language stored (spec 0014): its title and comment, each network's
    /// title and comment, and each member's comment by its name, in interface order. Places with no text in any language are left out.
    /// </summary>
    public IReadOnlyList<TiaPlacedText> BlockTexts(TiaBlock block) {
        var result = new List<TiaPlacedText>();
        Add("title", _db.ReadCoreTexts(block.Frame, _comment));
        Add("comment", _db.TryGetTarget(block.Frame, _blockComment, out var comment) ? _db.ReadCoreTexts(comment, _text) : []);
        foreach (var unit in CompileUnits(block)) {
            var number = (_db.ReadInteger(unit, _unitRef) ?? 0).ToString(CultureInfo.InvariantCulture);
            Add($"network {number} title", _db.ReadCoreTexts(unit, _comment));
            Add($"network {number} comment", _db.TryGetTarget(unit, _unitComment, out var unitComment) ? _db.ReadCoreTexts(unitComment, _text) : []);
        }
        if (_db.TryGetTarget(block.Frame, _comments, out var comments)) {
            // Comments are stored by the member's id path, 53:51; the place names it as a source does, Config.Max.
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            Name(ReadInterface(block), "");
            var stored = _db.ReadSimpleExpandoAllTexts(comments);
            foreach (var path in order) {
                if (stored.TryGetValue(path, out var texts)) {
                    Add("member " + names[path], texts);
                }
            }
            foreach (var (path, texts) in stored.Where(pair => !names.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
                Add("member " + path, texts);
            }

            void Name(IReadOnlyList<TiaMember> members, string prefix) {
                foreach (var member in members) {
                    names[member.Path] = prefix + member.Name;
                    order.Add(member.Path);
                    Name(member.Children, prefix + member.Name + ".");
                }
            }
        }
        return result;

        void Add(string place, IReadOnlyList<TiaText> texts) {
            if (texts.Any(text => text.Text.Length != 0)) {
                result.Add(new TiaPlacedText(place, texts));
            }
        }
    }

    /// <summary>Each tag's comment of a tag table in every language stored, by the tag's name; tags with none are left out.</summary>
    public IReadOnlyList<TiaPlacedText> TagTexts(TiaTagTable table) {
        var result = new List<TiaPlacedText>();
        foreach (var relation in _db.EnumerateRelations(table.Frame)) {
            if (_tagContent.Matches(relation.RelationId) && _db.TryGetFrame(relation.Target, out var tag)) {
                var texts = _db.ReadCoreTexts(tag, _comment);
                if (texts.Any(text => text.Text.Length != 0)) {
                    result.Add(new TiaPlacedText("tag " + (_db.ReadString(tag, _name) ?? ""), texts));
                }
            }
        }
        return result;
    }

    public void WriteRawNetwork(TiaBlock block, int index, PooledByteWriter output) {
        using var data = _db.ReadBlob(CompileUnits(block)[index], _unitData);
        if (FormOf(data.Span) != "EncryptedData") {
            output.Write(data.Span);
        }
    }

    /// <summary>The block's interface: sections flattened, each member carrying its section, structs as children.</summary>
    public IReadOnlyList<TiaMember> ReadInterface(TiaBlock block) {
        if (!_db.TryGetTarget(block.Frame, _currentInterface, out var root)) {
            return [];
        }
        var context = new InterfaceContext { Parts = PositionalParts(root), IsDataBlock = block.Kind.StartsWith("DB", StringComparison.Ordinal) };
        if (_db.TryGetTarget(block.Frame, _comments, out var comments)) {
            context.Comments = _db.ReadSimpleExpandoTexts(comments, _preference);
        }
        foreach (var part in context.Parts) {
            if (_db.TryGetFrame(part, out var frame) && _db.ReadString(frame, _partKind) == "Values") {
                using var payload = _db.ReadBlob(frame, _partPayload);
                CollectValues(payload.Span, context.Values);
            }
        }
        var members = ParseRoot(root, context);
        if (block.Kind.StartsWith("DB", StringComparison.Ordinal)) {
            ApplyImage(block, members, context);
        }
        var declared = new Dictionary<string, string>(context.Declared);
        if (!context.IsDataBlock) {
            // Only a data block's values differ from its declaration: anywhere else they are the declaration.
            foreach (var (path, value) in context.Values) {
                declared[path] = value;
            }
        }
        return Freeze(members, block.Kind is not ("FC" or "SFC" or "OB"), new TiaDeclaredValues(declared));
    }

    /// <summary>The block's identifier table, for naming operands.</summary>
    public TiaIdentTable ReadIdents(TiaBlock block) {
        var table = new TiaIdentTable();
        foreach (var relation in _db.EnumerateRelations(block.Frame)) {
            if (!_identContainer.Matches(relation.RelationId) || !_db.TryGetFrame(relation.Target, out var container)) {
                continue;
            }
            foreach (var part in _db.EnumerateRelations(container)) {
                if (_identParts.Matches(part.RelationId) && _db.TryGetFrame(part.Target, out var frame)) {
                    using var payload = _db.ReadBlob(frame, _identPayload);
                    table.Add(payload.Span);
                }
            }
        }
        table.Complete();
        return table;
    }

    /// <summary>All controller tag tables, with their tags and global constants.</summary>
    public List<TiaTagTable> ReadTagTables() {
        var type = _db.Schema.FindObjectType("Siemens.Automation.DomainModel.EAMTZTagTableData");
        var result = new List<TiaTagTable>();
        if (type is null) {
            return result;
        }
        foreach (ref readonly var frame in _db.Frames) {
            if (frame.TypeId != type.Id) {
                continue;
            }
            var tags = new List<TiaTag>();
            foreach (var relation in _db.EnumerateRelations(frame)) {
                if (_tagContent.Matches(relation.RelationId) && _db.TryGetFrame(relation.Target, out var tag)) {
                    var value = _db.ReadString(tag, _tagValue);
                    var accessible = value is null ? _db.ReadOptionalBoolean(tag, _tagAccessible) : null;
                    tags.Add(new TiaTag(
                        _db.ReadString(tag, _name) ?? "",
                        _db.ReadString(tag, _tagType),
                        _db.ReadString(tag, _tagAddress),
                        value,
                        _db.ReadCoreText(tag, _comment, _preference)) {
                        ExternalAccess = accessible switch {
                            null => null,
                            false => TiaExternalAccess.None,
                            true => _db.ReadBoolean(tag, _tagReadOnly) ? TiaExternalAccess.ReadOnly : TiaExternalAccess.ReadWrite,
                        },
                        ExternalVisible = accessible is null ? null : _db.ReadOptionalBoolean(tag, _tagVisible),
                    });
                }
            }
            result.Add(new TiaTagTable(frame, _db.ReadString(frame, _name) ?? "", tags));
        }
        return result;
    }

    private List<TiaBlock> ReadBlocks() {
        var code = _db.Schema.FindObjectType("Siemens.Simatic.Lang.Model.Blocks.CodeBlockData")?.Id;
        var data = _db.Schema.FindObjectType("Siemens.Simatic.Lang.Model.Blocks.DataBlockData")?.Id;
        var user = _db.Schema.FindObjectType("Siemens.Simatic.Lang.Model.Blocks.UserTypeData")?.Id;
        var result = new List<TiaBlock>();
        foreach (ref readonly var frame in _db.Frames) {
            if (frame.TypeId != code && frame.TypeId != data && frame.TypeId != user) {
                continue;
            }
            var subtype = _db.ReadString(frame, _subtype);
            var kind = _db.ReadString(frame, _blockType) ?? subtype ?? (frame.TypeId == user ? "UDT" : "?");
            kind = kind == "None" ? subtype ?? kind : kind;
            result.Add(new TiaBlock(
                frame,
                _db.ReadString(frame, _name) ?? "",
                kind,
                _db.ReadInteger(frame, _number),
                _db.ReadString(frame, _language),
                _db.ReadBoolean(frame, _protected),
                _db.ReadCoreText(frame, _comment, _preference)) {
                EventClass = kind == "OB" ? EventClassOf(subtype) : null,
            });
        }
        return result;
    }

    /// <summary>An OB's subtype is <c>OB.</c> and its event class, with <c>.Safety</c> after it on a fail-safe one.</summary>
    private static string? EventClassOf(string? subtype) {
        if (subtype is null || !subtype.StartsWith("OB.", StringComparison.Ordinal)) {
            return null;
        }
        var name = subtype[3..];
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        name = dot < 0 ? name : name[..dot];
        return name.Length == 0 ? null : name;
    }

    private List<TiaFrame> CompileUnits(TiaBlock block) {
        var unitType = _db.Schema.FindObjectType("Siemens.Simatic.Lang.Model.Blocks.CompileUnitData")?.Id;
        var units = new List<TiaFrame>();
        foreach (var relation in _db.EnumerateRelations(block.Frame)) {
            if (_sources.Matches(relation.RelationId) && relation.Target.TypeId == unitType && _db.TryGetFrame(relation.Target, out var unit)) {
                units.Add(unit);
            }
        }
        units.Sort((a, b) => (_db.ReadInteger(a, _unitRef) ?? 0).CompareTo(_db.ReadInteger(b, _unitRef) ?? 0));
        return units;
    }

    private static string FormOf(ReadOnlySpan<byte> data) {
        var head = data.StartsWith<byte>([0xEF, 0xBB, 0xBF]) ? data[3..] : data;
        var start = 0;
        while (start < head.Length && head[start] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') {
            start++;
        }
        head = head[start..];
        if (head.IsEmpty) {
            return "";
        }
        if (head[..Math.Min(300, head.Length)].IndexOf("<EncryptedData"u8) >= 0) {
            return "EncryptedData";
        }
        return head.StartsWith("<SCLSource"u8) ? "SCLSource" : head.StartsWith("<FlgNet"u8) ? "FlgNet" : head.StartsWith("<Statements"u8) ? "Statements" : "Other";
    }

    private List<TiaObjectKey> PositionalParts(in TiaFrame root) {
        var parts = new List<TiaObjectKey>();
        foreach (var relation in _db.EnumerateRelations(root)) {
            if (_usedParts.Matches(relation.RelationId)) {
                parts.Add(relation.Target);
            }
        }
        return parts;
    }

    private static void CollectValues(ReadOnlySpan<byte> xml, Dictionary<string, string> values) {
        var reader = new Utf8XmlReader(xml);
        while (reader.Read()) {
            if (!reader.IsEnd && reader.Name.SequenceEqual("V"u8) && reader.GetString("p"u8) is { } path) {
                var value = reader.GetString("Display"u8) ?? reader.GetString("v"u8);
                if (value is not null) {
                    values.TryAdd(path, value);
                }
            }
        }
    }

    private List<TiaMemberNode> ParseRoot(in TiaFrame root, InterfaceContext context) {
        using var payload = _db.ReadBlob(root, _partPayload);
        CollectValues(payload.Span, context.Values);
        using (var declared = _db.ReadExpandoBlob(root, "Values")) {
            CollectValues(declared.Span, context.Declared);
        }
        var reader = new Utf8XmlReader(payload.Span);
        var hasContent = !payload.IsEmpty && reader.Read();
        var holdsOnlyValues = hasContent ? reader.Name.SequenceEqual("Values"u8) : _db.ReadString(root, _partKind) == "Values";
        if (holdsOnlyValues && context.Parts.Count != 0 && _db.TryGetFrame(context.Parts[0], out var type)) {
            // A data block of a type holds only its values, none at all if nothing is set; the members are the type's.
            context.Parts = PositionalParts(type);
            context.HasOptimizedLayout = true;
            return ParseRoot(type, context);
        }
        if (!hasContent) {
            return [];
        }
        var members = ParseChildren(ref reader, "", "", context, out var offsets);
        ApplyOffsets(members, offsets);
        return members;
    }

    private List<TiaMemberNode> ParseChildren(ref Utf8XmlReader reader, string section, string prefix, InterfaceContext context, out List<int?> offsets) {
        var members = new List<TiaMemberNode>();
        offsets = [];
        if (reader.IsEmpty) {
            return members;
        }
        while (reader.Read()) {
            if (reader.IsEnd) {
                return members;
            }
            if (reader.Name.SequenceEqual("Offsets"u8)) {
                if (prefix.Length == 0 && section.Length == 0) {
                    context.RootOptimizedBits = reader.GetInt32("optSize"u8);
                }
                ReadOffsets(ref reader, offsets);
            } else if (reader.Name.SequenceEqual("Member"u8)) {
                ParseMember(ref reader, section, prefix, context, members);
            } else if (reader.Name.SequenceEqual("Constants"u8)) {
                ReadConstants(ref reader, context.Constants);
            } else {
                reader.SkipElement();
            }
        }
        return members;
    }

    /// <summary>The constants this interface uses in array bounds, by the name it writes them with.</summary>
    private static void ReadConstants(ref Utf8XmlReader reader, Dictionary<string, int> constants) {
        if (reader.IsEmpty) {
            return;
        }
        while (reader.Read() && !reader.IsEnd) {
            if (reader.Name.SequenceEqual("Constant"u8)
                && reader.GetString("Display"u8) is { } name
                && int.TryParse(reader.GetString("Value"u8), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) {
                constants[name] = value;
            }
            reader.SkipElement();
        }
    }

    private static void ReadOffsets(ref Utf8XmlReader reader, List<int?> offsets) {
        if (reader.IsEmpty) {
            return;
        }
        while (reader.Read() && !reader.IsEnd) {
            if (reader.Name.SequenceEqual("o"u8)) {
                offsets.Add(reader.GetInt32("o"u8));
            }
            reader.SkipElement();
        }
    }

    private void ParseMember(ref Utf8XmlReader reader, string section, string prefix, InterfaceContext context, List<TiaMemberNode> into) {
        var name = reader.GetString("Name"u8) ?? "";
        var id = reader.GetString("ID"u8) ?? "";
        var rid = reader.GetString("RID"u8);
        var type = reader.GetString("Type"u8);
        var subPart = reader.GetInt32("SubPartIndex"u8);
        var remanence = reader.GetString("Remanence"u8);
        var flags = reader.GetInt32("MFlags"u8);
        var atMember = reader.GetString("AtMemberID"u8);

        if (rid is null && name is "Input" or "Output" or "InOut" or "Static" or "Temp" or "Return" or "Constant") {
            var inner = ParseChildren(ref reader, name, prefix, context, out _);
            if (inner.Count == 0 && subPart is { } index) {
                inner = ParsePart(index, name, prefix, context, requireStructure: false);
            }
            into.AddRange(inner);
            return;
        }

        var path = prefix + id;
        var kids = ParseChildren(ref reader, section, path + ":", context, out var offsets);
        ApplyOffsets(kids, offsets);
        var typeMembers = new List<TiaMemberNode>();
        if (kids.Count == 0 && subPart is { } structure) {
            kids = ParsePart(structure, section, path + ":", context, requireStructure: true);
            if (kids.Count == 0 && type is not null) {
                typeMembers = ParseType(structure, section, path + ":", context);
            }
        }
        var libraryVersion = type is not null && subPart is { } external ? LibraryVersion(external, context) : null;
        into.Add(new TiaMemberNode(name, TypeOf(type, rid, subPart), path, section, context.Comments?.GetValueOrDefault(path), context.Values.GetValueOrDefault(path), rid, kids) {
            TypeMembers = typeMembers,
            IsRetain = remanence == "Retain",
            IsDbSpecific = remanence == "Classic" && section == "Static" && atMember is null,
            Flags = flags,
            Id = id,
            AtMemberId = atMember,
            InstructionVersion = libraryVersion,
            Constants = context.Constants,
        });
    }

    private List<TiaMemberNode> ParsePart(int index, string section, string prefix, InterfaceContext context, bool requireStructure) {
        if (index >= context.Parts.Count || !_db.TryGetFrame(context.Parts[index], out var part)) {
            return [];
        }
        if (requireStructure && _db.ReadString(part, _partKind) != "Structure") {
            return [];
        }
        using var payload = _db.ReadBlob(part, _partPayload);
        if (payload.IsEmpty) {
            return [];
        }
        var reader = new Utf8XmlReader(payload.Span);
        if (!reader.Read()) {
            return [];
        }
        var members = ParseChildren(ref reader, section, prefix, context, out var offsets);
        ApplyOffsets(members, offsets);
        return members;
    }

    /// <summary>A member's own type, for naming what is inside it: the members of a UDT, or of an FB's instance.</summary>
    private List<TiaMemberNode> ParseType(int index, string section, string prefix, InterfaceContext context) {
        if (index >= context.Parts.Count || !_db.TryGetFrame(context.Parts[index], out var part)) {
            return [];
        }
        using var payload = _db.ReadBlob(part, _partPayload);
        if (payload.IsEmpty) {
            return [];
        }
        var reader = new Utf8XmlReader(payload.Span);
        if (!reader.Read()) {
            return [];
        }
        var inner = new InterfaceContext { Parts = PositionalParts(part) };
        var members = ParseChildren(ref reader, section, prefix, inner, out var offsets);
        ApplyOffsets(members, offsets);
        return members;
    }

    /// <summary>The version a system type states about itself, such as <c>1.0</c>: stored as <c>1.0.3;S71500,S71200</c>. Null for any other type.</summary>
    private string? LibraryVersion(int index, InterfaceContext context) {
        if (index >= context.Parts.Count || !_db.TryGetFrame(context.Parts[index], out var part)) {
            return null;
        }
        var version = _db.ReadString(part, _libraryVersion);
        if (string.IsNullOrEmpty(version)) {
            return null;
        }
        var segments = version.Split(';')[0].Split('.');
        return segments.Length >= 2 ? segments[0] + "." + segments[1] : segments[0];
    }

    private static void ApplyOffsets(List<TiaMemberNode> members, List<int?> offsets) {
        for (var i = 0; i < members.Count && i < offsets.Count; i++) {
            members[i].OptimizedBitOffset = offsets[i];
        }
    }

    /// <summary>
    /// Fills start values from the compiled image. The XML lists only some of them; the image of an optimized data
    /// block generated from a UDT holds all: an 8 byte head, then little-endian values at the sum of the bit offsets
    /// along the path. Only there: an instance data block's offsets count within its sections, a data block with
    /// standard access has another layout, and a protected block's image is left alone.
    /// </summary>
    private void ApplyImage(TiaBlock block, List<TiaMemberNode> members, InterfaceContext context) {
        if (block.IsKnowHowProtected
            || !context.HasOptimizedLayout
            || context.RootOptimizedBits is not { } optimizedBits
            || !_db.TryGetTarget(block.Frame, _generatedFrom, out var master)
            || _db.Schema.FindObjectType(master.TypeId)?.FullName != UserTypeName
            || !_db.TryGetTarget(block.Frame, _loadables, out var load)) {
            return;
        }
        using var image = _db.ReadBlob(load, _image);
        if (image.Length < 8 + ((optimizedBits + 7) / 8)) {
            return;
        }
        Walk(members, 0, image.Span);

        static void Walk(List<TiaMemberNode> nodes, int baseBits, ReadOnlySpan<byte> bytes) {
            foreach (var node in nodes) {
                if (node.OptimizedBitOffset is not { } bits) {
                    continue;
                }
                var at = baseBits + bits;
                if (node.Children.Count != 0) {
                    Walk(node.Children, at, bytes);
                } else if (node.StartValue is null) {
                    node.StartValue = FormatScalar(node.Rid, bytes, (at / 8) + 8, at % 8);
                }
            }
        }
    }

    /// <summary>A scalar from the image, or null for a type it does not know. Only a Bool can start inside a byte.</summary>
    internal static string? FormatScalar(string? rid, ReadOnlySpan<byte> image, int at, int bit) {
        (int Size, string Form) shape = rid switch {
            "0x02000001" => (1, "bool"),
            "0x02000034" => (1, "u"),
            "0x02000037" => (1, "s"),
            "0x02000002" => (1, "x"),
            "0x02000005" => (2, "s"),
            "0x02000035" => (2, "u"),
            "0x02000004" => (2, "x"),
            "0x02000007" => (4, "s"),
            "0x02000036" => (4, "u"),
            "0x02000006" => (4, "x"),
            "0x02000032" => (8, "s"),
            "0x02000031" => (8, "u"),
            _ => default,
        };
        if (shape.Size == 0 || at + shape.Size > image.Length || (bit != 0 && shape.Form != "bool")) {
            return null;
        }
        Span<byte> wide = stackalloc byte[8];
        wide.Clear();
        image.Slice(at, shape.Size).CopyTo(wide);
        var unsigned = BinaryPrimitives.ReadUInt64LittleEndian(wide);
        return shape.Form switch {
            "bool" => (unsigned >> bit & 1) != 0 ? "TRUE" : "FALSE",
            "u" => unsigned.ToString(CultureInfo.InvariantCulture),
            "x" => "16#" + unsigned.ToString("X" + (shape.Size * 2), CultureInfo.InvariantCulture),
            _ => shape.Size switch {
                1 => ((sbyte)unsigned).ToString(CultureInfo.InvariantCulture),
                2 => ((short)unsigned).ToString(CultureInfo.InvariantCulture),
                4 => ((int)unsigned).ToString(CultureInfo.InvariantCulture),
                _ => ((long)unsigned).ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    private string TypeOf(string? type, string? rid, int? subPart) {
        if (!string.IsNullOrEmpty(type)) {
            return type;
        }
        if (rid is null) {
            return "";
        }
        var code = unchecked((int)uint.Parse(rid.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (_typeCodes.Value.TryGetValue(code, out var name) || ElementaryTypeCodes.TryGetValue(code, out name)) {
            return name == "LTOD" ? "LTime_Of_Day" : name;
        }
        // Only an OB's inputs use these two, and no identifier states their names.
        switch (code) {
            case 0x02000000:
                return "Void";
            case 0x020000D0:
                return "DB_ANY";
            case 0x020000A0:
                return "OB_ANY";
        }
        return code >> 24 != 2 && subPart is not null ? "Struct" : "RID " + rid;
    }

    /// <summary>
    /// The elementary type codes, for a type that no identifier of the file names: what the files seen state, and what
    /// TIA writes for a member of each type (the format page, section 11).
    /// </summary>
    private static readonly Dictionary<int, string> ElementaryTypeCodes = new() {
        [0x02000001] = "Bool", [0x02000002] = "Byte", [0x02000003] = "Char", [0x02000004] = "Word", [0x02000005] = "Int",
        [0x02000006] = "DWord", [0x02000007] = "DInt", [0x02000008] = "Real", [0x02000009] = "Date", [0x0200000A] = "Time_Of_Day",
        [0x0200000B] = "Time", [0x0200000C] = "S5Time", [0x0200000E] = "Date_And_Time", [0x02000013] = "String",
        [0x02000030] = "LReal", [0x02000031] = "ULInt", [0x02000032] = "LInt", [0x02000033] = "LWord", [0x02000034] = "USInt",
        [0x02000035] = "UInt", [0x02000036] = "UDInt", [0x02000037] = "SInt", [0x0200003D] = "WChar", [0x0200003E] = "WString",
        [0x02000040] = "LTime", [0x02000041] = "LTime_Of_Day", [0x02000042] = "LDT", [0x02000043] = "DTL",
    };

    /// <summary>The file states its own type codes: <c>T="Bool:33554433:Bool"</c> in every identifier part.</summary>
    private Dictionary<int, string> HarvestTypeCodes() {
        var codes = new Dictionary<int, string>();
        var type = _db.Schema.FindObjectType("Siemens.Simatic.Lang.Model.Idents.IdentPartData")?.Id;
        foreach (ref readonly var frame in _db.Frames) {
            if (frame.TypeId != type) {
                continue;
            }
            using var payload = _db.ReadBlob(frame, _identPayload);
            var rest = payload.Span;
            while (true) {
                var hit = rest.IndexOf(" T=\""u8);
                if (hit < 0) {
                    break;
                }
                rest = rest[(hit + 4)..];
                var firstColon = rest.IndexOf((byte)':');
                if (firstColon <= 0) {
                    continue;
                }
                var name = rest[..firstColon];
                var afterName = rest[(firstColon + 1)..];
                var secondColon = afterName.IndexOf((byte)':');
                if (secondColon <= 0 || !System.Buffers.Text.Utf8Parser.TryParse(afterName[..secondColon], out int code, out var used) || used != secondColon || code < 0) {
                    continue;
                }
                if (name.IndexOf((byte)'"') < 0) {
                    codes[code] = Encoding.UTF8.GetString(name);
                }
            }
        }
        return codes;
    }

    private static List<TiaMember> Freeze(List<TiaMemberNode> nodes, bool hasAccessFlags, TiaDeclaredValues values, string indexedParent = "", bool hiddenFromParent = false, bool readOnlyFromParent = false) {
        var result = new List<TiaMember>(nodes.Count);
        foreach (var n in nodes) {
            var overlaid = n.AtMemberId is null ? null : nodes.Find(other => other.Id == n.AtMemberId);
            var hidden = TiaMemberFlags.IsHidden(n.Flags ?? overlaid?.Flags, hiddenFromParent);
            var showsAccess = hasAccessFlags && n.Section is not ("Temp" or "Constant" or "Return");
            var indexed = indexedParent + n.Id;
            var declared = values.Text(n, indexed);
            var readOnly = readOnlyFromParent || TiaMemberFlags.IsReadOnly(n.Flags ?? overlaid?.Flags);
            var children = n.Children.Count == 0 ? [] : Freeze(n.Children, hasAccessFlags, values, TiaDeclaredValues.FirstElement(n, indexed) + ":", hidden, readOnly);
            result.Add(new TiaMember(n.Name, n.Type, n.Path, n.Section, n.Comment, n.StartValue ?? declared, children, n.IsRetain) {
                IsDbSpecific = n.IsDbSpecific,
                Declared = declared,
                AtMember = overlaid?.Name,
                Attributes = TiaMemberFlags.Attributes(showsAccess ? n.Flags : null, showsAccess && hidden, n.Section, n.Type, n.InstructionVersion),
                ExternalAccess = !showsAccess ? null : hidden ? TiaExternalAccess.None : readOnly ? TiaExternalAccess.ReadOnly : TiaExternalAccess.ReadWrite,
            });
        }
        return result;
    }

    private sealed class InterfaceContext {
        public List<TiaObjectKey> Parts { get; set; } = [];
        public Dictionary<string, string>? Comments { get; set; }
        public Dictionary<string, string> Values { get; } = [];
        public Dictionary<string, string> Declared { get; } = [];
        public Dictionary<string, int> Constants { get; } = [];
        public bool IsDataBlock { get; init; }
        public bool HasOptimizedLayout { get; set; }
        public int? RootOptimizedBits { get; set; }
    }
}
