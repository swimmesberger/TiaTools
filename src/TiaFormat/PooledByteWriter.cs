using System.Buffers;
using System.Text;

namespace TiaFormat;

/// <summary>A growing UTF-8 output buffer over the shared array pool, reusable across blocks with <see cref="Clear"/>.</summary>
public sealed class PooledByteWriter : IBufferWriter<byte>, IDisposable {
    private byte[] _buffer;
    private int _written;

    public PooledByteWriter(int capacity = 64 * 1024) {
        _buffer = ArrayPool<byte>.Shared.Rent(capacity);
    }

    public int Length => _written;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Clear() => _written = 0;

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0) {
        Ensure(Math.Max(sizeHint, 1));
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0) {
        Ensure(Math.Max(sizeHint, 1));
        return _buffer.AsSpan(_written);
    }

    public void Write(ReadOnlySpan<byte> bytes) {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_written));
        _written += bytes.Length;
    }

    public void Write(byte value) {
        Ensure(1);
        _buffer[_written++] = value;
    }

    public void Write(string? text) {
        if (string.IsNullOrEmpty(text)) {
            return;
        }
        Ensure(Encoding.UTF8.GetMaxByteCount(text.Length));
        _written += Encoding.UTF8.GetBytes(text, _buffer.AsSpan(_written));
    }

    public void WriteSpaces(int count) {
        Ensure(count);
        _buffer.AsSpan(_written, count).Fill((byte)' ');
        _written += count;
    }

    public void Dispose() {
        var buffer = _buffer;
        _buffer = [];
        _written = 0;
        if (buffer.Length != 0) {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Ensure(int more) {
        if (_buffer.Length - _written >= more) {
            return;
        }
        var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _written + more));
        _buffer.AsSpan(0, _written).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }
}
