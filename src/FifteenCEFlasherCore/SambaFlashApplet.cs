namespace FifteenCEFlasherCore;

public enum SambaAppletCommand : uint
{
    Initialize = 0x00,
    Write = 0x02,
    Read = 0x03,
    Lock = 0x04,
    Unlock = 0x05,
    ErasePage = 0x44,
}

public sealed class SambaAppletInfo
{
    public uint MemorySize { get; init; }
    public uint BufferAddress { get; init; }
    public uint BufferSize { get; init; }
    public uint PageSize { get; init; }
    public uint PageCount { get; init; }
    public uint AppStartPage { get; init; }
    public ushort LockRegionSize { get; init; }
    public ushort LockBitCount { get; init; }
}

public sealed class SambaFlashApplet
{
    public const uint LoadAddress = 0x20002000;
    public const uint MailboxAddress = 0x20002040;
    public const uint GoAddress = 0x20002000;

    public static ReadOnlySpan<byte> Image => SambaFlashAppletImage.Bytes;

    private readonly SambaClient _samba;
    public TimeSpan GoDelay { get; set; }
    public SambaAppletInfo? Info { get; private set; }

    public SambaFlashApplet(SambaClient samba, TimeSpan goDelay = default)
    {
        _samba = samba;
        GoDelay = goDelay;
        if (goDelay > TimeSpan.Zero)
            samba.AfterSendDelay = TimeSpan.FromMilliseconds(20);
    }

    public void Upload()
    {
        _samba.DiscardAvailable();
        _samba.Write(LoadAddress, Image);
    }

    public SambaAppletInfo Initialize(uint comType = 0, uint traceLevel = 0, uint bank = 0)
    {
        _samba.WriteWord(MailboxAddress, (uint)SambaAppletCommand.Initialize);
        _samba.WriteWord(MailboxAddress + 0x04, 0);
        _samba.WriteWord(MailboxAddress + 0x08, comType);
        _samba.WriteWord(MailboxAddress + 0x0C, traceLevel);
        _samba.WriteWord(MailboxAddress + 0x10, bank);
        Run(SambaAppletCommand.Initialize);

        var lockWord = _samba.ReadWord(MailboxAddress + 0x14);
        var loaded = new SambaAppletInfo
        {
            MemorySize = _samba.ReadWord(MailboxAddress + 0x08),
            BufferAddress = _samba.ReadWord(MailboxAddress + 0x0C),
            BufferSize = _samba.ReadWord(MailboxAddress + 0x10),
            PageSize = _samba.ReadWord(MailboxAddress + 0x18),
            PageCount = _samba.ReadWord(MailboxAddress + 0x1C),
            AppStartPage = _samba.ReadWord(MailboxAddress + 0x20),
            LockRegionSize = (ushort)lockWord,
            LockBitCount = (ushort)(lockWord >> 16),
        };

        if (loaded.BufferAddress == 0 || loaded.BufferSize == 0 || loaded.PageSize == 0)
            throw FlasherError.SambaProtocol("applet INIT returned an empty buffer");

        Info = loaded;
        return loaded;
    }

    public SambaAppletInfo LoadAndInitialize(uint comType = 0, uint traceLevel = 0, uint bank = 0)
    {
        Upload();
        return Initialize(comType, traceLevel, bank);
    }

    public void UnlockRegion(int region)
    {
        _samba.WriteWord(MailboxAddress, (uint)SambaAppletCommand.Unlock);
        _samba.WriteWord(MailboxAddress + 0x04, 0);
        _samba.WriteWord(MailboxAddress + 0x08, (uint)region);
        Run(SambaAppletCommand.Unlock);
    }

    public int Write(uint flashOffset, ReadOnlySpan<byte> data)
    {
        if (flashOffset < FlashLayout.ApplicationStart)
            throw FlasherError.WriteWouldTouchBootloader(flashOffset);
        if (Info is null)
            throw FlasherError.SambaProtocol("flash applet is not initialized");
        if (data.IsEmpty || data.Length > Info.BufferSize)
            throw FlasherError.SambaProtocol($"applet WRITE size {data.Length} exceeds buffer {Info.BufferSize}");

        _samba.Write(Info.BufferAddress, data);
        _samba.WriteWord(MailboxAddress, (uint)SambaAppletCommand.Write);
        _samba.WriteWord(MailboxAddress + 0x04, 0);
        _samba.WriteWord(MailboxAddress + 0x08, Info.BufferAddress);
        _samba.WriteWord(MailboxAddress + 0x0C, (uint)data.Length);
        _samba.WriteWord(MailboxAddress + 0x10, flashOffset);
        Run(SambaAppletCommand.Write);
        return (int)_samba.ReadWord(MailboxAddress + 0x08);
    }

    internal void Run(SambaAppletCommand command, TimeSpan? timeout = null)
    {
        _samba.Go(GoAddress);
        var firstWait = FirstPollDelay(command);
        if (firstWait > TimeSpan.Zero)
            Thread.Sleep(firstWait);

        var expected = ~((uint)command);
        var retries = command == SambaAppletCommand.ErasePage ? 25 : 10;
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(12));

        for (var attempt = 0; attempt < retries; attempt++)
        {
            if (DateTime.UtcNow >= deadline)
                break;

            try
            {
                var word = _samba.ReadWord(MailboxAddress, TimeSpan.FromSeconds(1));
                if (word == expected)
                {
                    var status = _samba.ReadWord(MailboxAddress + 0x04);
                    if (status != 0)
                        throw FlasherError.AppletFailed(status);
                    return;
                }
            }
            catch (FlasherError ex) when (ex.Kind == FlasherErrorKind.SambaTimeout)
            {
                if (GoDelay > TimeSpan.Zero && attempt + 1 < retries)
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                continue;
            }

            if (GoDelay > TimeSpan.Zero && attempt + 1 < retries)
                Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        throw FlasherError.SambaTimeout(
            $"Timed out waiting for the SAM-BA flash applet (command 0x{(uint)command:X}). Leave the cable plugged in. If Status is not Connected, hold ERASE, press RESET, then release ERASE.");
    }

    private TimeSpan FirstPollDelay(SambaAppletCommand command)
    {
        if (GoDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;
        _ = command;
        return GoDelay > TimeSpan.FromMilliseconds(100) ? GoDelay : TimeSpan.FromMilliseconds(100);
    }
}
