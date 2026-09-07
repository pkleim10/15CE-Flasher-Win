namespace FifteenCEFlasherCore;

public sealed class SambaClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    public const int BlockSize = 4096;

    private readonly IByteTransport _transport;
    public string Version { get; private set; } = "";
    public IList<string> CommandTrace { get; } = new List<string>();
    public TimeSpan AfterSendDelay { get; set; } = TimeSpan.Zero;

    public SambaClient(IByteTransport transport) => _transport = transport;

    public void Connect(TimeSpan? timeout = null)
    {
        var t = timeout ?? DefaultTimeout;
        EnterBinaryMode();
        try
        {
            ReadVersion(t);
        }
        catch (FlasherError ex) when (ex.Kind == FlasherErrorKind.SambaTimeout)
        {
            SendCommand("T#");
            _ = TryReadUntilPromptOrTimeout(TimeSpan.FromSeconds(Math.Min(1, t.TotalSeconds)));
            EnterBinaryMode();
            ReadVersion(t);
        }
    }

    public void EnterBinaryMode()
    {
        SendCommand("N#");
        _transport.DiscardAvailable(TimeSpan.FromMilliseconds(200));
    }

    public void Ping(TimeSpan? timeout = null) =>
        _ = ReadWord(FlashCalw.ChipIdCidr, timeout ?? DefaultTimeout);

    private void ReadVersion(TimeSpan timeout)
    {
        SendCommand("V#");
        Version = ReadAsciiLine(timeout);
        if (string.IsNullOrWhiteSpace(Version))
            throw FlasherError.SambaProtocol("empty version string");
    }

    public uint ReadWord(uint address, TimeSpan? timeout = null)
    {
        SendCommand($"w{address:X8},4#");
        var bytes = _transport.ReadExactly(4, timeout ?? DefaultTimeout);
        return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
    }

    public void WriteWord(uint address, uint value) =>
        SendCommand($"W{address:X8},{value:X8}#");

    public byte[] Read(uint address, int length, TimeSpan? timeout = null)
    {
        if (length <= 0)
            return Array.Empty<byte>();

        var t = timeout ?? TimeSpan.FromSeconds(30);
        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var chunk = Math.Min(BlockSize, length - offset);
            var addr = address + (uint)offset;
            SendCommand($"R{addr:X8},{chunk:X8}#");
            var part = _transport.ReadExactly(chunk, t);
            Buffer.BlockCopy(part, 0, result, offset, chunk);
            offset += chunk;
        }
        return result;
    }

    public void Go(uint address) => SendCommand($"G{address:X8}#");

    public void DiscardAvailable(TimeSpan? timeout = null) =>
        _transport.DiscardAvailable(timeout ?? TimeSpan.FromMilliseconds(50));

    public void Write(uint address, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var offset = 0;
        while (offset < data.Length)
        {
            var chunk = Math.Min(BlockSize, data.Length - offset);
            var addr = address + (uint)offset;
            SendCommand($"S{addr:X8},{chunk:X8}#");
            _transport.Write(data.Slice(offset, chunk));
            if (AfterSendDelay > TimeSpan.Zero)
                Thread.Sleep(AfterSendDelay);
            offset += chunk;
        }
    }

    public void Close() => _transport.Close();

    private void SendCommand(string command)
    {
        CommandTrace.Add(command);
        _transport.Write(System.Text.Encoding.ASCII.GetBytes(command));
    }

    private string ReadAsciiLine(TimeSpan timeout)
    {
        var collected = new List<byte>();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var chunk = _transport.Read(64, remaining);
            if (chunk.Length == 0)
                continue;

            collected.AddRange(chunk);
            var nl = collected.IndexOf(0x0A);
            if (nl >= 0)
                return System.Text.Encoding.UTF8.GetString(collected.Take(nl).ToArray()).Trim();
        }
        throw FlasherError.SambaTimeout();
    }

    private byte[] TryReadUntilPromptOrTimeout(TimeSpan timeout)
    {
        var collected = new List<byte>();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var chunk = _transport.Read(64, deadline - DateTime.UtcNow);
            if (chunk.Length == 0)
                break;
            collected.AddRange(chunk);
            if (collected.Contains((byte)'>'))
                break;
        }
        return collected.ToArray();
    }
}
