using System.IO.Ports;

namespace FifteenCEFlasherCore;

/// <summary>Win32 serial link — 115200 8N1, no DTR/RTS toggling.</summary>
public sealed class WindowsSerialLink : IByteTransport
{
    private readonly SerialPort _port;
    private bool _closed;

    public WindowsSerialLink(string portName)
    {
        _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 500,
            WriteTimeout = 5000,
            NewLine = "\n",
        };

        try
        {
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw FlasherError.SerialOpenFailed(portName);
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_closed)
            throw FlasherError.NotConnected();
        _port.Write(data.ToArray(), 0, data.Length);
    }

    public byte[] Read(int maxCount, TimeSpan timeout)
    {
        if (_closed)
            throw FlasherError.NotConnected();

        _port.ReadTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);
        try
        {
            var buffer = new byte[maxCount];
            var read = _port.Read(buffer, 0, maxCount);
            if (read <= 0)
                return Array.Empty<byte>();
            return buffer.AsSpan(0, read).ToArray();
        }
        catch (TimeoutException)
        {
            return Array.Empty<byte>();
        }
    }

    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        try
        {
            if (_port.IsOpen)
                _port.Close();
        }
        catch
        {
            // Best effort.
        }
        _port.Dispose();
    }

    public void Dispose() => Close();
}
