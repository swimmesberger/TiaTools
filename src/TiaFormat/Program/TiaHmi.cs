using System.Buffers.Binary;
using System.Text.RegularExpressions;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>
/// Reads a project's HMI devices (spec 0011, format page section 19): connections, HMI tags with the PLC tags and data
/// block members they read, HMI alarms, and screens with their items. Runtime users and passwords are never read.
/// </summary>
public static partial class TiaHmi {
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private const string Utah = "Siemens.Simatic.Hmi.Utah.";
    private const string ModernUi = "Siemens.Simatic.Hmi.DL.ModernUI.";

    // A Unified screen and its items keep their properties as dictionary expando values under short keys: a property's
    // number as a character (format page, section 19).
    private const string UnifiedWidth = "\u0005";
    private const string UnifiedHeight = "\u0006";
    private const string UnifiedLeft = "c";
    private const string UnifiedTop = "d";
    private const string UnifiedNumber = "\u0007\u0005";

    /// <summary>The report of every HMI device of the project, or of the one named <paramref name="device"/> only.</summary>
    public static TiaHmiReport Read(TiaProgram program, string? device = null) => new Reader(program).Read(device);

    /// <summary>One pass over the database: every HMI object, grouped by the HMI application it belongs to.</summary>
    private sealed class Reader {
        private readonly TiaProgram _program;
        private readonly TiaDatabase _db;
        private readonly TiaAttributeHandle _text, _uniqueId;
        private readonly TiaAttributeHandle _name, _subtype, _deviceType, _plcId, _logicalAddress, _addressMode, _typeName, _alarmId, _triggerMode,
            _limitMode, _limitValue, _screenNumber, _screenWidth, _screenHeight, _itemLeft, _itemTop, _itemWidth, _itemHeight, _bit;
        private readonly TiaRelationHandle _target, _environment, _connection, _cycle, _content, _consumer, _provider, _connectionPoint, _remoteTargets,
            _alarmClass, _discreteTrigger, _analogTrigger, _textChildren, _children, _ilItems, _expandoReferences;
        private readonly uint? _application, _connectionType, _tableType, _tagType, _linkType, _discreteType, _analogType, _screenType, _itemType, _ilScreenType, _ilItemType,
            _plcTagType, _dataBlockType, _cycleType, _classType, _textType, _structureMemberType, _arrayMemberType;
        private readonly TiaRelationHandle _membersInverse, _elementsInverse;

