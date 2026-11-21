using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>
/// An object a cross reference names: a block, a tag, a system instruction, or a member of a block's interface.
/// <see cref="Member"/> is the member's dotted name, with array indexes, or null for the object itself.
/// </summary>
public sealed record TiaCrossTarget(TiaObjectKey Key, string Kind, string Name, string? Member) {
    /// <summary>How TIA's list writes it: <c>"Recipe".steps[1].target</c> for a data block member, the member alone for a block's own one.</summary>
    public string DisplayName(TiaObjectKey user) =>
        Member is null ? Name : Key == user ? Member : "\"" + Name + "\"." + Member;
}

/// <summary>
/// One cross reference (spec 0015): <see cref="User"/> uses <see cref="Used"/> with an access in TIA's terms (Read,
/// Write, RW, Call, InstanceDB, Multiinstance, Declaration), in a network or compile unit (null for its interface), at a
/// part or token UId. <see cref="Declaration"/> names the member a use in the interface declares. A closed block's
/// references carry no place (ADR 0015).
/// </summary>
public sealed record TiaCrossReference(TiaBlock User, TiaCrossTarget Used, string Access, int? Network, int? UId, string? Declaration);

/// <summary>
/// TIA's cross-reference list, read from <c>XRef/XRef.db</c> beside the database (spec 0015, ADR 0014,
/// <c>docs/FORMAT.md</c> section 21). Only what the reader shows is read: uses of blocks, tags, data blocks, their
/// members and system instructions, by blocks. Jumps and labels are left out.
/// </summary>
public sealed class TiaCrossReferences {
    private const string XRefFile = "XRef/XRef.db";

    private TiaCrossReferences(IReadOnlyList<TiaCrossReference> references, bool isCurrent) {
        References = references;
        IsCurrent = isCurrent;
    }

    /// <summary>Every reference, in the list's order, the ones TIA derives from instance data blocks and multi-instances after.</summary>
    public IReadOnlyList<TiaCrossReference> References { get; }

    /// <summary>Whether the list belongs to this state of the database: its save point is the one the database names.</summary>
    public bool IsCurrent { get; }

    /// <summary>The list beside the database the program was opened from; null if there is none or it cannot be read.</summary>
    public static TiaCrossReferences? Read(TiaProgram program) {
        if (program.Database.SourcePath is not { } path || TiaDatabase.ReadProjectFile(path, XRefFile) is not { } bytes) {
            return null;
        }
        try {
            return Read(program, SqliteFile.Open(bytes));
        } catch (Exception e) when (e is TiaFormatException or IndexOutOfRangeException or ArgumentException or InvalidCastException) {
            return null;
        }
    }

    /// <summary>The list in a SQLite file, read against a program.</summary>
    public static TiaCrossReferences Read(TiaProgram program, SqliteFile file) => new Reader(program, file).Run();

    /// <summary>The save point TIA's database names for its cross-reference list, or null (FORMAT.md section 21).</summary>
    public static string? SavePoint(TiaDatabase database) {
        var type = database.Schema.FindObjectType("Siemens.Automation.ObjectFrame.CorePersistenceInfo");
        if (type is null) {
            return null;
        }
        ReadOnlySpan<byte> name = [5, (byte)'X', (byte)'R', (byte)'e', (byte)'f'];
        foreach (ref readonly var frame in database.Frames) {
            if (frame.TypeId != type.Id) {
                continue;
            }
            var body = database.Body(frame);
            var at = body.IndexOf(name);
            if (at < 0 || at + name.Length >= body.Length) {
                continue;
            }
            // A string of section 7.2: a varint that counts its own bytes, then the text.
            var length = body[at + name.Length];
            if (length is > 1 and < 0x80 && at + name.Length + length <= body.Length) {
                return Encoding.UTF8.GetString(body.Slice(at + name.Length + 1, length - 1));
            }
        }
        return null;
    }

    /// <summary>TIA's name of an access, by the high half of <c>relationInfo</c>; null for those the reader leaves out.</summary>
    internal static string? AccessName(long code) => code switch {
        0x1 => "Read",
        0x2 => "Write",
        0x3 => "RW",
        0x6 => "Declaration",
        0xC => "Call",
        0xF => "Multiinstance",
        0x10 => "InstanceDB",
        _ => null,
    };

