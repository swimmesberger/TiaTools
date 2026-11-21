using System.Buffers.Binary;
using System.Text;

namespace TiaFormat.Storage;

/// <summary>One table of a SQLite file: its name, root page, and column names in order.</summary>
public sealed record SqliteTable(string Name, int RootPage, IReadOnlyList<string> Columns, int RowIdColumn) {
    /// <summary>The position of a column by name, ignoring case; -1 if the table has none.</summary>
    public int Column(string name) {
        for (var i = 0; i < Columns.Count; i++) {
            if (string.Equals(Columns[i], name, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>One row: its rowid and a value per column, each null, a long, a double, a string or a byte array.</summary>
public readonly record struct SqliteRow(long RowId, object?[] Values) {
    public long Integer(int column) => Values[column] is long value ? value : 0;

    public byte[] Blob(int column) => Values[column] switch {
        byte[] bytes => bytes,
        string text => Encoding.UTF8.GetBytes(text),
        _ => [],
    };
}

/// <summary>
/// A read-only reader of the SQLite file format, enough for TIA's cross-reference list (ADR 0014): the header, the schema
/// table, table b-tree pages, overflow pages and records. No indexes, no write-ahead log, nothing written. A file it does
/// not understand throws <see cref="TiaFormatException"/>. The layout: https://www.sqlite.org/fileformat2.html
/// </summary>
public sealed class SqliteFile {
    private static readonly byte[] Magic = "SQLite format 3\0"u8.ToArray();
    private readonly byte[] _data;
    private readonly int _pageSize;
    private readonly int _usable;
    private readonly int _pageCount;
    private readonly Dictionary<string, SqliteTable> _tables = new(StringComparer.OrdinalIgnoreCase);

    private SqliteFile(byte[] data) {
        _data = data;
        if (data.Length < 100 || !data.AsSpan(0, 16).SequenceEqual(Magic)) {
            throw new TiaFormatException("Not a SQLite file.");
        }
        var size = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(16));
        _pageSize = size == 1 ? 65536 : size;
        if (_pageSize < 512 || (_pageSize & (_pageSize - 1)) != 0 || data.Length % _pageSize != 0) {
            throw new TiaFormatException($"SQLite page size {_pageSize} does not fit a file of {data.Length} bytes.");
        }
        _usable = _pageSize - data[20];
        _pageCount = data.Length / _pageSize;
        var encoding = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(56));
        if (encoding is not (0 or 1)) {
            throw new TiaFormatException($"SQLite text encoding {encoding} is not UTF-8.");
        }
        foreach (var row in ReadTree(1)) {
            if (row.Values.Length >= 5 && row.Values[0] is "table" && row.Values[1] is string name && row.Values[3] is long root && row.Values[4] is string sql) {
                var (columns, rowId) = ParseColumns(sql);
                _tables[name] = new SqliteTable(name, (int)root, columns, rowId);
            }
        }
    }

    public static SqliteFile Open(byte[] data) => new(data);

    public IReadOnlyCollection<SqliteTable> Tables => _tables.Values;

    public SqliteTable? FindTable(string name) => _tables.GetValueOrDefault(name);

    /// <summary>Every row of a table in rowid order; the INTEGER PRIMARY KEY column, stored as null, gets the rowid.</summary>
    public IEnumerable<SqliteRow> Rows(SqliteTable table) {
        foreach (var row in ReadTree(table.RootPage)) {
            if (table.RowIdColumn >= 0 && table.RowIdColumn < row.Values.Length) {
                row.Values[table.RowIdColumn] = row.RowId;
            }
            if (row.Values.Length < table.Columns.Count) {
                // Columns added later are missing from older records and hold null.
                var values = new object?[table.Columns.Count];
                row.Values.CopyTo(values, 0);
                yield return row with { Values = values };
            } else {
                yield return row;
            }
        }
    }

    private IEnumerable<SqliteRow> ReadTree(int root) {
        var pending = new Stack<int>();
        pending.Push(root);
        var visited = new HashSet<int>();
        // Interior pages hold their children in key order, the right-most last; a stack visits them in that order if
        // pushed in reverse.
        while (pending.Count != 0) {
            var page = pending.Pop();
            if (page < 1 || page > _pageCount || !visited.Add(page)) {
                throw new TiaFormatException($"SQLite page {page} is out of the file or visited twice.");
            }
            var start = (page - 1) * _pageSize;
            var header = page == 1 ? 100 : 0;
            var kind = _data[start + header];
            var cells = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(start + header + 3));
            switch (kind) {
                case 0x0D:
                    for (var i = 0; i < cells; i++) {
                        var cell = start + BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(start + header + 8 + i * 2));
                        yield return ReadLeafCell(cell);
                    }
                    break;
                case 0x05:
                    pending.Push((int)BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(start + header + 8)));
                    for (var i = cells - 1; i >= 0; i--) {
                        var cell = start + BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(start + header + 12 + i * 2));
                        pending.Push((int)BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(cell)));
                    }
                    break;
                default:
                    throw new TiaFormatException($"SQLite page {page} is of kind {kind:X2}, not a table b-tree page.");
            }
        }
    }

    private SqliteRow ReadLeafCell(int at) {
        var size = ReadVarInt(ref at);
        var rowId = ReadVarInt(ref at);
        var payload = Payload(at, size);
        return new SqliteRow(rowId, ReadRecord(payload));
    }

    /// <summary>A cell's payload, its part on the page followed by its overflow pages.</summary>
    private byte[] Payload(int at, long size) {
        if (size < 0 || size > _data.Length * 2L) {
            throw new TiaFormatException($"SQLite payload of {size} bytes.");
        }
        var max = _usable - 35;
        if (size <= max) {
            return _data.AsSpan(at, (int)size).ToArray();
        }
        var min = (_usable - 12) * 32 / 255 - 23;
        var local = min + (int)((size - min) % (_usable - 4));
        if (local > max) {
            local = min;
        }
        var payload = new byte[size];
        _data.AsSpan(at, local).CopyTo(payload);
        var written = local;
        var next = (int)BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(at + local));
        var pages = 0;
        while (written < size) {
            if (next < 1 || next > _pageCount || ++pages > _pageCount) {
                throw new TiaFormatException($"SQLite overflow page {next} is out of the file.");
            }
            var start = (next - 1) * _pageSize;
            var count = (int)Math.Min(size - written, _usable - 4);
            _data.AsSpan(start + 4, count).CopyTo(payload.AsSpan(written));
            written += count;
            next = (int)BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(start));
        }
        return payload;
    }

    private static object?[] ReadRecord(byte[] record) {
        var at = 0;
        var headerSize = (int)ReadVarInt(record, ref at);
        var types = new List<long>();
        while (at < headerSize) {
            types.Add(ReadVarInt(record, ref at));
        }
        var values = new object?[types.Count];
        var body = headerSize;
        for (var i = 0; i < types.Count; i++) {
            var type = types[i];
            switch (type) {
                case 0:
                    values[i] = null;
                    break;
                case >= 1 and <= 6:
                    var width = type switch { 5 => 6, 6 => 8, _ => (int)type };
                    long value = (sbyte)record[body];
                    for (var b = 1; b < width; b++) {
                        value = (value << 8) | record[body + b];
                    }
                    values[i] = value;
                    body += width;
                    break;
                case 7:
                    values[i] = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(record.AsSpan(body)));
                    body += 8;
                    break;
                case 8:
                    values[i] = 0L;
                    break;
                case 9:
                    values[i] = 1L;
                    break;
                case >= 12 when type % 2 == 0:
                    var blob = (int)((type - 12) / 2);
                    values[i] = record.AsSpan(body, blob).ToArray();
                    body += blob;
                    break;
                case >= 13:
                    var text = (int)((type - 13) / 2);
                    values[i] = Encoding.UTF8.GetString(record, body, text);
                    body += text;
                    break;
                default:
                    throw new TiaFormatException($"SQLite serial type {type} is reserved.");
            }
        }
        return values;
    }

    private long ReadVarInt(ref int at) => ReadVarInt(_data, ref at);

    /// <summary>SQLite's varint: big-endian groups of 7 bits, the top bit set on all but the last; a ninth byte gives 8 bits.</summary>
    private static long ReadVarInt(byte[] data, ref int at) {
        long value = 0;
        for (var i = 0; i < 8; i++) {
            var b = data[at++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) {
                return value;
            }
        }
        return (value << 8) | data[at++];
    }

    /// <summary>The column names of a CREATE TABLE statement, and which one is the INTEGER PRIMARY KEY that is the rowid.</summary>
    internal static (List<string> Columns, int RowId) ParseColumns(string sql) {
        var open = sql.IndexOf('(', StringComparison.Ordinal);
        var close = sql.LastIndexOf(')');
        var columns = new List<string>();
        var rowId = -1;
        if (open < 0 || close < open) {
            return (columns, rowId);
        }
        var depth = 0;
        var from = open + 1;
        for (var i = open + 1; i <= close; i++) {
            var c = sql[i];
            if (c == '(') {
                depth++;
            } else if (c == ')' && i < close) {
                depth--;
            } else if ((c == ',' && depth == 0) || i == close) {
                var definition = sql[from..i].Trim();
                from = i + 1;
                var words = definition.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0 || words[0].ToUpperInvariant() is "CONSTRAINT" or "PRIMARY" or "UNIQUE" or "CHECK" or "FOREIGN") {
                    continue;
                }
                if (words.Length >= 4 && words[1].Equals("integer", StringComparison.OrdinalIgnoreCase)
                    && words[2].Equals("primary", StringComparison.OrdinalIgnoreCase) && words[3].StartsWith("key", StringComparison.OrdinalIgnoreCase)) {
                    rowId = columns.Count;
                }
                columns.Add(words[0].Trim('"', '`', '[', ']'));
            }
        }
        return (columns, rowId);
    }
}
