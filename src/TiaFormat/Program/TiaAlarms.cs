using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A text of a program alarm, such as its alarm text, in every language it stores.</summary>
/// <param name="Kind">One of <see cref="TiaAlarms.TextKinds"/>.</param>
/// <param name="Texts">Each language's text as stored; empty if the alarm stores none of its own.</param>
/// <param name="Inherited">True if an instance alarm stores no text of its own here, so its type's text stands.</param>
public sealed record TiaAlarmText(string Kind, IReadOnlyList<TiaText> Texts, bool Inherited = false);

/// <summary>An associated value of a program alarm: its number, 1 for <c>SD_1</c>, and the operand its block wires to it.</summary>
public readonly record struct TiaAssociatedValue(int Number, string Operand);

/// <summary>
/// A program alarm (spec 0014): an alarm type, declared by a <c>Program_Alarm</c> member of an FB, or one of its
/// instances in an instance data block. An instance's texts, class and priority are its own where it stores them, else
/// its type's.
/// </summary>
/// <param name="Plc">The PLC's name.</param>
/// <param name="Block">The FB that declares the alarm.</param>
/// <param name="Name">The <c>Program_Alarm</c> member that declares it.</param>
/// <param name="Instance">The instance data block, or null for the alarm type itself.</param>
/// <param name="IsClosed">True if the declaring FB is know-how protected: then nothing more is read (ADR 0011).</param>
public sealed record TiaAlarm(string Plc, string Block, string Name, string? Instance, bool IsClosed) {
    /// <summary>The alarm's number in the PLC; only an instance has one.</summary>
    public long? AlarmId { get; init; }

    /// <summary>The alarm class's name, or null if none is set.</summary>
    public string? Class { get; init; }

    /// <summary>The priority, or null if none is set.</summary>
    public long? Priority { get; init; }

    /// <summary>The eleven texts in TIA's order: alarm text, info text, additional texts 1 to 9.</summary>
    public IReadOnlyList<TiaAlarmText> Texts { get; init; } = [];

    /// <summary>The associated values the alarm passes, with the operands its block wires to them.</summary>
    public IReadOnlyList<TiaAssociatedValue> AssociatedValues { get; init; } = [];

    /// <summary>The text lists its texts take a text from, by name.</summary>
    public IReadOnlyList<string> TextLists { get; init; } = [];

    /// <summary>The alarm as a person names it: the instance or the FB, then the member.</summary>
    public string Path => (Instance ?? Block) + "." + Name;
}

/// <summary>A project's alarm class.</summary>
public sealed record TiaAlarmClass(string Name, IReadOnlyList<TiaText> DisplayName, bool Acknowledgement, long? GlobalId);

/// <summary>An entry of a text list: the values it covers and its text in every language.</summary>
public sealed record TiaTextListEntry(long From, long To, IReadOnlyList<TiaText> Texts);

/// <summary>A PLC's user text list (spec 0014).</summary>
/// <param name="Range">How its values are read: Decimal, Binary or Bit.</param>
/// <param name="UsedBy">The paths of the alarms whose texts take a text from it.</param>
public sealed record TiaTextList(string Plc, string Name, string Range, IReadOnlyList<TiaText> Comment, IReadOnlyList<TiaTextListEntry> Entries, IReadOnlyList<string> UsedBy);

/// <summary>
/// Reads a project's program alarms, alarm classes and text lists (spec 0014; the format page says where they are
/// stored). The alarms of a know-how protected FB stay closed: they are listed by name and nothing else is read.
/// </summary>
public sealed partial class TiaAlarms {
    /// <summary>The kinds of an alarm's texts, in TIA's order.</summary>
    public static readonly IReadOnlyList<string> TextKinds = ["Alarm text", "Info text", .. Enumerable.Range(1, 9).Select(i => $"Additional text {i}")];

    private const string AlarmData = "Siemens.Simatic.AlarmServices.ObjectFrame.IAlarmData";
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private const string RangeList = "Siemens.Automation.DomainModel.TextLists.IObjectRangeListData";
    private const string Range = "Siemens.Automation.DomainModel.TextLists.IRangeData";

    private readonly TiaProgram _program;
    private readonly TiaDatabase _db;
    private readonly Dictionary<ulong, TiaBlock> _blocks;
    private readonly TiaAttributeHandle _name, _subtype, _comment, _text, _alarmId, _priority, _acknowledgement, _globalId, _listRange, _from, _to, _entryText;
    private readonly TiaRelationHandle _texts, _typeAlarm, _parent, _alarmClass, _displayName, _entries, _usedBy;