    private sealed class Reader(TiaProgram program, SqliteFile file) {
        private readonly TiaDatabase _db = program.Database;
        private readonly Dictionary<TiaObjectKey, TiaBlock> _blocks = program.Blocks.Where(block => !block.IsStub).ToDictionary(block => block.Frame.Key);
        private readonly Dictionary<TiaObjectKey, Dictionary<string, string>> _members = [];
        private readonly Dictionary<long, TiaObjectKey?> _parts = [];
        private readonly Dictionary<long, (long Primary, byte[] Sub)> _objects = [];
        private readonly TiaAttributeHandle _name = program.Database.Schema.Attribute("Siemens.Automation.ObjectFrame.ICoreAttributes", "Name");
        private readonly TiaAttributeHandle _author = program.Database.Schema.Attribute("Siemens.Automation.ObjectFrame.ICoreAttributes", "Author");

        public TiaCrossReferences Run() {
            var infoTable = Table("info");
            var current = file.Rows(infoTable).FirstOrDefault().Values?[infoTable.Column("savepointID")] as string;
            var parts = Table("parts");
            foreach (var row in file.Rows(parts)) {
                _parts[row.RowId] = Part(row.Blob(parts.Column("binary")));
            }
            var objs = Table("objs");
            foreach (var row in file.Rows(objs)) {
                _objects[row.RowId] = (row.Integer(objs.Column("primaryObject")), row.Blob(objs.Column("subObject")));
            }
            var rels = Table("rels");
            var references = new List<TiaCrossReference>();
            var instanceOf = new Dictionary<TiaObjectKey, TiaObjectKey>();
            var seen = new HashSet<(TiaObjectKey, TiaCrossTarget, string, int?, int?)>();
            foreach (var row in file.Rows(rels)) {
                var info = row.Integer(rels.Column("relationInfo"));
                var left = Object(row.Integer(rels.Column("left")));
                var right = Object(row.Integer(rels.Column("right")));
                if (left is null || right is null) {
                    continue;
                }
                // An instance data block names its function block in the inverse direction only.
                if ((info & 0xFFFF) == 2 && info >> 16 == 0x10) {
                    instanceOf[left.Value.Key] = right.Value.Key;
                    continue;
                }
                if ((info & 0xFFFF) != 1 || AccessName(info >> 16) is not { } access || left.Value.Sub.Length != 0
                    || !_blocks.TryGetValue(left.Value.Key, out var user) || Target(right.Value, row.Blob(rels.Column("rightFilter"))) is not { } used) {
                    continue;
                }
                var location = row.Blob(rels.Column("location"));
                var unit = location.Length >= 8 ? BinaryPrimitives.ReadInt32LittleEndian(location) : 0;
                var uid = location.Length >= 8 ? BinaryPrimitives.ReadInt32LittleEndian(location.AsSpan(4)) : 0;
                string? declaration = null;
                if (unit == 0 && uid != 0) {
                    // A use in the interface: the second number is the id of the member it declares.
                    declaration = Members(user).GetValueOrDefault(uid.ToString(CultureInfo.InvariantCulture));
                }
                var reference = new TiaCrossReference(user, used, access, unit == 0 ? null : unit, unit == 0 ? null : uid, declaration);
                if (user.IsKnowHowProtected) {
                    // ADR 0015: what a closed block uses is shown, its own members and the places inside it are not.
                    if (used.Key == user.Frame.Key) {
                        continue;
                    }
                    reference = reference with { Network = null, UId = null, Declaration = null };
                }
                if (seen.Add((user.Frame.Key, used, access, reference.Network, reference.UId))) {
                    references.Add(reference);
                }
            }
            references.AddRange(Derived(references, instanceOf, seen));
            var stored = SavePoint(_db);
            return new TiaCrossReferences(references, current is not null && current == stored);
        }

        /// <summary>
        /// The calls TIA shows that the list holds only as a use: a call through an instance data block calls its function
        /// block, and a call through a multi-instance calls the type of the member.
        /// </summary>
        private List<TiaCrossReference> Derived(List<TiaCrossReference> references, Dictionary<TiaObjectKey, TiaObjectKey> instanceOf, HashSet<(TiaObjectKey, TiaCrossTarget, string, int?, int?)> seen) {
            var types = references.Where(reference => reference.Access == "Multiinstance" && reference.Network is null && reference.Declaration is not null)
                .GroupBy(reference => (reference.User.Frame.Key, reference.Declaration!))
                .ToDictionary(group => group.Key, group => group.First().Used);
            var derived = new List<TiaCrossReference>();
            // A call the list already holds in the same network is not derived a second time.
            var calls = references.Where(reference => reference.Access == "Call").Select(reference => (reference.User.Frame.Key, reference.Used, reference.Network)).ToHashSet();
            foreach (var reference in references) {
                TiaCrossTarget? called = null;
                if (reference.Access == "InstanceDB" && instanceOf.TryGetValue(reference.Used.Key, out var type) && _blocks.TryGetValue(type, out var block)) {
                    called = new TiaCrossTarget(type, block.Kind, block.Name, null);
                } else if (reference is { Access: "Multiinstance", Network: not null } && reference.Used.Key == reference.User.Frame.Key && reference.Used.Member is { } member
                    && types.TryGetValue((reference.User.Frame.Key, member), out var memberType)) {
                    called = memberType;
                }
                if (called is not null && calls.Add((reference.User.Frame.Key, called, reference.Network)) && seen.Add((reference.User.Frame.Key, called, "Call", reference.Network, reference.UId))) {
                    derived.Add(reference with { Used = called, Access = "Call", Declaration = null });
                }
            }
            return derived;
        }

