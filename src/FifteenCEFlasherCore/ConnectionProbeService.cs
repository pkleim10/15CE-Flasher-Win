namespace FifteenCEFlasherCore;

public enum ConnectionProbeState
{
    Waiting,
    Connected,
    Error,
}

public sealed class ConnectionProbeResult
{
    public ConnectionProbeState State { get; init; }
    public string StatusText { get; init; } = "";
    public string? DetailText { get; init; }
    public SerialPortInfo? Port { get; init; }
    public DeviceIdentity? Identity { get; init; }
    public string? SambaVersion { get; init; }
    public IReadOnlyList<SerialPortInfo> AllPorts { get; init; } = [];
}

/// <summary>Polls COM ports ~1 Hz and attempts SAM-BA connect on Atmel CDC only.</summary>
public sealed class ConnectionProbeService
{
    public ISerialPortListing PortListing { get; init; } = new WindowsComPortListing();
    public Func<SerialPortInfo, IByteTransport> OpenTransport { get; init; } =
        port => new WindowsSerialLink(port.PortName);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public ConnectionProbeResult Poll()
    {
        var allPorts = PortListing.ListPorts();
        var atmelPorts = allPorts.AtmelSamBaPorts().ToList();

        if (allPorts.Count == 0)
        {
            return new ConnectionProbeResult
            {
                State = ConnectionProbeState.Error,
                StatusText = "No programming cable detected",
                DetailText = "Plug in the pogo cable USB adapter.",
                AllPorts = allPorts,
            };
        }

        if (atmelPorts.Count == 0)
        {
            var ftdi = allPorts.Where(p => p.Kind == UsbPortKind.Ftdi).ToList();
            return new ConnectionProbeResult
            {
                State = ConnectionProbeState.Waiting,
                StatusText = "Waiting…",
                DetailText = "Hold ERASE, press RESET, then release ERASE.",
                AllPorts = allPorts,
            };
        }

        Exception? lastError = null;
        foreach (var port in atmelPorts)
        {
            try
            {
                var transport = OpenTransport(port);
                var client = new SambaClient(transport);
                try
                {
                    client.Connect(ConnectTimeout);
                    var identity = new FlashCalw(client).Identify();
                    if (!identity.IsSam4L)
                    {
                        return new ConnectionProbeResult
                        {
                            State = ConnectionProbeState.Error,
                            StatusText = "Unsupported chip",
                            DetailText = $"{identity.Name} (CIDR=0x{identity.Cidr:X8})",
                            Port = port,
                            Identity = identity,
                            SambaVersion = client.Version,
                            AllPorts = allPorts,
                        };
                    }

                    return new ConnectionProbeResult
                    {
                        State = ConnectionProbeState.Connected,
                        StatusText = $"Connected: {identity.Name}",
                        DetailText = $"{port.PortName} · {client.Version.Trim()}",
                        Port = port,
                        Identity = identity,
                        SambaVersion = client.Version,
                        AllPorts = allPorts,
                    };
                }
                finally
                {
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        return new ConnectionProbeResult
        {
            State = ConnectionProbeState.Waiting,
            StatusText = "Waiting…",
            DetailText = lastError?.Message ?? "Hold ERASE, press RESET, then release ERASE.",
            AllPorts = allPorts,
        };
    }
}