    public TiaAlarms(TiaProgram program) {
        _program = program;
        _db = program.Database;
        _blocks = program.Blocks.Where(block => !block.IsStub).GroupBy(block => block.Frame.ObjectId).ToDictionary(group => group.Key, group => group.First());
        var s = _db.Schema;
        _name = s.Attribute(Core, "Name");
        _subtype = s.Attribute(Core, "Subtype");
        _comment = s.Attribute(Core, "Comment");
        _text = s.Attribute("Siemens.Automation.ObjectFrame.ICoreTextRepository", "Text");
        _alarmId = s.Attribute(AlarmData, "AlarmId");
        _priority = s.Attribute(AlarmData, "Priority");
        _acknowledgement = s.Attribute("Siemens.Simatic.AlarmServices.ObjectFrame.IAlarmClassData", "Acknowledgement");
        _globalId = s.Attribute("Siemens.Simatic.AlarmServices.ObjectFrame.IAlarmClassData", "GlobalId");
        _listRange = s.Attribute(RangeList, "ListRange");
        _from = s.Attribute(Range, "RangeFrom");
        _to = s.Attribute(Range, "RangeTo");
        _entryText = s.Attribute(Range, "Text");
        _texts = s.Relation("Texts");
        _typeAlarm = s.Relation("TypeAlarm");
        _parent = s.Relation("PhysicalParent");
        _alarmClass = s.Relation("AlarmClass");
        _displayName = s.Relation("AlarmClassData_IDisplayNaming_DisplayName");
        _entries = s.Relation("Entries");
        _usedBy = s.Relation("IsUsedByAlarms");
    }

    /// <summary>Every program alarm, each type followed by its instances, by PLC, block and member.</summary>
    public IReadOnlyList<TiaAlarm> Alarms() {
        var result = new List<TiaAlarm>();
        foreach (var frame in FramesOf("Siemens.Simatic.AlarmServices.ObjectFrame.Private.SimpleControllerAlarmData")) {
            if (Read(frame) is { } alarm) {
                result.Add(alarm);
            }
        }
        return [.. result.OrderBy(alarm => alarm.Plc, StringComparer.Ordinal).ThenBy(alarm => alarm.Block, StringComparer.Ordinal)
            .ThenBy(alarm => alarm.Name, StringComparer.Ordinal).ThenBy(alarm => alarm.Instance is not null).ThenBy(alarm => alarm.Instance, StringComparer.Ordinal)];
    }

    /// <summary>The project's alarm classes, by name.</summary>
    public IReadOnlyList<TiaAlarmClass> Classes() =>
        [.. FramesOf("Siemens.Simatic.AlarmServices.ObjectFrame.AlarmClassData").Select(frame => new TiaAlarmClass(
            _db.ReadString(frame, _name) ?? "",
            _db.TryGetTarget(frame, _displayName, out var text) ? _db.ReadCoreTexts(text, _text) : [],
            _db.ReadBoolean(frame, _acknowledgement),
            _db.ReadInteger(frame, _globalId))).OrderBy(alarmClass => alarmClass.Name, StringComparer.Ordinal)];

    /// <summary>Every PLC's user text lists, by PLC and name, each entry by its first value.</summary>
    public IReadOnlyList<TiaTextList> TextLists() {
        var paths = new Dictionary<ulong, string>();
        foreach (var frame in FramesOf("Siemens.Simatic.AlarmServices.ObjectFrame.Private.SimpleControllerAlarmData")) {
            if (Read(frame) is { } alarm) {
                paths[frame.ObjectId] = alarm.Path;
            }
        }
        var result = new List<TiaTextList>();
        foreach (var frame in FramesOf("Siemens.Automation.DomainModel.UserTextRangeListData")) {
            var entries = new List<TiaTextListEntry>();
            var usedBy = new List<string>();
            foreach (var relation in _db.EnumerateRelations(frame)) {
                if (!_db.TryGetFrame(relation.Target, out var target)) {
                    continue;
                }
                if (_entries.Matches(relation.RelationId)) {
                    entries.Add(new TiaTextListEntry(_db.ReadInteger(target, _from) ?? 0, _db.ReadInteger(target, _to) ?? 0, _db.ReadCoreTexts(target, _entryText)));
                } else if (_usedBy.Matches(relation.RelationId) && paths.TryGetValue(target.ObjectId, out var path)) {
                    usedBy.Add(path);
                }
            }
            result.Add(new TiaTextList(_program.PlcName(frame) ?? "", _db.ReadString(frame, _name) ?? "", _db.ReadString(frame, _listRange) ?? "",
                _db.ReadCoreTexts(frame, _comment), [.. entries.OrderBy(entry => entry.From)], [.. usedBy.Order(StringComparer.Ordinal)]));
        }
        return [.. result.OrderBy(list => list.Plc, StringComparer.Ordinal).ThenBy(list => list.Name, StringComparer.Ordinal)];
    }

