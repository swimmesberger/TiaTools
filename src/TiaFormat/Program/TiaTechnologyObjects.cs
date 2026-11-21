using System.Globalization;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>
/// A technology object of a PLC, such as an axis or a PID controller (spec 0013): a data block of a technology type,
/// listed apart from the blocks. <see cref="Kind"/> is the type as TIA names it, <c>TO_PositioningAxis</c> or
/// <c>PID_Compact</c>; <see cref="Version"/> its version, <c>8.0</c>.
/// </summary>
public sealed record TiaTechnologyObject(TiaFrame Frame, string Name, string Kind, string? Version, long? Number, bool IsKnowHowProtected);

/// <summary>
/// One parameter of a technology object, named as TIA's parameter view names it (<c>DynamicLimits.MaxVelocity</c>), with
/// its data type and its value: the one set on the object, else its default. Value is null where the reader finds none.
/// </summary>
public readonly record struct TiaTechnologyParameter(string Name, string Type, string? Value);

public sealed partial class TiaProgram {
    private const string TechnologicalDataBlock = "Siemens.Simatic.Lang.Model.Blocks.TechnologicalDataBlockData";

    private IReadOnlyList<TiaTechnologyObject>? _technologyObjects;

    /// <summary>Every technology object, of every PLC and master copy. They are not in <see cref="Blocks"/>.</summary>
    public IReadOnlyList<TiaTechnologyObject> TechnologyObjects => LazyInitializer.EnsureInitialized(ref _technologyObjects, ReadTechnologyObjects);

    private List<TiaTechnologyObject> ReadTechnologyObjects() {
        var type = _db.Schema.FindObjectType(TechnologicalDataBlock)?.Id;
        var result = new List<TiaTechnologyObject>();
        if (type is null) {
            return result;
        }
        foreach (ref readonly var frame in _db.Frames) {
            if (frame.TypeId != type || (frame.States & TiaFrameStates.Reference) != 0) {
                continue;
            }
            // The type it is an instance of names the kind as TIA does: TO_SpeedAxis, or PID_Compact where the data block's own OfName says PID_Compact_1500.
            var kind = _db.TryGetTarget(frame, _generatedFrom, out var type2) || _db.TryGetTarget(frame, _isGeneratedFrom, out type2)
                ? _db.ReadString(type2, _name)
                : null;
            result.Add(new TiaTechnologyObject(
                frame,
                _db.ReadString(frame, _name) ?? "",
                kind ?? _db.ReadString(frame, _subtype) ?? "",
                _db.ReadString(frame, _version),
                _db.ReadInteger(frame, _number),
                _db.ReadBoolean(frame, _protected)));
        }
        return result;
    }

    /// <summary>
    /// A technology object's parameters, in the order of its type's members, named as TIA names them: a member of its
    /// base type (<c>Base</c>) without that prefix, an array element with its index, <c>Sensor[1].Type</c>. A value is the
    /// one stored on the object, else the one in its compiled image, at the sum of the optimized offsets along its path.
    /// </summary>
    public IReadOnlyList<TiaTechnologyParameter> ReadParameters(TiaTechnologyObject technologyObject) {
        if (!_db.TryGetTarget(technologyObject.Frame, _currentInterface, out var root)) {
            return [];
        }
        var context = new InterfaceContext { Parts = PositionalParts(root), IsDataBlock = true };
        var members = ParseRoot(root, context);
        foreach (var key in PositionalParts(root)) {
            if (_db.TryGetFrame(key, out var part) && _db.ReadString(part, _partKind) == "Values") {
                using var payload = _db.ReadBlob(part, _partPayload);
                CollectValues(payload.Span, context.Values);
            }
        }
        // The compiled image has two areas, each with the offsets of its own members: what is retained, and the rest. Each
        // buffer may start with a head, whatever it holds beyond its area's size.
        var hasImage = _db.TryGetTarget(technologyObject.Frame, _loadables, out var load);
        using var volatileImage = hasImage ? _db.ReadBlob(load, _image) : default;
        using var retainImage = hasImage ? _db.ReadBlob(load, _retainImage) : default;
        var volatileHead = Head(volatileImage.Length, context.RootVolatileBits);
        var retainHead = Head(retainImage.Length, context.RootRetainBits);
        var result = new List<TiaTechnologyParameter>();
        Walk(members, "", "", 0, null);
        return result;

        static int? Head(int length, int? areaBits) => areaBits is { } bits && length >= bits / 8 ? length - (bits / 8) : null;

        void Walk(List<TiaMemberNode> nodes, string name, string path, int? bits, bool? retained) {
            foreach (var node in nodes) {
                // A member of the instance's own sections decides its area; what is inside it is in the same one.
                var inRetain = retained ?? node.IsRetain;
                var own = path.Length == 0 ? node.Id : path + ":" + node.Id;
                var at = bits is { } start && node.OptimizedBitOffset is { } offset ? start + offset : (int?)null;
                // TIA's names leave out the base type a positioning axis is built on.
                var named = node.Name == "Base" && node.Inner.Count != 0 ? name : name.Length == 0 ? node.Name : name + "." + node.Name;
                if (TiaDeclaredValues.IsArray(node)) {
                    var bounds = TiaDeclaredValues.Bounds(node);
                    if (bounds.Count != 1) {
                        continue;
                    }
                    for (var i = bounds[0].Low; i <= bounds[0].High; i++) {
                        var index = i.ToString(CultureInfo.InvariantCulture);
                        // Elements of a UDT follow one another, each the size of its type.
                        var element = at is { } first && node.TypeOptimizedBits is { } stride ? first + ((i - bounds[0].Low) * stride) : (int?)null;
                        Leaf(node, named + "[" + index + "]", own + "[" + index + "]", ElementType(node.Type), element, inRetain);
                    }
                    continue;
                }
                Leaf(node, named, own, node.Type, at, inRetain);
            }
        }

        void Leaf(TiaMemberNode node, string name, string path, string type, int? bits, bool retained) {
            if (node.Inner.Count != 0) {
                Walk(node.Inner, name, path, bits, retained);
                return;
            }
            var value = context.Values.GetValueOrDefault(path);
            var head = retained ? retainHead : volatileHead;
            if (value is null && bits is { } at && head is { } start) {
                value = FormatScalar(node.Rid, retained ? retainImage.Span : volatileImage.Span, (at / 8) + start, at % 8, reals: true);
            }
            result.Add(new TiaTechnologyParameter(name, type, value));
        }
    }

    private static string ElementType(string arrayType) {
        var of = arrayType.LastIndexOf(" of ", StringComparison.Ordinal);
        return of < 0 ? arrayType : arrayType[(of + 4)..];
    }
}
