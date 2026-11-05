using System.Buffers;

namespace TiaFormat.Storage;

/// <summary>Bytes in an array from the shared pool. Dispose exactly once; do not copy after disposing.</summary>
public struct PooledBytes : IDisposable {
    private byte[]? _array;

    internal PooledBytes(byte[] array, int length) {
        _array = array;
        Length = length;
    }

    public int Length { get; }
    public readonly ReadOnlySpan<byte> Span => _array is null ? default : _array.AsSpan(0, Length);
    public readonly bool IsEmpty => Length == 0;

    public void Dispose() {
        var array = _array;
        _array = null;
        if (array is not null) {
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