        public Reader(TiaProgram program) {
            _program = program;
            _db = program.Database;
            var s = _db.Schema;
            _name = s.Attribute(Core, "Name");
            _text = s.Attribute("Siemens.Automation.ObjectFrame.ICoreTextRepository", "Text");
            _uniqueId = s.Attribute(Utah + "Alarm.IHmiScadaAlarmAttributes", "UniqueId");
            _subtype = s.Attribute(Core, "Subtype");
            _deviceType = s.Attribute("Siemens.Simatic.HwConfiguration.Hmi.Model.IHmiApplicationAttributes", "DeviceApplicationType");
            _plcId = s.Attribute(Utah + "Communication.IHmiConnectionAttributes", "PlcId");
            _logicalAddress = s.Attribute("Siemens.Automation.DomainModel.ITagAddress", "LogicalAddress");
            _addressMode = s.Attribute("Siemens.Automation.DomainModel.ITagAddress", "AddressMode");
            _typeName = s.Attribute("Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem", "DisplayTypeName");
            _alarmId = s.Attribute("Siemens.Simatic.AlarmServices.ObjectFrame.IAlarmData", "AlarmId");
            _triggerMode = s.Attribute(Utah + "Alarm.IHmiDiscreteAlarmAttributes", "TriggerMode");
            _bit = s.Attribute(Utah + "Alarm.IHmiDiscreteAlarmAttributes", "TriggerBitNumber");
            _limitMode = s.Attribute(Utah + "Alarm.IHmiAnalogAlarmLimitAttributes", "LimitMode");
            _limitValue = s.Attribute(Utah + "Alarm.IHmiAnalogAlarmLimitAttributes", "LimitValueConstant");
            _screenNumber = s.Attribute(Utah + "GraphX.IHmiScreenAttributes", "Number");
            _screenWidth = s.Attribute(Utah + "GraphX.IHmiScreenAttributes", "Width");
            _screenHeight = s.Attribute(Utah + "GraphX.IHmiScreenAttributes", "Height");
            _itemLeft = s.Attribute(Utah + "GraphX.IHmiScreenItemAttributes", "Left");
            _itemTop = s.Attribute(Utah + "GraphX.IHmiScreenItemAttributes", "Top");
            _itemWidth = s.Attribute(Utah + "GraphX.IHmiScreenItemAttributes", "Width");
            _itemHeight = s.Attribute(Utah + "GraphX.IHmiScreenItemAttributes", "Height");
            _target = s.Relation("Target");
            _environment = s.Relation("Environment");
            _connection = s.Relation("IHmiTagAttributes_Connection");
            _cycle = s.Relation("IHmiTagAttributes_AcquisitionCycle");
            _content = s.Relation("Content");
            _consumer = s.Relation("Consumer");
            _provider = s.Relation("Provider");
            _connectionPoint = s.Relation("ConnectionPoint");
            _remoteTargets = s.Relation("Conn2RemoteTargets");
            _alarmClass = s.Relation("HmiAlarmClassDataRelation");
            _discreteTrigger = s.Relation("IHmiDiscreteAlarmAttributes_TriggerTag");
            _analogTrigger = s.Relation("IHmiAnalogAlarmAttributes_TriggerTag");
            _textChildren = s.Relation("ExpandoChildren");
            _children = s.Relation("Children");
            _ilItems = s.Relation("ILScreenItems");
            _expandoReferences = s.Relation("ExpandoReferences");
            _application = s.FindObjectType("Siemens.Simatic.HwConfiguration.Hmi.Model.HmiApplicationData")?.Id;
            _connectionType = s.FindObjectType(Utah + "Communication.HmiConnectionData")?.Id;
            _tableType = s.FindObjectType(Utah + "Tag.HmiTagTableData")?.Id;
            _tagType = s.FindObjectType(Utah + "Tag.HmiTagData")?.Id;
            _linkType = s.FindObjectType("Siemens.Automation.DomainModel.LinkMaintainer.ScopedLinkMaintainerData")?.Id;
            _discreteType = s.FindObjectType(Utah + "Alarm.HmiDiscreteAlarmData")?.Id;
            _analogType = s.FindObjectType(Utah + "Alarm.HmiAnalogAlarmData")?.Id;
            _screenType = s.FindObjectType(Utah + "GraphX.HmiScreenData")?.Id;
            _itemType = s.FindObjectType(Utah + "GraphX.HmiScreenItemData")?.Id;
            _ilScreenType = s.FindObjectType(ModernUi + "ILScreenData")?.Id;
            _ilItemType = s.FindObjectType(ModernUi + "ILScreenItemData")?.Id;
            _plcTagType = s.FindObjectType("Siemens.Automation.DomainModel.EAMTZTagData")?.Id;
            _dataBlockType = s.FindObjectType("Siemens.Simatic.Lang.Model.Blocks.DataBlockData")?.Id;
            _cycleType = s.FindObjectType(Utah + "Cycles.HmiCycleData")?.Id;
            _classType = s.FindObjectType(Utah + "Alarm.HmiAlarmClassData")?.Id;
            _textType = s.FindObjectType(Utah + "Globalization.HmiTextData")?.Id;
            _structureMemberType = s.FindObjectType(Utah + "Tag.HmiStructureMemberTagData")?.Id;
            _arrayMemberType = s.FindObjectType(Utah + "Tag.HmiArrayMemberTagData")?.Id;
            _membersInverse = s.Relation("MembersInverse");
            _elementsInverse = s.Relation("ArrayElementsInverse");
        }

