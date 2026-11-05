using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TiaFormat.Schema;

namespace TiaFormat.Storage;

/// <summary>
/// One opened <c>PEData.plf</c>: the current object of each id, the schema, and typed reads that allocate only what
/// they return. Immutable after opening, so reads may run in parallel.
/// </summary>
public sealed class TiaDatabase {
    private const int HeaderSize = 98;
    private const int FrameHeadSize = 44;
    private const int HashSize = 32;
    private const uint SchemaType = 0x70000;
    private const uint ExpandoMappingType = 0x70002;
    private const int SystemPayloadHead = 4;
    private const int ExpandoEntries = 18;
    private const uint SegmentType = 0x70013;
    private const uint CommitType = 0x70014;
    private const uint DeflateMagic = 0x6007BDED;
    private const int KindSmall = 0;
    private const int KindCompressedSmall = 1;
    private const int KindLarge = 4;

    private readonly byte[] _data;
    private readonly TiaFrame[] _frames;
    private readonly Dictionary<TiaObjectKey, int> _index;
    private readonly Lazy<Dictionary<uint, Dictionary<uint, string>>> _expandoKeys;

    private TiaDatabase(byte[] data, TiaFrame[] frames, Dictionary<TiaObjectKey, int> index, int allFrameCount, TiaSchema schema) {
        _data = data;
        _frames = frames;
        _index = index;
        AllFrameCount = allFrameCount;
        Schema = schema;
        _expandoKeys = new Lazy<Dictionary<uint, Dictionary<uint, string>>>(ReadExpandoKeys);
    }

    public TiaSchema Schema { get; }

    /// <summary>Frames in the file, old versions and system frames included.</summary>
    public int AllFrameCount { get; }

    /// <summary>The current object of each id, in file order.</summary>
    public ReadOnlySpan<TiaFrame> Frames => _frames;

