using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TiaFormat.Tests;

/// <summary>Writes small PEData.plf files by the format page, so tests need no real archive.</summary>
internal sealed class TiaFileBuilder {
    public const string SchemaHead = """
        <MetaInfo xmlns="http://www.siemens.com/Automation/2004/04/ObjectFrame/Meta"><Package name="P"><Namespace name="N">
        """;

    public const string SchemaTail = "</Namespace></Package></MetaInfo>";

    private readonly ArrayBufferWriter<byte> _file = new();

    public TiaFileBuilder(string schemaBody) {
        var header = new byte[98];
        BinaryPrimitives.WriteInt16LittleEndian(header, 64);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), 2000);
        _file.Write(header);
        WriteSystem(0x70000, 1, Compress(SchemaHead + schemaBody + SchemaTail));
    }

    public void WriteSystem(uint type, ulong id, ReadOnlySpan<byte> payload) {
        var body = new byte[4 + payload.Length + 1];
        BinaryPrimitives.WriteInt32LittleEndian(body, payload.Length);
        payload.CopyTo(body.AsSpan(4));
        body[^1] = 0xFF;
        WriteFrame(type, id, 0, 0, 0, body);
    }

    public void Commit(ulong id = 900) => WriteSystem(0x70014, id, new byte[40]);

    /// <summary>A frame of an object type: offsets from the frame start, one per entry, then the rest of the body.</summary>
    public void WriteObject(uint type, ulong id, IReadOnlyList<byte[]?> entries, ushort states = 0, uint info = 0) {
        var tableEnd = 44 + (entries.Count * 4);
        var body = new MemoryStream();
        var table = new byte[entries.Count * 4];
        var at = tableEnd;
        for (var i = 0; i < entries.Count; i++) {
            if (entries[i] is { } entry) {
                BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(i * 4), at);
                at += entry.Length;
            }
        }
        body.Write(table);
        foreach (var entry in entries) {
            if (entry is not null) {
                body.Write(entry);
            }
        }
        body.WriteByte(0xFF);
        WriteFrame(type, id, info, states, (byte)entries.Count, body.ToArray());
    }

    public byte[] ToArray() => _file.WrittenSpan.ToArray();

    private void WriteFrame(uint type, ulong id, uint info, ushort states, byte offsetCount, byte[] body) {
        var frame = new byte[44 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), type);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(8), id);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(20), info);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24), states);
        frame[26] = offsetCount;
        body.CopyTo(frame.AsSpan(44));
        _file.Write(frame);
        _file.Write(SHA256.HashData(frame));
    }

    public static byte[] Compress(string text) => Compress(Encoding.UTF8.GetBytes(text));

    public static byte[] Compress(byte[] raw) {
        var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true)) {
            z.Write(raw);
        }
        return output.ToArray();
    }

    /// <summary>An interface segment: u32 size, then slots; each value after them is reached by a relative offset.</summary>
    public static byte[] Segment(int slotBytes, params (int Slot, byte[] Value)[] relativeValues) {
        var size = 4 + slotBytes;
        foreach (var (_, value) in relativeValues) {
            size += value.Length;
        }
        var segment = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(segment, size);
        var at = 4 + slotBytes;
        foreach (var (slot, value) in relativeValues) {
            BinaryPrimitives.WriteInt32LittleEndian(segment.AsSpan(slot), at);
            value.CopyTo(segment.AsSpan(at));
            at += value.Length;
        }
        return segment;
    }

    public static byte[] VarInt(int value) {
        var bytes = new List<byte>();
        do {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        } while (value != 0);
        return [.. bytes];
    }

    /// <summary>A string value: the varint counts itself.</summary>
    public static byte[] String(string text) {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var total = utf8.Length + 1;
        var prefix = VarInt(total);
        if (prefix.Length != 1) {
            prefix = VarInt(utf8.Length + 2);
        }
        return [.. prefix, .. utf8];
    }

    /// <summary>A blob value of kind 0 or 1 in pages, with the varint total in front.</summary>
    public static byte[] Blob(byte[] raw, bool compress, int pageSize = 4096) => WithTotal(Head(raw, compress, pageSize));

    /// <summary>A kind 0 or 1 head and its pages without the total: also the payload of a segment frame.</summary>
    public static byte[] Head(byte[] raw, bool compress, int pageSize = 4096) {
        var pages = (raw.Length + pageSize - 1) / pageSize;
        var output = new MemoryStream();
        output.WriteByte(compress ? (byte)1 : (byte)0);
        var numbers = new byte[18];
        BinaryPrimitives.WriteInt64LittleEndian(numbers, raw.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(numbers.AsSpan(12), (ushort)pageSize);
        BinaryPrimitives.WriteInt32LittleEndian(numbers.AsSpan(14), pages);
        output.Write(numbers);
        var words = (pages + 31) / 32;
        output.Write(VarInt(words));
        var bitmap = new byte[words * 4];
        for (var i = 0; i < pages; i++) {
            bitmap[i / 8] |= (byte)(1 << (i % 8));
        }
        output.Write(bitmap);
        for (var i = 0; i < pages; i++) {
            var page = raw.AsSpan(i * pageSize, Math.Min(pageSize, raw.Length - (i * pageSize)));
            if (compress) {
                var packed = Compress(page.ToArray());
                output.Write(VarInt(packed.Length));
                output.Write(packed);
            } else {
                output.Write(page);
            }
        }
        return output.ToArray();
    }

    /// <summary>A kind 4 value: the head with no pages, and references to segment frames.</summary>
    public static byte[] LargeBlob(int length, int pages, params ulong[] segmentIds) {
        var output = new MemoryStream();
        output.WriteByte(4);
        var numbers = new byte[18];
        BinaryPrimitives.WriteInt64LittleEndian(numbers, length);
        BinaryPrimitives.WriteUInt16LittleEndian(numbers.AsSpan(12), 4096);
        BinaryPrimitives.WriteInt32LittleEndian(numbers.AsSpan(14), pages);
        output.Write(numbers);
        var words = (pages + 31) / 32;
        output.Write(VarInt(words));
        output.Write(new byte[words * 4]);
        foreach (var id in segmentIds) {
            var reference = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(reference, 0x70013);
            BinaryPrimitives.WriteUInt64LittleEndian(reference.AsSpan(4), id);
            output.Write(reference);
        }
        return WithTotal(output.ToArray());
    }

    /// <summary>A one-to-one or low-filled relation list.</summary>
    public static byte[] Slots(params (uint Relation, uint Type, ulong Id)[] slots) {
        var list = new byte[4 + (slots.Length * 16)];
        BinaryPrimitives.WriteUInt16LittleEndian(list, (ushort)list.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(2), (ushort)slots.Length);
        for (var i = 0; i < slots.Length; i++) {
            var slot = list.AsSpan(4 + (i * 16));
            BinaryPrimitives.WriteUInt32LittleEndian(slot, slots[i].Relation);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[4..], slots[i].Type);
            BinaryPrimitives.WriteUInt64LittleEndian(slot[8..], slots[i].Id);
        }
        return list;
    }

    /// <summary>A per-relation list.</summary>
    public static byte[] Targets(params (uint Type, ulong Id)[] targets) {
        var list = new byte[12 + (targets.Length * 12)];
        BinaryPrimitives.WriteInt32LittleEndian(list, list.Length);
        BinaryPrimitives.WriteInt32LittleEndian(list.AsSpan(8), targets.Length);
        for (var i = 0; i < targets.Length; i++) {
            BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(12 + (i * 12)), targets[i].Type);
            BinaryPrimitives.WriteUInt64LittleEndian(list.AsSpan(16 + (i * 12)), targets[i].Id);
        }
        return list;
    }

    private static byte[] WithTotal(byte[] content) {
        var prefix = VarInt(content.Length + 1);
        if (prefix.Length != 1) {
            prefix = VarInt(content.Length + prefix.Length);
        }
        return [.. prefix, .. content];
    }
}