        public TiaHmiReport Read(string? only) {
            var applications = new List<TiaFrame>();
            var byApplication = new Dictionary<ulong, Objects>();
            Objects Of(in TiaFrame frame) =>
                _db.TryGetTarget(frame, _target, out var owner) && byApplication.TryGetValue(owner.ObjectId, out var objects) ? objects : Objects.None;

            foreach (ref readonly var frame in _db.Frames) {
                if (frame.TypeId == _application && _program.Environment(frame).Kind == "ProjectData" && (only is null || _db.ReadString(frame, _name) == only)) {
                    applications.Add(frame);
                    byApplication[frame.ObjectId] = new Objects();
                }
            }
            foreach (ref readonly var frame in _db.Frames) {
                var type = frame.TypeId;
                if (type == _connectionType) {
                    Of(frame).Connections.Add(frame);
                } else if (type == _tableType) {
                    Of(frame).Tables.Add(frame);
                } else if (type == _linkType) {
                    Of(frame).Links.Add(frame);
                } else if (type == _discreteType || type == _analogType) {
                    Of(frame).Alarms.Add(frame);
                } else if (type == _ilScreenType || (type == _screenType && _db.ReadString(frame, _subtype) == "ScreenView")) {
                    // Of Comfort/Advanced screens only the screens themselves: templates, pop-ups and slide-ins are out of scope.
                    Of(frame).Screens.Add(frame);
                }
            }

            var devices = new List<TiaHmiDevice>();
            foreach (var application in applications) {
                var objects = byApplication[application.ObjectId];
                var links = ReadLinks(objects.Links);
                devices.Add(new TiaHmiDevice(
                    _db.ReadString(application, _name) ?? "",
                    _db.ReadString(application, _subtype)?.EndsWith(".IL", StringComparison.Ordinal) == true ? TiaWinCcFamily.Unified : TiaWinCcFamily.ComfortAdvanced,
                    _db.ReadString(application, _deviceType),
                    [.. objects.Connections.Select(ReadConnection).OrderBy(connection => connection.Name, StringComparer.Ordinal)],
                    [.. objects.Tables.Select(table => ReadTable(table, links)).OrderBy(table => table.Folder, StringComparer.Ordinal).ThenBy(table => table.Name, StringComparer.Ordinal)],
                    [.. objects.Alarms.Select(ReadAlarm).OrderBy(alarm => alarm.Kind).ThenBy(alarm => alarm.Number ?? long.MaxValue).ThenBy(alarm => alarm.Name, StringComparer.Ordinal)],
                    [.. objects.Screens.Select(ReadScreen).OrderBy(screen => screen.Number ?? long.MaxValue).ThenBy(screen => screen.Name, StringComparer.Ordinal)]));
            }
            return new TiaHmiReport([.. devices.OrderBy(device => device.Name, StringComparer.Ordinal)]);
        }

        /// <summary>The folder path below the HMI device's own root folder, such as <c>HmiTagFolder</c>, which TIA shows as the device's "HMI tags".</summary>
        private string Folder(in TiaFrame frame) {
            var path = _program.FolderPath(frame);
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            return slash < 0 ? "" : path[(slash + 1)..];
        }

        private TiaHmiConnection ReadConnection(TiaFrame frame) {
            string? plc = null;
            if (_db.TryGetTarget(frame, _connectionPoint, out var point) && _db.TryGetTarget(point, _remoteTargets, out var remote)) {
                plc = _db.ReadString(remote, _name);
            }
            return new TiaHmiConnection(_db.ReadString(frame, _name) ?? "", plc, _db.ReadString(frame, _plcId), _db.ReadString(frame, _subtype) == "Integrated");
        }