        private SqliteTable Table(string name) => file.FindTable(name) ?? throw new TiaFormatException($"XRef.db has no table {name}.");

        private static TiaObjectKey? Part(byte[] binary) =>
            binary.Length == 14 && binary[0] == (byte)'O' && binary[1] == (byte)'G'
                ? new TiaObjectKey(BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(2)), BinaryPrimitives.ReadUInt64LittleEndian(binary.AsSpan(6)))
                : null;

        /// <summary>An object of the list: what it is in the database, and the member ids below it.</summary>
        private (TiaObjectKey Key, int[] Sub)? Object(long id) {
            if (!_objects.TryGetValue(id, out var row) || _parts.GetValueOrDefault(row.Primary) is not { } key) {
                return null;
            }
            var sub = row.Sub.Length >= 4 && row.Sub.Length % 4 == 0
                ? Enumerable.Range(0, row.Sub.Length / 4).Select(i => BinaryPrimitives.ReadInt32LittleEndian(row.Sub.AsSpan(i * 4))).ToArray()
                : [];
            return (key, sub);
        }

        private TiaCrossTarget? Target((TiaObjectKey Key, int[] Sub) target, byte[] filter) {
            if (!_db.TryGetFrame(target.Key, out var frame)) {
                return null;
            }
            var type = _db.Schema.FindObjectType(frame.TypeId)?.FullName ?? "";
            if (_blocks.TryGetValue(target.Key, out var block)) {
                string? member = null;
                if (target.Sub.Length != 0) {
                    member = MemberName(block, target.Sub, filter);
                    if (member is null) {
                        return null;
                    }
                }
                return new TiaCrossTarget(target.Key, block.Kind, block.Name, member);
            }
            if (target.Sub.Length != 0) {
                return null;
            }
            return type switch {
                "Siemens.Automation.DomainModel.EAMTZTagData" => new TiaCrossTarget(target.Key, "Tag", _db.ReadString(frame, _name) ?? "", null),
                // A system instruction is a proxy, named by its author.
                "Siemens.Simatic.Lang.Model.Blocks.IECPLInstructionProxyData" => new TiaCrossTarget(target.Key, "Instruction", _db.ReadString(frame, _author) ?? "", null),
                _ => null,
            };
        }

        private Dictionary<string, string> Members(TiaBlock block) {
            if (!_members.TryGetValue(block.Frame.Key, out var names)) {
                names = program.MemberNamesById(block);
                _members[block.Frame.Key] = names;
            }
            return names;
        }

        /// <summary>
        /// The dotted name of a member by its id path; an array index from the filter goes after the segment it names:
        /// <c>02</c>, a count, then per index a u16 segment, counted from 1, and an i32 index (FORMAT.md section 21).
        /// </summary>
        private string? MemberName(TiaBlock block, int[] ids, byte[] filter) {
            var names = Members(block);
            var segments = new List<string>();
            for (var i = 0; i < ids.Length; i++) {
                var path = string.Join(':', ids.Take(i + 1).Select(id => id.ToString(CultureInfo.InvariantCulture)));
                if (!names.TryGetValue(path, out var name)) {
                    return null;
                }
                segments.Add(name[(name.LastIndexOf('.') + 1)..]);
            }
            if (filter.Length >= 2 && filter[0] == 2) {
                for (var i = 0; i < filter[1] && 2 + (i * 6) + 6 <= filter.Length; i++) {
                    var segment = BinaryPrimitives.ReadUInt16LittleEndian(filter.AsSpan(2 + (i * 6))) - 1;
                    var index = BinaryPrimitives.ReadInt32LittleEndian(filter.AsSpan(4 + (i * 6)));
                    if (segment >= 0 && segment < segments.Count) {
                        segments[segment] += string.Create(CultureInfo.InvariantCulture, $"[{index}]");
                    }
                }
            }
            return string.Join('.', segments);
        }
    }
}