    private TiaAlarm? Read(in TiaFrame frame) {
        var isInstance = _db.ReadString(frame, _subtype) == "InstanceAlarm";
        TiaFrame type = frame;
        if (isInstance && !_db.TryGetTarget(frame, _typeAlarm, out type)) {
            return null;
        }
        if (!_db.TryGetTarget(type, _parent, out var parent) || !_blocks.TryGetValue(parent.ObjectId, out var block)) {
            return null;
        }
        string? instance = null;
        if (isInstance) {
            instance = _db.TryGetTarget(frame, _parent, out var data) && _blocks.TryGetValue(data.ObjectId, out var dataBlock) ? dataBlock.Name : null;
        }
        var alarm = new TiaAlarm(_program.PlcName(frame) ?? "", block.Name, _db.ReadString(frame, _name) ?? "", instance, block.IsKnowHowProtected);
        if (alarm.IsClosed) {
            return alarm;
        }
        var texts = Texts(frame);
        if (isInstance) {
            var typeTexts = Texts(type);
            texts = [.. texts.Select((text, i) => HasText(text.Texts) || i >= typeTexts.Count ? text : text with { Texts = typeTexts[i].Texts, Inherited = true })];
        }
        return alarm with {
            AlarmId = isInstance ? _db.ReadInteger(frame, _alarmId) : null,
            Class = ClassName(frame) ?? (isInstance ? ClassName(type) : null),
            Priority = Priority(frame) ?? (isInstance ? Priority(type) : null),
            Texts = texts,
            AssociatedValues = AssociatedValues(type, block),
            TextLists = [.. texts.SelectMany(text => text.Texts).SelectMany(text => TextListReference().Matches(text.Text).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        };
    }

    private List<TiaAlarmText> Texts(in TiaFrame alarm) {
        // TIA stores the info text first, then the alarm text, then the additional texts.
        var stored = new List<IReadOnlyList<TiaText>>();
        foreach (var relation in _db.EnumerateRelations(alarm)) {
            if (_texts.Matches(relation.RelationId)) {
                stored.Add(_db.TryGetFrame(relation.Target, out var text) ? _db.ReadCoreTexts(text, _text) : []);
            }
        }
        var result = new List<TiaAlarmText>();
        for (var i = 0; i < TextKinds.Count; i++) {
            var at = i switch { 0 => 1, 1 => 0, _ => i };
            result.Add(new TiaAlarmText(TextKinds[i], at < stored.Count ? stored[at] : []));
        }
        return result;
    }

    private static bool HasText(IReadOnlyList<TiaText> texts) => texts.Any(text => text.Text.Length != 0);

    private string? ClassName(in TiaFrame alarm) => _db.TryGetTarget(alarm, _alarmClass, out var alarmClass) ? _db.ReadString(alarmClass, _name) : null;

    private long? Priority(in TiaFrame alarm) => _db.ReadInteger(alarm, _priority) is { } priority and >= 0 ? priority : null;

    /// <summary>The type's associated values: stored as XML naming each value's identifier in the declaring block.</summary>
    private List<TiaAssociatedValue> AssociatedValues(in TiaFrame type, TiaBlock block) {
        var result = new List<TiaAssociatedValue>();
        var xml = _db.ReadExpandoString(type, "AlarmServices_AssociateValueInfo");
        if (xml is null) {
            using var blob = _db.ReadExpandoBlob(type, "AlarmServices_AssociateValueInfo");
            xml = blob.Length == 0 ? null : Encoding.UTF8.GetString(blob.Span);
        }
        var start = xml?.IndexOf('<') ?? -1;
        if (start < 0) {
            return result;
        }
        var idents = _program.ReadIdents(block);
        using var reader = XmlReader.Create(new StringReader(xml![start..]), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        while (reader.Read()) {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "AVI"
                && int.TryParse(reader.GetAttribute("Index"), out var index) && int.TryParse(reader.GetAttribute("IdentRef"), out var ident)) {
                result.Add(new TiaAssociatedValue(index + 1, idents.Name(ident)));
            }
        }
        return result;
    }

    private IEnumerable<TiaFrame> FramesOf(string typeName) {
        if (_db.Schema.FindObjectType(typeName) is not { } type) {
            return [];
        }
        var frames = new List<TiaFrame>();
        foreach (ref readonly var frame in _db.Frames) {
            if (frame.TypeId == type.Id) {
                frames.Add(frame);
            }
        }
        return frames;
    }

    [GeneratedRegex(@"%t#([^@]+)@", RegexOptions.CultureInvariant)]
    private static partial Regex TextListReference();
}