        /// <summary>
        /// The PLC object each HMI tag reads, by the tag's object id. A tag table's link maintainer holds its consumers
        /// (HMI tags) and its providers (PLC tags, data blocks), each provider once however many tags read it. A tag that
        /// reads a data block names its number in its <c>TypeSafeAddress</c>; a tag that reads a PLC tag holds that tag's
        /// address. Only where neither tells, two lists of the same length are taken in step.
        /// </summary>
        private Dictionary<ulong, TiaFrame> ReadLinks(List<TiaFrame> maintainers) {
            var result = new Dictionary<ulong, TiaFrame>();
            foreach (var maintainer in maintainers) {
                var consumers = new List<TiaObjectKey>();
                var providers = new List<TiaFrame>();
                foreach (var relation in _db.EnumerateRelations(maintainer)) {
                    if (_consumer.Matches(relation.RelationId)) {
                        consumers.Add(relation.Target);
                    } else if (_provider.Matches(relation.RelationId) && _db.TryGetFrame(relation.Target, out var provider)) {
                        providers.Add(provider);
                    }
                }
                for (var i = 0; i < consumers.Count; i++) {
                    if (!_db.TryGetFrame(consumers[i], out var tag)) {
                        continue;
                    }
                    if (FindProvider(tag, providers) is { } found) {
                        result[tag.ObjectId] = found;
                    } else if (consumers.Count == providers.Count) {
                        result[tag.ObjectId] = providers[i];
                    }
                }
            }
            return result;
        }

