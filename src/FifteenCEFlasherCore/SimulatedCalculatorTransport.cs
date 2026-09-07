namespace FifteenCEFlasherCore;

/// <summary>In-memory ATSAM4LC2C that speaks SAM-BA for tests and DEMO mode.</summary>
public sealed class SimulatedCalculatorTransport : IByteTransport
{
    public static readonly SerialPortInfo DemoPort = new()
    {
        PortName = "COM_DEMO",
        DevicePath = "COM_DEMO",
        Kind = UsbPortKind.AtmelSamBa,
        VendorId = UsbVidPid.AtmelVendor,
        ProductId = UsbVidPid.AtmelSamBaProduct,
    };

    public TimeSpan OperationDelay { get; set; }
    public Dictionary<uint, byte> Memory { get; } = new();
    public bool Closed { get; private set; }

    private readonly List<byte> _inbound = new();
    private readonly List<byte> _outbound = new();
    private (uint Address, int Remaining, int Total)? _pendingPayload;

    public SimulatedCalculatorTransport(TimeSpan operationDelay = default, bool preloadApplication = false)
    {
        OperationDelay = operationDelay;
        StoreWord(FlashCalw.ChipIdCidr, 0xAB0A07E0);
        StoreWord(FlashCalw.ChipIdExid, 0x0400000F);
        StoreWord(FlashCalw.CpuIdAddress, 0x410FC241);

        if (preloadApplication)
        {
            for (var i = 0; i < FlashLayout.ExpectedFirmwareByteCount; i++)
                Memory[FlashLayout.ApplicationStart + (uint)i] = 0;
            Memory[FlashLayout.ApplicationStart] = 0x90;
            Memory[FlashLayout.ApplicationStart + 1] = 0x90;
        }
    }

    public void StoreWord(uint address, uint value)
    {
        for (var i = 0; i < 4; i++)
            Memory[address + (uint)i] = (byte)((value >> (8 * i)) & 0xFF);
    }

    public uint LoadWord(uint address)
    {
        uint value = 0;
        for (var i = 0; i < 4; i++)
            value |= (uint)(GetByte(address + (uint)i) << (8 * i));
        return value;
    }

    internal byte GetByte(uint address) =>
        Memory.TryGetValue(address, out var b) ? b : (byte)0xFF;

    public void Write(ReadOnlySpan<byte> data)
    {
        if (Closed)
            throw FlasherError.NotConnected();

        if (_pendingPayload is { } pending)
        {
            var take = Math.Min(pending.Remaining, data.Length);
            for (var i = 0; i < take; i++)
                Memory[pending.Address + (uint)i] = data[i];

            pending = (pending.Address + (uint)take, pending.Remaining - take, pending.Total);
            _pendingPayload = pending.Remaining > 0 ? pending : null;

            if (_pendingPayload is null)
                DelayForBytes(pending.Total);

            if (take < data.Length)
            {
                _inbound.AddRange(data.Slice(take).ToArray());
                DrainCommands();
            }
            return;
        }

        _inbound.AddRange(data.ToArray());
        DrainCommands();
    }

    public byte[] Read(int maxCount, TimeSpan timeout)
    {
        if (Closed)
            throw FlasherError.NotConnected();
        if (_outbound.Count == 0)
            return Array.Empty<byte>();

        var take = Math.Min(maxCount, _outbound.Count);
        var slice = _outbound.GetRange(0, take);
        _outbound.RemoveRange(0, take);
        return slice.ToArray();
    }

    public void Close()
    {
        _inbound.Clear();
        _outbound.Clear();
        _pendingPayload = null;
    }

    public void Dispose() => Close();

    private void DrainCommands()
    {
        while (_pendingPayload is null)
        {
            var hash = _inbound.IndexOf((byte)'#');
            if (hash < 0)
                return;

            var commandBytes = _inbound.GetRange(0, hash);
            _inbound.RemoveRange(0, hash + 1);
            HandleCommand(System.Text.Encoding.ASCII.GetString(commandBytes.ToArray()));
        }

        if (_pendingPayload is { } pending && _inbound.Count > 0)
        {
            var take = Math.Min(pending.Remaining, _inbound.Count);
            for (var i = 0; i < take; i++)
                Memory[pending.Address + (uint)i] = _inbound[i];
            _inbound.RemoveRange(0, take);
            pending = (pending.Address + (uint)take, pending.Remaining - take, pending.Total);
            _pendingPayload = pending.Remaining > 0 ? pending : null;
            if (_pendingPayload is null)
                DelayForBytes(pending.Total);
        }
    }