    /// <summary>Product version, such as 2000 for V20.</summary>
    public int ProductMajor => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(24));

    /// <summary>Opens a <c>.zap*</c> archive, a project or library folder, or a <c>PEData.plf</c> itself.</summary>
    public static TiaDatabase Open(string path, bool verifyHashes = false) => Parse(ReadPlf(path), verifyHashes);

    /// <summary>Reads a database held in memory; the array is kept, not copied.</summary>
    public static TiaDatabase Parse(byte[] data, bool verifyHashes = false) {
        if (data.Length < HeaderSize) {
            throw new TiaFormatException("Too short for a file header.");
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24)) < 1400) {
            throw new TiaFormatException("Files of a product version below 1400 use another layout, which this reader does not know.");
        }

        var all = new List<TiaFrame>(data.Length / 1024);
        var lastCommit = -1;
        var schemaFrame = -1;
        var offset = HeaderSize;
        while (offset + FrameHeadSize + HashSize <= data.Length) {
            var frame = ReadFrameHead(data, offset, all.Count);
            if (frame.Size < FrameHeadSize || (long)offset + frame.Size + HashSize > data.Length) {
                throw new TiaFormatException($"Frame at {offset} has an impossible size {frame.Size}.");
            }
            if (verifyHashes) {
                VerifyHash(data, frame);
            }
            if (frame.TypeId == CommitType) {
                lastCommit = all.Count;
            }
            if (frame.TypeId == SchemaType) {
                schemaFrame = all.Count;
            }
            all.Add(frame);
            offset += frame.Size + HashSize;
        }
        if (offset != data.Length) {
            throw new TiaFormatException($"The frame walk ended at {offset} of {data.Length} bytes.");
        }
        if (schemaFrame < 0) {
            throw new TiaFormatException("The file holds no schema.");
        }

        // The file is a log: per id the last frame up to the last commit point wins, and a deleted last frame ends the object.
        var limit = lastCommit >= 0 ? lastCommit : all.Count - 1;
        var latest = new Dictionary<TiaObjectKey, int>(all.Count);
        for (var i = 0; i <= limit; i++) {
            latest[all[i].Key] = i;
        }
        var current = new List<TiaFrame>(latest.Count);
        for (var i = 0; i <= limit; i++) {
            var frame = all[i];
            if (latest[frame.Key] == i && !frame.IsDeleted) {
                current.Add(frame);
            }
        }
        var index = new Dictionary<TiaObjectKey, int>(current.Count);
        for (var i = 0; i < current.Count; i++) {
            index[current[i].Key] = i;
        }

        var schema = ParseSchema(data, all[schemaFrame]);
        return new TiaDatabase(data, [.. current], index, all.Count, schema);
    }

    public bool TryGetFrame(TiaObjectKey key, out TiaFrame frame) {
        if (_index.TryGetValue(key, out var i)) {
            frame = _frames[i];
            return true;
        }
        frame = default;
        return false;
    }

    /// <summary>The frame's interface segment; false if the object has none for this interface.</summary>
    public bool TryGetSegment(in TiaFrame frame, int interfaceIndex, out int segmentOffset, out int segmentSize) {
        segmentOffset = 0;
        segmentSize = 0;
        if (interfaceIndex >= frame.OffsetCount) {
            return false;
        }
        var relative = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(frame.Offset + FrameHeadSize + (interfaceIndex * 4)));
        if (relative == 0) {
            return false;
        }
        segmentOffset = frame.Offset + relative;
        segmentSize = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(segmentOffset)), frame.Offset + frame.Size - segmentOffset);
        return true;
    }

    /// <summary>
    /// Where an attribute's slot is. False when the type has no such attribute, the object no such segment, or the
    /// segment is shorter than the schema: an object written under an older schema simply lacks that attribute.
    /// </summary>
    private bool TryLocate(in TiaFrame frame, TiaAttributeHandle attribute, out int slotOffset, out int segmentOffset, out TiaAttributeSlot slot) {
        slotOffset = 0;
        segmentOffset = 0;
        if (!attribute.TryResolve(frame.TypeId, out var interfaceIndex, out slot) || !TryGetSegment(frame, interfaceIndex, out segmentOffset, out var size)) {
            return false;
        }
        if (slot.Offset + slot.Type.Size > size) {
            return false;
        }
        slotOffset = segmentOffset + slot.Offset;
        return true;
    }

    /// <summary>
    /// Follows a slot's offset to its value. Null, or an offset outside the frame, means no value: a few objects in
    /// old libraries do not fit the schema for some attributes, and one such value must not cost the whole object.
    /// </summary>
    private bool TryFollow(in TiaFrame frame, int slotOffset, int segmentOffset, out int value) {
        var relative = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(slotOffset));
        value = segmentOffset + relative;
        return relative > 0 && (long)segmentOffset + relative < frame.Offset + frame.Size;
    }

    public bool ReadBoolean(in TiaFrame frame, TiaAttributeHandle attribute) =>
        TryLocate(frame, attribute, out var at, out _, out _) && _data[at] != 0;

    /// <summary>An integer or enumeration attribute, or null if absent.</summary>
    public long? ReadInteger(in TiaFrame frame, TiaAttributeHandle attribute) {
        if (!TryLocate(frame, attribute, out var at, out _, out var slot)) {
            return null;
        }
        var span = _data.AsSpan(at);
        var kind = slot.Type.Kind == TiaValueKind.Enumeration ? slot.Type.StorageKind : slot.Type.Kind;
        return kind switch {
            TiaValueKind.UInt8 or TiaValueKind.Boolean => span[0],
            TiaValueKind.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span),
            TiaValueKind.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            TiaValueKind.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span),
            TiaValueKind.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            TiaValueKind.Int64 => BinaryPrimitives.ReadInt64LittleEndian(span),
            TiaValueKind.UInt64 => BinaryPrimitives.ReadUInt64LittleEndian(span) is var value && value <= long.MaxValue
                ? (long)value
                : throw new TiaFormatException($"Attribute {slot.Name} holds a value above the range of this read; use {nameof(ReadUnsignedInteger)}."),
            _ => throw new TiaFormatException($"Attribute {slot.Name} of type {slot.Type.Name} is not an integer."),
        };
    }

    /// <summary>An <c>xs:ulong</c> attribute over its whole range, or null if absent.</summary>
    public ulong? ReadUnsignedInteger(in TiaFrame frame, TiaAttributeHandle attribute) {
        if (!TryLocate(frame, attribute, out var at, out _, out var slot)) {
            return null;
        }
        return slot.Type.Kind == TiaValueKind.UInt64
            ? BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(at))
            : throw new TiaFormatException($"Attribute {slot.Name} of type {slot.Type.Name} is not an unsigned 64 bit integer.");
    }

    /// <summary>A string, or an enumeration's constant name, or null if absent.</summary>
    public string? ReadString(in TiaFrame frame, TiaAttributeHandle attribute) {
        if (!TryLocate(frame, attribute, out var at, out var segment, out var slot)) {
            return null;
        }
        if (slot.Type.Kind == TiaValueKind.Enumeration) {
            var value = ReadInteger(frame, attribute) ?? 0;
            return slot.Type.Constants!.GetValueOrDefault(value) ?? value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (slot.Type.Kind != TiaValueKind.String) {
            throw new TiaFormatException($"Attribute {slot.Name} of type {slot.Type.Name} is not a string.");
        }
        if (!TryFollow(frame, at, segment, out var start)) {
            return null;
        }
        var length = ReadVarInt(_data, start, out var body);
        var bytes = length - (body - start);
        return bytes < 0 || body + bytes > frame.Offset + frame.Size ? null : Encoding.UTF8.GetString(_data, body, bytes);
    }

    /// <summary>The preferred language of a core text attribute: English if present, else the first; null if absent.</summary>
    public string? ReadCoreText(in TiaFrame frame, TiaAttributeHandle attribute) {
        if (!TryLocate(frame, attribute, out var at, out var segment, out _)) {
            return null;
        }
        return TryFollow(frame, at, segment, out var value) ? ReadCoreTextAt(value) : null;
    }

    /// <summary>A blob or XML attribute, decoded into pooled memory. Empty if absent.</summary>
    public PooledBytes ReadBlob(in TiaFrame frame, TiaAttributeHandle attribute) {
        if (!TryLocate(frame, attribute, out var at, out var segment, out _)) {
            return default;
        }
        return TryFollow(frame, at, segment, out var value) ? DecodeBlob(value) : default;
    }

    /// <summary>Relations of a frame in stored order. Empty list places are included, because list indexes count them.</summary>
    public RelationEnumerator EnumerateRelations(in TiaFrame frame) => new(this, frame, Schema.FindObjectType(frame.TypeId));

    /// <summary>The first existing target of a relation.</summary>
    public bool TryGetTarget(in TiaFrame frame, TiaRelationHandle relation, out TiaFrame target) {
        foreach (var r in EnumerateRelations(frame)) {
            if (relation.Matches(r.RelationId) && TryGetFrame(r.Target, out target)) {
                return true;
            }
        }
        target = default;
        return false;
    }

    /// <summary>
    /// A blob or XML value of a dictionary expando interface, by the name of its key; empty if the object has none.
    /// The names come from the object type's key table, reached through the mapping table.
    /// </summary>
    public PooledBytes ReadExpandoBlob(in TiaFrame frame, string name) =>
        TryFindExpandoValue(frame, name, out var value, out var heap) && value != uint.MaxValue ? DecodeBlob(heap + (int)value) : default;

    /// <summary>A boolean value of a dictionary expando interface, by the name of its key; null if the object has none.</summary>
    public bool? ReadExpandoBoolean(in TiaFrame frame, string name) =>
        TryFindExpandoValue(frame, name, out var value, out _) ? value != 0 : null;

    /// <summary>The value's slot: the value itself if it fits, else its offset from the returned heap.</summary>
    private bool TryFindExpandoValue(in TiaFrame frame, string name, out uint value, out int heap) {
        value = 0;
        heap = 0;
        var type = Schema.FindObjectType(frame.TypeId);
        if (type is null || !_expandoKeys.Value.TryGetValue(frame.TypeId, out var keys)) {
            return false;
        }
        for (var i = 0; i < type.Interfaces.Length; i++) {
            if (type.Interfaces[i].Set.Expando != TiaExpandoMode.Dictionary || !TryGetSegment(frame, i, out var segment, out var size) || size < ExpandoEntries) {
                continue;
            }
            var count = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(segment + 8));
            var keysAt = segment + ExpandoEntries;
            var valuesAt = keysAt + (4 * count);
            for (var entry = 0; entry < count; entry++) {
                if (!keys.TryGetValue(BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(keysAt + (4 * entry))), out var key) || key != name) {
                    continue;
                }
                value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(valuesAt + (4 * entry)));
                heap = valuesAt + (4 * count);
                return true;
            }
        }
        return false;
    }

    private Dictionary<uint, Dictionary<uint, string>> ReadExpandoKeys() {
        var result = new Dictionary<uint, Dictionary<uint, string>>();
        foreach (ref readonly var frame in _frames.AsSpan()) {
            if (frame.TypeId != ExpandoMappingType) {
                continue;
            }
            var at = frame.Offset + FrameHeadSize + SystemPayloadHead;
            var count = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at));
            at += 4;
            for (var i = 0; i < count; i++, at += 16) {
                var table = new TiaObjectKey(BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at + 4)), BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(at + 8)));
                if (TryGetFrame(table, out var keys)) {
                    result[BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at))] = ReadKeyTable(keys);
                }
            }
        }
        return result;
    }

    /// <summary>12 bytes, u32 count, then each key: u32 key, varint length of the name alone, name, u32 value type.</summary>
    private Dictionary<uint, string> ReadKeyTable(in TiaFrame frame) {
        var result = new Dictionary<uint, string>();
        var at = frame.Offset + FrameHeadSize + SystemPayloadHead + 12;
        var count = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at));
        at += 4;
        for (var i = 0; i < count; i++) {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at));
            var length = ReadVarInt(_data, at + 4, out var name);
            result[key] = Encoding.UTF8.GetString(_data, name, length);
            at = name + length + 4;
        }
        return result;
    }

    /// <summary>The comment texts of a simple expando interface, by their key.</summary>
    public Dictionary<string, string> ReadSimpleExpandoTexts(in TiaFrame frame) {
        var result = new Dictionary<string, string>();
        var type = Schema.FindObjectType(frame.TypeId);
        if (type is null) {
            return result;
        }
        for (var i = 0; i < type.Interfaces.Length; i++) {
            if (type.Interfaces[i].Set.Expando != TiaExpandoMode.Simple || !TryGetSegment(frame, i, out var segment, out _)) {
                continue;
            }
            ReadSimpleExpando(frame, segment, result);
        }
        return result;
    }

    private void ReadSimpleExpando(in TiaFrame frame, int segment, Dictionary<string, string> result) {
        const uint CoreTextTypeId = 0x8000001f;
        var count = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(segment + 8));
        if (count <= 0) {
            return;
        }
        var typesAt = segment + 18;
        var namesAt = typesAt + (4 * count);
        var names = ArrayPool<(int Start, int Length)>.Shared.Rent(count);
        try {
            var valueTable = 0;
            for (var i = 0; i < count; i++) {
                var nameStart = segment + BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(namesAt + (4 * i)));
                var end = nameStart;
                while (_data[end] != 0 || _data[end + 1] != 0) {
                    end += 2;
                }
                names[i] = (nameStart, end - nameStart);
                valueTable = end + 2;
            }
            var valueBase = valueTable + (4 * count);
            for (var i = 0; i < count; i++) {
                var type = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(typesAt + (4 * i)));
                var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(valueTable + (4 * i)));
                if (type != CoreTextTypeId || value == uint.MaxValue || valueBase + value >= frame.Offset + frame.Size) {
                    continue;
                }
                result[Encoding.Unicode.GetString(_data, names[i].Start, names[i].Length)] = ReadCoreTextAt(valueBase + (int)value) ?? "";
            }
        } finally {
            ArrayPool<(int, int)>.Shared.Return(names);
        }
    }

    private string? ReadCoreTextAt(int at) {
        ReadVarInt(_data, at, out var p);
        // size, unknown, default index, then the string table.
        p += 12;
        var count = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(p));
        p += 4;
        var ids = p;
        var present = ids + (2 * count);
        p = present + (4 * count);
        string? first = null;
        for (var i = 0; i < count; i++) {
            if (BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(present + (4 * i))) == 0) {
                continue;
            }
            var length = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(p));
            p += 4;
            var language = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(ids + (2 * i)));
            // An empty English text beside a German one is common: it says nothing, so the other language stands.
            if (language == 1033 && length != 0) {
                return Encoding.UTF8.GetString(_data, p, length);
            }
            if (length != 0) {
                first ??= Encoding.UTF8.GetString(_data, p, length);
            }
            p += length;
        }
        return first;
    }

    private PooledBytes DecodeBlob(int at) {
        var total = ReadVarInt(_data, at, out var head);
        if (total <= head - at) {
            return default;
        }
        var kind = _data[head];
        if (kind is not (KindSmall or KindCompressedSmall or KindLarge)) {
            throw new TiaFormatException($"Blob kind {kind} at {at} is not known.");
        }
        var length = checked((int)BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(head + 1)));
        var pageCount = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(head + 15));
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try {
            if (kind == KindLarge) {
                var words = ReadVarInt(_data, head + 19, out var bitmap);
                var written = 0;
                var reference = bitmap + (words * 4);
                var segments = (pageCount + 31) / 32;
                var pageSize = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(head + 13));
                for (var i = 0; i < segments && written < length; i++, reference += 12) {
                    var type = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(reference));
                    var id = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(reference + 4));
                    if (type == 0) {
                        var zeros = Math.Min(32 * pageSize, length - written);
                        buffer.AsSpan(written, zeros).Clear();
                        written += zeros;
                        continue;
                    }
                    if (type != SegmentType || !TryGetFrame(new TiaObjectKey(type, id), out var segment)) {
                        throw new TiaFormatException($"Large blob at {at} refers to a missing segment {type:x}/{id}.");
                    }
                    written += DecodePages(segment.Offset + FrameHeadSize + 4, buffer.AsSpan(written, length - written));
                }
                if (written != length) {
                    throw new TiaFormatException($"Large blob at {at}: {written} of {length} bytes.");
                }
            } else {
                // Fewer pages than the head's length would leave the rest of the rented array, another renter's bytes, in the value.
                var written = DecodePages(head, buffer.AsSpan(0, length));
                if (written != length) {
                    throw new TiaFormatException($"Blob at {at}: {written} of {length} bytes.");
                }
            }
        } catch {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
        return new PooledBytes(buffer, length);
    }

    /// <summary>Decodes a head with kind 0 or 1 and its pages. Returns the head's own decoded length.</summary>
    private int DecodePages(int head, Span<byte> destination) {
        var kind = _data[head];
        var length = checked((int)BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(head + 1)));
        var pageSize = (int)BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(head + 13));
        if (pageSize == 0) {
            pageSize = 4096;
        }
        var pageCount = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(head + 15));
        var words = ReadVarInt(_data, head + 19, out var bitmap);
        var p = bitmap + (words * 4);
        var left = length;
        var written = 0;
        for (var i = 0; i < pageCount; i++) {
            var want = Math.Min(pageSize, left);
            var word = i >> 5;
            var stored = word < words && (BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(bitmap + (word * 4))) >> (i & 31) & 1) != 0;
            var page = destination.Slice(written, want);
            if (!stored) {
                page.Clear();
            } else if (kind == KindSmall) {
                _data.AsSpan(p, want).CopyTo(page);
                p += want;
            } else {
                var size = ReadVarInt(_data, p, out p);
                InflatePage(p, size, page);
                p += size;
            }
            written += want;
            left -= want;
        }
        return written;
    }

    private void InflatePage(int at, int size, Span<byte> page) {
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at)) == DeflateMagic;
        using var input = raw ? new MemoryStream(_data, at + 4, size - 4, false) : new MemoryStream(_data, at, size, false);
        using Stream inflater = raw ? new DeflateStream(input, CompressionMode.Decompress) : new ZLibStream(input, CompressionMode.Decompress);
        inflater.ReadAtLeast(page, page.Length, throwOnEndOfStream: true);
    }

    private static TiaFrame ReadFrameHead(byte[] data, int offset, int sequence) {
        var head = data.AsSpan(offset, FrameHeadSize);
        return new TiaFrame(
            offset,
            BinaryPrimitives.ReadInt32LittleEndian(head),
            BinaryPrimitives.ReadUInt32LittleEndian(head[4..]),
            BinaryPrimitives.ReadUInt64LittleEndian(head[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(head[16..]),
            (TiaFrameInfo)BinaryPrimitives.ReadUInt32LittleEndian(head[20..]),
            (TiaFrameStates)BinaryPrimitives.ReadUInt16LittleEndian(head[24..]),
            head[26],
            head[27],
            sequence);
    }

    private static void VerifyHash(byte[] data, in TiaFrame frame) {
        Span<byte> hash = stackalloc byte[HashSize];
        SHA256.HashData(data.AsSpan(frame.Offset, frame.Size), hash);
        if (!hash.SequenceEqual(data.AsSpan(frame.Offset + frame.Size, HashSize))) {
            throw new TiaFormatException($"Frame at {frame.Offset} has a wrong hash.");
        }
    }

    private static TiaSchema ParseSchema(byte[] data, in TiaFrame frame) {
        var body = frame.Offset + FrameHeadSize;
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(body));
        using var input = new MemoryStream(data, body + 4, length, false);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        return TiaSchema.Parse(inflater);
    }

    internal static int ReadVarInt(byte[] data, int at, out int next) {
        var value = 0;
        var shift = 0;
        while (true) {
            var b = data[at++];
            value |= (b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0) {
                next = at;
                return value;
            }
        }
    }

    private static byte[] ReadPlf(string path) {
        if (Directory.Exists(path)) {
            var inFolder = Path.Combine(path, "System", "PEData.plf");
            return File.ReadAllBytes(File.Exists(inFolder) ? inFolder : Path.Combine(path, "PEData.plf"));
        }
        if (Path.GetExtension(path).StartsWith(".zap", StringComparison.OrdinalIgnoreCase)) {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("System/PEData.plf", StringComparison.OrdinalIgnoreCase))
                ?? throw new TiaFormatException($"{path} holds no System/PEData.plf.");
            var bytes = new byte[entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(bytes);
            return bytes;
        }
        return File.ReadAllBytes(path);
    }

    /// <summary>Walks a frame's relation lists without allocating. See the format page for the three list forms.</summary>
    public ref struct RelationEnumerator {
        private readonly byte[] _data;
        private readonly int _frameOffset;
        private readonly int _count;
        private readonly int _first;
        private readonly bool _lowFilled;
        private readonly TiaObjectType? _type;
        private int _entry;
        private int _slot;
        private int _end;
        private uint _listRelation;
        private bool _inList;
        private bool _perRelation;

        internal RelationEnumerator(TiaDatabase database, in TiaFrame frame, TiaObjectType? type) {
            _data = database._data;
            _frameOffset = frame.Offset;
            _count = frame.OffsetCount;
            _type = type;
            _first = type?.Interfaces.Length ?? _count;
            _lowFilled = (frame.Info & TiaFrameInfo.LowFilledRelations) != 0;
            _entry = _first;
        }

        public TiaRelation Current { get; private set; }

        public readonly RelationEnumerator GetEnumerator() => this;

        public bool MoveNext() {
            while (true) {
                if (_inList && TryNextInList()) {
                    return true;
                }
                _inList = false;
                if (_entry >= _count || _type is null) {
                    return false;
                }
                OpenList();
            }
        }

        private bool TryNextInList() {
            if (_perRelation) {
                if (_slot + 12 > _end) {
                    return false;
                }
                Current = new TiaRelation(_listRelation, new TiaObjectKey(
                    BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_slot)),
                    BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_slot + 4))));
                _slot += 12;
                return true;
            }
            while (_slot + 16 <= _end) {
                var relation = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_slot));
                var target = new TiaObjectKey(
                    BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_slot + 4)),
                    BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_slot + 8)));
                _slot += 16;
                if (relation != 0) {
                    Current = new TiaRelation(relation, target);
                    return true;
                }
            }
            return false;
        }

        private void OpenList() {
            var index = _entry - _first;
            var relative = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_frameOffset + FrameHeadSize + (_entry * 4)));
            _entry++;
            var slotted = index == 0 || (index == 1 && _lowFilled);
            if (relative == 0 || (!slotted && _type!.IsCoreText)) {
                return;
            }
            var at = _frameOffset + relative;
            if (slotted) {
                _perRelation = false;
                _slot = at + 4;
                _end = at + BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at));
            } else {
                var k = index - (_lowFilled ? 2 : 1);
                _perRelation = true;
                _listRelation = k < _type!.ToManyRelations.Length ? _type.ToManyRelations[k].Id : 0;
                _slot = at + 12;
                _end = _slot + (BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at + 8)) * 12);
            }
            _inList = true;
        }
    }
}