        private TiaFrame? FindProvider(in TiaFrame tag, List<TiaFrame> providers) {
            if (DataBlockNumber(_db.ReadExpandoValueBytes(tag, "TypeSafeAddress")) is { } number) {
                foreach (var provider in providers) {
                    var id = provider.ObjectId;
                    if (provider.TypeId == _dataBlockType && _program.Blocks.FirstOrDefault(block => block.Frame.ObjectId == id)?.Number == number) {
                        return provider;
                    }
                }
                return null;
            }
            if (_db.ReadString(tag, _logicalAddress) is { Length: > 0 } address) {
                foreach (var provider in providers) {
                    if (provider.TypeId == _plcTagType && _db.ReadString(provider, _logicalAddress) == address) {
                        return provider;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// The data block a <c>TypeSafeAddress</c> names: its <c>Rid</c> is <c>0x8A0E</c> and the block's number in the
        /// low 16 bits; null for any other area.
        /// </summary>
        internal static long? DataBlockNumber(ReadOnlySpan<byte> value) =>
            value.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(value[12..]) is var rid && rid >> 16 == 0x8A0E ? rid & 0xFFFF : null;

        private TiaHmiTagTable ReadTable(TiaFrame table, Dictionary<ulong, TiaFrame> links) {
            var tags = new List<TiaHmiTag>();
            foreach (var relation in _db.EnumerateRelations(table)) {
                if (_content.Matches(relation.RelationId) && relation.Target.TypeId == _tagType && _db.TryGetFrame(relation.Target, out var tag)) {
                    tags.Add(ReadTag(tag, links));
                }
            }
            return new TiaHmiTagTable(_db.ReadString(table, _name) ?? "", Folder(table), tags);
        }

        private TiaHmiTag ReadTag(in TiaFrame tag, Dictionary<ulong, TiaFrame> links) {
            var connection = _db.TryGetTarget(tag, _connection, out var connectionFrame) ? _db.ReadString(connectionFrame, _name) : null;
            var cycle = _db.TryGetTarget(tag, _cycle, out var cycleFrame) ? _db.ReadString(cycleFrame, _name) : null;
            var link = links.TryGetValue(tag.ObjectId, out var provider) ? ReadLink(tag, provider) : null;
            var address = link is null && connection is not null && _db.ReadString(tag, _addressMode) != "Invalid" ? _db.ReadString(tag, _logicalAddress) : null;
            return new TiaHmiTag(_db.ReadString(tag, _name) ?? "", _db.ReadString(tag, _typeName), connection, cycle, address is { Length: > 0 } ? address : null, link);
        }

        /// <summary>
        /// A PLC tag is linked by itself. A data block member is linked by the data block, and the HMI tag's expando
        /// <c>TypeSafeAddress</c>, a structure of <c>CRC</c>, <c>LidPath</c> and <c>Rid</c>, names the member by the
        /// <c>LID</c> of each step of its path.
        /// </summary>
        private TiaHmiPlcLink ReadLink(in TiaFrame tag, in TiaFrame provider) {
            var plc = _program.PlcName(provider);
            var name = _db.ReadString(provider, _name) ?? "";
            if (provider.TypeId != _dataBlockType) {
                return new TiaHmiPlcLink(plc, name, IsDataBlock: false, Member: null, IsResolved: true);
            }
            var path = LidPath(_db.ReadExpandoValueBytes(tag, "TypeSafeAddress"));
            var id = provider.ObjectId;
            var block = _program.Blocks.FirstOrDefault(candidate => candidate.Frame.ObjectId == id);
            var member = path is null || block is null || block.IsKnowHowProtected ? null : ResolveMember(_program.ReadInterface(block), path);
            return new TiaHmiPlcLink(plc, name, IsDataBlock: true, member, member is not null);
        }

        /// <summary>
        /// The <c>LidPath</c> of a <c>TypeSafeAddress</c>: u32 size, u32 CRC, u32 ref to an array of u32, u32 Rid. Each
        /// step is a flag byte and 24 bits of value: <c>0x20</c> a struct, <c>0x40</c> a member that is not one, with
        /// <c>0x10</c> also set an array index; the value is the member's <c>LID</c>, or the index.
        /// </summary>
        internal static List<uint>? LidPath(ReadOnlySpan<byte> value) {
            if (value.Length < 16) {
                return null;
            }
            var array = BinaryPrimitives.ReadInt32LittleEndian(value[8..]);
            if (array < 4 || array + 8 > value.Length) {
                return null;
            }
            var count = BinaryPrimitives.ReadInt32LittleEndian(value[(array + 4)..]);
            if (count < 0 || array + 8 + (4 * count) > value.Length) {
                return null;
            }
            var path = new List<uint>(count);
            for (var i = 0; i < count; i++) {
                path.Add(BinaryPrimitives.ReadUInt32LittleEndian(value[(array + 8 + (4 * i))..]));
            }
            return path;
        }

        /// <summary>The member a path names, as a source writes it; empty for the whole block; null if a step is not found.</summary>
        /// <remarks>
        /// An index step counts from the array's lower bound, as its declared type states it (<c>Array[1..5] of Int</c>);
        /// steps in a row are the dimensions of one array, written <c>[i, j]</c>. Bounds given by a constant are not
        /// resolved.
        /// </remarks>
        private static string? ResolveMember(IReadOnlyList<TiaMember> members, List<uint> path) {
            var text = new System.Text.StringBuilder();
            List<int>? bounds = null;
            var dimension = 0;
            foreach (var step in path) {
                var value = (int)(step & 0x00FFFFFF);
                if ((step & 0x10000000) != 0) {
                    if (bounds is null || dimension >= bounds.Count) {
                        return null;
                    }
                    text.Append(dimension == 0 ? "[" : ", ").Append((bounds[dimension] + value).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    dimension++;
                    if (dimension == bounds.Count) {
                        text.Append(']');
                    }
                    continue;
                }
                if (bounds is not null && dimension != 0 && dimension != bounds.Count) {
                    return null;
                }
                var member = members.FirstOrDefault(candidate => candidate.Lid == value);
                if (member is null) {
                    return null;
                }
                if (text.Length != 0) {
                    text.Append('.');
                }
                text.Append(QuoteIfNeeded(member.Name));
                members = member.Children;
                bounds = LowerBounds(member.Type);
                dimension = 0;
            }
            return bounds is not null && dimension != 0 && dimension != bounds.Count ? null : text.ToString();
        }

        /// <summary>The lower bound of each dimension of <c>Array[1..5, 0..2] of T</c>; null for any other type, or a bound that is not a number.</summary>
        internal static List<int>? LowerBounds(string type) {
            var end = type.IndexOf(']', StringComparison.Ordinal);
            if (!type.StartsWith("Array[", StringComparison.Ordinal) || end < 0) {
                return null;
            }
            var bounds = new List<int>();
            foreach (var range in type[6..end].Split(',')) {
                var low = range.Split("..")[0].Trim();
                if (!int.TryParse(low, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var bound)) {
                    return null;
                }
                bounds.Add(bound);
            }
            return bounds;
        }

        private static string QuoteIfNeeded(string name) => PlainName().IsMatch(name) ? name : $"\"{name}\"";

        private bool IsTag(uint type) => type == _tagType || type == _structureMemberType || type == _arrayMemberType;

        /// <summary>
        /// An HMI tag's name, or a member tag's path from its root tag: <c>Recipe.speed</c>, <c>Line.motors[2].on</c>. A
        /// member leads to its parent through <c>MembersInverse</c>, an array element through <c>ArrayElementsInverse</c>.
        /// </summary>
        private string? TagPath(in TiaFrame tag) {
            var parts = new List<string>();
            var current = tag;
            for (var depth = 0; depth < 32; depth++) {
                var name = _db.ReadString(current, _name) ?? "";
                parts.Add(name);
                if (current.TypeId == _tagType || !(_db.TryGetTarget(current, _membersInverse, out var parent) || _db.TryGetTarget(current, _elementsInverse, out parent))) {
                    break;
                }
                current = parent;
            }
            parts.Reverse();
            var path = new System.Text.StringBuilder();
            foreach (var part in parts) {
                if (path.Length != 0 && !part.StartsWith('[')) {
                    path.Append('.');
                }
                path.Append(part);
            }
            return path.ToString();
        }

        private TiaHmiAlarm ReadAlarm(TiaFrame alarm) {
            var discrete = alarm.TypeId == _discreteType;
            var alarmClass = _db.TryGetTarget(alarm, _alarmClass, out var classFrame) ? _db.ReadString(classFrame, _name) : null;
            var trigger = _db.TryGetTarget(alarm, discrete ? _discreteTrigger : _analogTrigger, out var tag) ? _db.ReadString(tag, _name) : null;
            return new TiaHmiAlarm(
                discrete ? TiaHmiAlarmKind.Discrete : TiaHmiAlarmKind.Analog,
                _db.ReadString(alarm, _name) ?? "",
                // Unified numbers its alarms across both kinds; Comfort/Advanced per kind.
                _db.ReadInteger(alarm, _uniqueId) ?? _db.ReadInteger(alarm, _alarmId),
                alarmClass,
                trigger,
                discrete ? ReadBit(alarm) : null,
                discrete ? null : _db.ReadString(alarm, _limitMode),
                discrete ? null : ReadLimit(alarm),
                ReadText(alarm));
        }

        private long? ReadBit(in TiaFrame alarm) => _db.ReadInteger(alarm, _bit);

        private string? ReadLimit(in TiaFrame alarm) => _db.ReadString(alarm, _limitValue);

        /// <summary>The event text: the alarm's child text object, a core text, without TIA's rich text markup.</summary>
        private string? ReadText(in TiaFrame alarm) {
            foreach (var relation in _db.EnumerateRelations(alarm)) {
                if (_textChildren.Matches(relation.RelationId) && relation.Target.TypeId == _textType && _db.TryGetFrame(relation.Target, out var text)) {
                    return PlainText(_db.ReadCoreText(text, _text));
                }
            }
            return null;
        }

        private TiaHmiScreen ReadScreen(TiaFrame screen) {
            var items = new List<TiaHmiScreenItem>();
            var unified = screen.TypeId == _ilScreenType;
            ReadItems(screen, unified ? _ilItems : _children, unified, items);
            return unified
                ? new TiaHmiScreen(_db.ReadString(screen, _name) ?? "", Folder(screen), _db.ReadExpandoInteger(screen, UnifiedNumber),
                    _db.ReadExpandoInteger(screen, UnifiedWidth), _db.ReadExpandoInteger(screen, UnifiedHeight), items)
                : new TiaHmiScreen(_db.ReadString(screen, _name) ?? "", Folder(screen), _db.ReadInteger(screen, _screenNumber),
                    _db.ReadInteger(screen, _screenWidth), _db.ReadInteger(screen, _screenHeight), items);
        }

        /// <summary>A screen's items in stored order, an item in a group after its group.</summary>
        private void ReadItems(in TiaFrame holder, TiaRelationHandle relation, bool unified, List<TiaHmiScreenItem> into, int depth = 0) {
            foreach (var child in _db.EnumerateRelations(holder)) {
                if (relation.Matches(child.RelationId) && child.Target.TypeId == (unified ? _ilItemType : _itemType) && _db.TryGetFrame(child.Target, out var item)) {
                    into.Add(ReadItem(item, unified));
                    if (depth < 16) {
                        ReadItems(item, relation, unified, into, depth + 1);
                    }
                }
            }
        }

        private TiaHmiScreenItem ReadItem(in TiaFrame item, bool unified) {
            var tags = new List<string>();
            foreach (var relation in _db.EnumerateRelations(item)) {
                if (_expandoReferences.Matches(relation.RelationId) && IsTag(relation.Target.TypeId) && _db.TryGetFrame(relation.Target, out var tag)
                    && TagPath(tag) is { } tagName && !tags.Contains(tagName)) {
                    tags.Add(tagName);
                }
            }
            var name = _db.ReadString(item, _name) ?? "";
            var type = _db.ReadString(item, _subtype) ?? "";
            return unified
                ? new TiaHmiScreenItem(name, type, _db.ReadExpandoInteger(item, UnifiedLeft), _db.ReadExpandoInteger(item, UnifiedTop),
                    _db.ReadExpandoInteger(item, UnifiedWidth), _db.ReadExpandoInteger(item, UnifiedHeight), tags)
                : new TiaHmiScreenItem(name, type, _db.ReadInteger(item, _itemLeft), _db.ReadInteger(item, _itemTop), _db.ReadInteger(item, _itemWidth), _db.ReadInteger(item, _itemHeight), tags);
        }
    }

    /// <summary>What belongs to one HMI application.</summary>
    private sealed class Objects {
        public static readonly Objects None = new();
        public List<TiaFrame> Connections { get; } = [];
        public List<TiaFrame> Tables { get; } = [];
        public List<TiaFrame> Links { get; } = [];
        public List<TiaFrame> Alarms { get; } = [];
        public List<TiaFrame> Screens { get; } = [];
    }

    /// <summary>
    /// The text of TIA's HMI rich text, such as <c>&lt;body&gt;&lt;p&gt;Motor fault&lt;/p&gt;&lt;/body&gt;</c>: markup
    /// left out, paragraphs on lines of their own, entities read.
    /// </summary>
    internal static string? PlainText(string? text) {
        if (text is null || !text.Contains('<', StringComparison.Ordinal)) {
            return text;
        }
        var lines = Markup().Replace(ParagraphEnd().Replace(text, "\n"), "");
        return System.Net.WebUtility.HtmlDecode(lines).Trim('\n');
    }

    [GeneratedRegex("</p>|<br\\s*/?>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEnd();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainName();
}