    private void HandleCommand(string command)
    {
        switch (command)
        {
            case "T":
                _outbound.AddRange("\r\n>"u8.ToArray());
                return;
            case "N":
                _outbound.AddRange("\r\n"u8.ToArray());
                return;
            case "V":
                _outbound.AddRange("v1.1 Dec 15 2013 19:03:14\r\n"u8.ToArray());
                return;
        }

        if (command.Length == 0)
            throw FlasherError.SambaProtocol("empty command");

        var head = command[0];
        if (head == 'w')
        {
            AppendWordBytes(LoadWord(ParseAddress(command)));
            return;
        }
        if (head == 'W')
        {
            var (address, value) = ParseAddressValue(command);
            StoreWord(address, value);
            return;
        }
        if (head == 'R')
        {
            var (address, length) = ParseAddressValue(command);
            DelayForBytes((int)length);
            for (var i = 0; i < length; i++)
                _outbound.Add(GetByte(address + (uint)i));
            return;
        }
        if (head == 'S')
        {
            var (address, length) = ParseAddressValue(command);
            _pendingPayload = (address, (int)length, (int)length);
            return;
        }
        if (head == 'G')
        {
            RunOfficialApplet(ParseAddress(command));
            return;
        }

        throw FlasherError.SambaProtocol($"unknown command {command}");
    }

    private void RunOfficialApplet(uint goAddress)
    {
        var mailbox = SambaFlashApplet.MailboxAddress;
        if (goAddress != SambaFlashApplet.GoAddress)
            return;

        var cmd = LoadWord(mailbox);
        uint status = 0;

        if (cmd == (uint)SambaAppletCommand.Initialize)
        {
            StoreWord(mailbox + 0x08, 0x00020000);
            StoreWord(mailbox + 0x0C, 0x20002C00);
            StoreWord(mailbox + 0x10, 0x200);
            StoreWord(mailbox + 0x14, 0x00102000);
            StoreWord(mailbox + 0x18, 0x200);
            StoreWord(mailbox + 0x1C, 256);
            StoreWord(mailbox + 0x20, 32);
        }
        else if (cmd == (uint)SambaAppletCommand.Write)
        {
            var buffer = LoadWord(mailbox + 0x08);
            var length = (int)LoadWord(mailbox + 0x0C);
            var offset = LoadWord(mailbox + 0x10);
            if (offset < FlashLayout.ApplicationStart)
            {
                StoreWord(mailbox + 0x08, 0);
                status = 0x02;
            }
            else
            {
                for (var i = 0; i < length; i++)
                    Memory[offset + (uint)i] = GetByte(buffer + (uint)i);
                StoreWord(mailbox + 0x08, (uint)length);
            }
        }
        else if (cmd == (uint)SambaAppletCommand.Unlock)
        {
            status = 0;
        }
        else if (cmd == (uint)SambaAppletCommand.ErasePage)
        {
            var page = (int)LoadWord(mailbox + 0x08);
            if (page < 32)
                status = 0x04;
            else
            {
                var pageAddress = (uint)(page * FlashCalw.PageSize);
                for (var i = 0; i < FlashCalw.PageSize; i++)
                    Memory[pageAddress + (uint)i] = 0xFF;
            }
        }
        else if (cmd == (uint)SambaAppletCommand.Read)
        {
            var buffer = LoadWord(mailbox + 0x08);
            var length = (int)LoadWord(mailbox + 0x0C);
            var offset = LoadWord(mailbox + 0x10);
            for (var i = 0; i < length; i++)
                Memory[buffer + (uint)i] = GetByte(offset + (uint)i);
            StoreWord(mailbox + 0x08, (uint)length);
        }
        else
        {
            status = 0x0F;
        }

        StoreWord(mailbox + 0x04, status);
        StoreWord(mailbox, ~cmd);
    }

    private void DelayForBytes(int byteCount)
    {
        if (OperationDelay <= TimeSpan.Zero || byteCount <= 0)
            return;

        var pages = Math.Max(1, (byteCount + FlashCalw.PageSize - 1) / FlashCalw.PageSize);
        var ms = Math.Min(500, (int)(OperationDelay.TotalMilliseconds * pages));
        if (ms > 0)
            Thread.Sleep(ms);
    }

    private void AppendWordBytes(uint value)
    {
        _outbound.Add((byte)(value & 0xFF));
        _outbound.Add((byte)((value >> 8) & 0xFF));
        _outbound.Add((byte)((value >> 16) & 0xFF));
        _outbound.Add((byte)((value >> 24) & 0xFF));
    }

    private static uint ParseAddress(string command)
    {
        var body = command[1..];
        var addressHex = body.Split(',')[0];
        return Convert.ToUInt32(addressHex, 16);
    }

    private static (uint Address, uint Value) ParseAddressValue(string command)
    {
        var body = command[1..];
        var parts = body.Split(',');
        if (parts.Length != 2)
            throw FlasherError.SambaProtocol($"bad command {command}");
        return (Convert.ToUInt32(parts[0], 16), Convert.ToUInt32(parts[1], 16));
    }
}

public sealed class SimulatedPortListing : ISerialPortListing
{
    public IReadOnlyList<SerialPortInfo> ListPorts() => [SimulatedCalculatorTransport.DemoPort];
}
