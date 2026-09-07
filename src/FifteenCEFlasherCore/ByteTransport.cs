namespace FifteenCEFlasherCore;

public interface IByteTransport : IDisposable
{
    void Write(ReadOnlySpan<byte> data);
    byte[] Read(int maxCount, TimeSpan timeout);
    void Close();
}

public static class ByteTransportExtensions
{
    public static byte[] ReadExactly(this IByteTransport transport, int count, TimeSpan timeout)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var result = new byte[count];
        var offset = 0;
        var deadline = DateTime.UtcNow + timeout;
        while (offset < count)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw FlasherError.SambaTimeout();

            var chunk = transport.Read(count - offset, remaining);
            if (chunk.Length == 0)
                throw FlasherError.SambaTimeout();

            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    public static void DiscardAvailable(this IByteTransport transport, TimeSpan timeout)
    {
        _ = transport.Read(4096, timeout);
    }
}
