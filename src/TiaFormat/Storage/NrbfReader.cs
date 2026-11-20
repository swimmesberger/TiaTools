using System.Buffers.Binary;
using System.Text;

namespace TiaFormat.Storage;

/// <summary>An object of a .NET binary serialization stream: its type's name and its members by name.</summary>
internal sealed class NrbfObject(string typeName, Dictionary<string, object?> members) {
    public string TypeName { get; } = typeName;

    public Dictionary<string, object?> Members { get; } = members;

    public object? this[string name] => Members.GetValueOrDefault(name);
}

/// <summary>
/// Reads a stream in the .NET Remoting binary format (MS-NRBF), which TIA uses for some blobs, into plain values:
/// <see cref="NrbfObject"/>, <c>object?[]</c>, strings and primitives. Only reads: it instantiates no type the stream
/// names, so nothing in it runs.
/// </summary>
internal sealed class NrbfReader {
    private const int MaxDepth = 256;
    private const int MaxElements = 1 << 24;

    private readonly byte[] _data;
    private readonly Dictionary<int, object?> _objects = [];
    private readonly Dictionary<int, ClassShape> _shapes = [];
    private readonly List<Pending> _pending = [];
    private int _at;

    private NrbfReader(byte[] data, int start) {
        _data = data;
        _at = start;
    }

    /// <summary>The header of a stream: record 0, a root id, header id -1, version 1.0.</summary>
    private static ReadOnlySpan<byte> HeaderTail => [0xFF, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>The root object of the first stream found within the first bytes, or null if there is none.</summary>
    public static object? Read(ReadOnlySpan<byte> bytes, int searchWithin = 16) {
        for (var start = 0; start <= searchWithin && start + 17 <= bytes.Length; start++) {
            if (bytes[start] == 0 && bytes.Slice(start + 5, HeaderTail.Length).SequenceEqual(HeaderTail)) {
                return new NrbfReader(bytes.ToArray(), start).ReadStream();
            }
        }
        return null;
    }

    private object? ReadStream() {
        _at++;
        var root = ReadInt32();
        _at += 12;
        while (true) {
            var type = ReadByte();
            if (type == 11) {
                break;
            }
            ReadRecord(type, 0);
        }
        foreach (var pending in _pending) {
            var target = _objects.GetValueOrDefault(pending.Id);
            if (pending.Owner is NrbfObject owner) {
                owner.Members[pending.Member!] = target;
            } else {
                ((object?[])pending.Owner)[pending.Index] = target;
            }
        }
        return _objects.GetValueOrDefault(root);
    }

    /// <summary>One record; a null run comes back as <see cref="NullRun"/>, a reference as <see cref="Reference"/>.</summary>
    private object? ReadRecord(byte type, int depth) {
        if (depth > MaxDepth) {
            throw new TiaFormatException("A serialized value nests too deep.");
        }
        switch (type) {
            case 1: {
                var id = ReadInt32();
                var shape = _shapes.GetValueOrDefault(ReadInt32()) ?? throw new TiaFormatException("A serialized object refers to a class not yet described.");
                _shapes[id] = shape;
                return ReadMembers(id, shape, depth);
            }
            case 2 or 3 or 4 or 5: {
                var id = ReadInt32();
                var name = ReadString();
                var count = ReadCount();
                var names = new string[count];
                for (var i = 0; i < count; i++) {
                    names[i] = ReadString();
                }
                var kinds = new byte[count];
                var extras = new byte[count];
                if (type is 4 or 5) {
                    for (var i = 0; i < count; i++) {
                        kinds[i] = ReadByte();
                    }
                    for (var i = 0; i < count; i++) {
                        extras[i] = ReadTypeInfo(kinds[i]);
                    }
                } else {
                    Array.Fill(kinds, (byte)2);
                }
                if (type is 3 or 5) {
                    _at += 4;
                }
                var shape = new ClassShape(name, names, kinds, extras);
                _shapes[id] = shape;
                return ReadMembers(id, shape, depth);
            }
            case 6: {
                var id = ReadInt32();
                var text = ReadString();
                _objects[id] = text;
                return text;
            }
            case 7: {
                var id = ReadInt32();
                var arrayType = ReadByte();
                var rank = ReadInt32();
                long total = 1;
                for (var i = 0; i < rank; i++) {
                    total *= ReadCount();
                }
                if (arrayType is 3 or 4 or 5) {
                    _at += 4 * rank;
                }
                var kind = ReadByte();
                var extra = ReadTypeInfo(kind);
                return ReadElements(id, (int)Math.Min(total, MaxElements + 1L), kind, extra, depth);
            }
            case 8:
                return ReadPrimitive(ReadByte());
            case 9:
                return new Reference(ReadInt32());
            case 10:
                return null;
            case 12:
                _at += 4;
                ReadString();
                return Skip.Instance;
            case 13:
                return new NullRun(ReadByte());
            case 14:
                return new NullRun(ReadCount());
            case 15: {
                var id = ReadInt32();
                var count = ReadCount();
                return ReadElements(id, count, 0, ReadByte(), depth);
            }
            case 16 or 17: {
                var id = ReadInt32();
                return ReadElements(id, ReadCount(), 2, 0, depth);
            }
            default:
                throw new TiaFormatException($"Serialized record type {type} is not known.");
        }
    }

    private NrbfObject ReadMembers(int id, ClassShape shape, int depth) {
        var result = new NrbfObject(shape.Name, new Dictionary<string, object?>(shape.Names.Length));
        _objects[id] = result;
        for (var i = 0; i < shape.Names.Length; i++) {
            var value = shape.Kinds[i] == 0 ? ReadPrimitive(shape.Extras[i]) : ReadValue(depth);
            if (value is Reference reference) {
                _pending.Add(new Pending(result, shape.Names[i], 0, reference.Id));
                value = null;
            }
            result.Members[shape.Names[i]] = value;
        }
        return result;
    }

    private object?[] ReadElements(int id, int count, byte kind, byte extra, int depth) {
        if (count > MaxElements) {
            throw new TiaFormatException("A serialized array is too long.");
        }
        var result = new object?[count];
        _objects[id] = result;
        for (var i = 0; i < count;) {
            if (kind == 0) {
                result[i++] = ReadPrimitive(extra);
                continue;
            }
            var value = ReadValue(depth);
            if (value is NullRun run) {
                i += run.Count;
                continue;
            }
            if (value is Reference reference) {
                _pending.Add(new Pending(result, null, i, reference.Id));
                value = null;
            }
            result[i++] = value;
        }
        return result;
    }

    /// <summary>A member's or element's record; a library record in between describes nothing of the value.</summary>
    private object? ReadValue(int depth) {
        while (true) {
            var value = ReadRecord(ReadByte(), depth + 1);
            if (value is not Skip) {
                return value;
            }
        }
    }

    /// <summary>Reads what follows a member's type: a primitive type, a class name, or a class name and a library.</summary>
    private byte ReadTypeInfo(byte kind) {
        switch (kind) {
            case 0 or 7:
                return ReadByte();
            case 3:
                ReadString();
                return 0;
            case 4:
                ReadString();
                _at += 4;
                return 0;
            default:
                return 0;
        }
    }

    private object? ReadPrimitive(byte type) {
        var span = _data.AsSpan(_at);
        switch (type) {
            case 1: _at += 1; return span[0] != 0;
            case 2: _at += 1; return span[0];
            case 3: {
                var length = span[0] < 0x80 ? 1 : span[0] < 0xE0 ? 2 : span[0] < 0xF0 ? 3 : 4;
                _at += length;
                return Encoding.UTF8.GetString(span[..length]);
            }
            case 5 or 18: return ReadString();
            case 6: _at += 8; return BinaryPrimitives.ReadDoubleLittleEndian(span);
            case 7: _at += 2; return BinaryPrimitives.ReadInt16LittleEndian(span);
            case 8: _at += 4; return BinaryPrimitives.ReadInt32LittleEndian(span);
            case 9: _at += 8; return BinaryPrimitives.ReadInt64LittleEndian(span);
            case 10: _at += 1; return (sbyte)span[0];
            case 11: _at += 4; return BinaryPrimitives.ReadSingleLittleEndian(span);
            case 12: _at += 8; return TimeSpan.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(span));
            case 13: _at += 8; return BinaryPrimitives.ReadInt64LittleEndian(span) & 0x3FFFFFFFFFFFFFFF;
            case 14: _at += 2; return BinaryPrimitives.ReadUInt16LittleEndian(span);
            case 15: _at += 4; return BinaryPrimitives.ReadUInt32LittleEndian(span);
            case 16: _at += 8; return BinaryPrimitives.ReadUInt64LittleEndian(span);
            case 17: return null;
            default: throw new TiaFormatException($"Serialized primitive type {type} is not known.");
        }
    }

