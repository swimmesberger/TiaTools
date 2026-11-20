using System.Globalization;
using System.Text;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>
/// Prints what is stored for one object: attributes, dictionary expando values (marked <c>~</c>) and relations. For
/// finding where a fact lives. Encrypted values stay closed; values named like a password, key, hash or certificate,
/// and user management and certificate objects as a whole, are not shown.
/// </summary>
public static class TiaInspector {
    private const int ShownCharacters = 4000;
    private const string Withheld = "[not shown]";

    public static void Write(TiaDatabase database, in TiaFrame frame, TextWriter output, bool full = false) {
        var type = database.Schema.FindObjectType(frame.TypeId);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"=== {type?.FullName ?? "?"} id={frame.ObjectId} states=0x{(int)frame.States:X}"));
        if (type is null) {
            return;
        }
        if (TiaSecrets.IsSecretType(type.FullName)) {
            output.WriteLine("  [user management or certificates: not shown]");
            return;
        }
        foreach (var layout in type.Interfaces) {
            foreach (var slot in layout.Slots) {
                var value = TiaSecrets.IsSecretName(slot.Name) ? Withheld : Read(database, frame, layout.Set.FullName, slot, full);
                if (!string.IsNullOrEmpty(value) && value is not "False" and not "0") {
                    output.WriteLine($"  {ShortName(layout.Set.FullName)}.{slot.Name} = {value}");
                }
            }
        }
        foreach (var key in database.ReadExpandoKeys(frame)) {
            var value = TiaSecrets.IsSecretName(key.Name) ? Withheld : ReadExpando(database, frame, key, full);
            if (!string.IsNullOrEmpty(value)) {
                output.WriteLine($"  ~{key.Name} = {value}");
            }
        }
        foreach (var relation in database.EnumerateRelations(frame)) {
            if (relation.Target.ObjectId == 0) {
                continue;
            }
            var target = database.Schema.FindObjectType(relation.Target.TypeId)?.FullName;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  -> {database.Schema.FindRelationName(relation.RelationId) ?? "?"} : {ShortName(target ?? "?")} {relation.Target.ObjectId}"));
        }
    }

    private static string? Read(TiaDatabase database, in TiaFrame frame, string set, TiaAttributeSlot slot, bool full) {
        var handle = database.Schema.Attribute(set, slot.Name);
        try {
            switch (slot.Type.Kind) {
                case TiaValueKind.Boolean:
                    return database.ReadBoolean(frame, handle).ToString();
                case TiaValueKind.UInt8 or TiaValueKind.Int16 or TiaValueKind.Int32 or TiaValueKind.Int64:
                    return database.ReadInteger(frame, handle)?.ToString(CultureInfo.InvariantCulture);
                case TiaValueKind.String or TiaValueKind.Enumeration:
                    return database.ReadString(frame, handle);
                case TiaValueKind.Blob or TiaValueKind.Xml:
                    using (var blob = database.ReadBlob(frame, handle)) {
                        return Show(blob.Span, full);
                    }
                default:
                    return full ? $"[{slot.Type.Kind}, not decoded]" : null;
            }
        } catch (Exception e) when (e is TiaFormatException or InvalidDataException or ArgumentOutOfRangeException) {
            return "[unreadable]";
        }
    }

    private static string? ReadExpando(TiaDatabase database, in TiaFrame frame, TiaExpandoKey key, bool full) {
        try {
            switch (key.ValueTypeId) {
                case 0x8000000A:
                    return database.ReadExpandoDateTime(frame, key.Name)?.ToString("u", CultureInfo.InvariantCulture);
                case 0x8000000B:
                    return database.ReadExpandoString(frame, key.Name);
                case 0x8000000D or 0x8000000E:
                    using (var blob = database.ReadExpandoBlob(frame, key.Name)) {
                        return Show(blob.Span, full);
                    }
                default:
                    if (database.ReadExpandoInteger(frame, key.Name) is { } number) {
                        return number.ToString(CultureInfo.InvariantCulture);
                    }
                    return full ? string.Create(CultureInfo.InvariantCulture, $"[type 0x{key.ValueTypeId:X}, not decoded]") : null;
            }
        } catch (Exception e) when (e is TiaFormatException or InvalidDataException or ArgumentOutOfRangeException) {
            return "[unreadable]";
        }
    }

    private static string Show(ReadOnlySpan<byte> bytes, bool full) {
        if (bytes.IndexOf("<EncryptedData"u8) >= 0) {
            return "[encrypted: closed]";
        }
        var text = Encoding.UTF8.GetString(full ? bytes : bytes[..Math.Min(bytes.Length, ShownCharacters)]);
        var printable = text.All(c => c is '\n' or '\r' or '\t' || !char.IsControl(c));
        return printable ? text : string.Create(CultureInfo.InvariantCulture, $"[{bytes.Length} bytes]");
    }

    private static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
}