    private byte ReadByte() {
        if (_at >= _data.Length) {
            throw new TiaFormatException("A serialized value ends early.");
        }
        return _data[_at++];
    }

    private int ReadInt32() {
        if (_at + 4 > _data.Length) {
            throw new TiaFormatException("A serialized value ends early.");
        }
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_at));
        _at += 4;
        return value;
    }

    private int ReadCount() {
        var count = ReadInt32();
        return count is < 0 or > MaxElements ? throw new TiaFormatException("A serialized count is out of range.") : count;
    }

    /// <summary>A length-prefixed UTF-8 string: the length in 7 bit groups, low first.</summary>
    private string ReadString() {
        var length = 0;
        for (var shift = 0; ; shift += 7) {
            var b = ReadByte();
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) {
                break;
            }
            if (shift > 28) {
                throw new TiaFormatException("A serialized string length is malformed.");
            }
        }
        if (length < 0 || _at + length > _data.Length) {
            throw new TiaFormatException("A serialized string ends early.");
        }
        var text = Encoding.UTF8.GetString(_data, _at, length);
        _at += length;
        return text;
    }

    private sealed record ClassShape(string Name, string[] Names, byte[] Kinds, byte[] Extras);

    private sealed record Reference(int Id);

    private sealed record NullRun(int Count);

    private sealed class Skip {
        public static readonly Skip Instance = new();
    }

    private readonly record struct Pending(object Owner, string? Member, int Index, int Id);
}
