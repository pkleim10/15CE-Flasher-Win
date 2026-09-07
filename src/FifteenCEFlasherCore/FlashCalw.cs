namespace FifteenCEFlasherCore;

public sealed class DeviceIdentity
{
    public uint Cidr { get; init; }
    public uint Exid { get; init; }
    public uint CpuId { get; init; }
    public string Name { get; init; } = "";
    public int FlashBytes { get; init; }

    public bool IsSupported15C =>
        FlashBytes == FlashLayout.ExpectedFirmwareByteCount + (int)FlashLayout.BootloaderSize
        && IsCortexM4
        && IsSam4L;

    public bool IsCortexM4 => ((CpuId >> 4) & 0xFFF) == 0xC24;

    public bool IsSam4L => ((Cidr >> 20) & 0xFF) == 0xB0;
}

internal static class Sam4LChipTable
{
    private const uint Cidr128Kb = 0xAB0A07E0;
    private const uint CidrMask = 0xFFFFFFE0;

    private static readonly (uint Cidr, uint Exid, string Name, int FlashKb)[] Known =
    [
        (0xAB0A07E0, 0x0400000F, "ATSAM4LC2C", 128),
        (0xAB0A07E0, 0x0300000F, "ATSAM4LC2B", 128),
        (0xAB0A07E0, 0x0200000F, "ATSAM4LC2A", 128),
        (0xAB0A07E0, 0x04000002, "ATSAM4LS2C", 128),
        (0xAB0A07E0, 0x03000002, "ATSAM4LS2B", 128),
        (0xAB0A07E0, 0x02000002, "ATSAM4LS2A", 128),
        (0xAB0A09E0, 0x0400000F, "ATSAM4LC4C", 256),
        (0xAB0B0AE0, 0x1400000F, "ATSAM4LC8C", 512),
    ];

    public static DeviceIdentity Identify(uint cidr, uint exid, uint cpuid)
    {
        foreach (var (knownCidr, knownExid, name, flashKb) in Known)
        {
            if ((knownCidr & CidrMask) == (cidr & CidrMask) && knownExid == exid)
                return new DeviceIdentity { Cidr = cidr, Exid = exid, CpuId = cpuid, Name = name, FlashBytes = flashKb * 1024 };
        }

        if ((cidr & CidrMask) == (Cidr128Kb & CidrMask))
            return new DeviceIdentity { Cidr = cidr, Exid = exid, CpuId = cpuid, Name = "ATSAM4Lx2", FlashBytes = 128 * 1024 };

        return new DeviceIdentity { Cidr = cidr, Exid = exid, CpuId = cpuid, Name = "unknown", FlashBytes = 0 };
    }
}

public sealed class FlashPageWrite
{
    public uint FlashOffset { get; init; }
    public int PageIndex { get; init; }
    public int PageCount { get; init; }
    public byte[] PageData { get; init; } = [];

    public string Header => $"Page {PageIndex + 1} of {PageCount} · 0x{FlashOffset:X5}";

    public IEnumerable<string> FormattedWordLines(int wordsPerLine = 16)
    {
        var words = new List<string>();
        for (var index = 0; index + 1 < PageData.Length; index += 2)
        {
            var word = PageData[index] | (PageData[index + 1] << 8);
            words.Add($"{word:X4}");
            if (words.Count == wordsPerLine)
            {
                yield return string.Join(" ", words);
                words.Clear();
            }
        }
        if (words.Count > 0)
            yield return string.Join(" ", words);
    }
}

public sealed class FlashCalw
{
    public const uint ChipIdCidr = 0x400E0740;
    public const uint ChipIdExid = 0x400E0744;
    public const uint CpuIdAddress = 0xE000ED00;
    public const int PageSize = 512;
    public const int LockRegions = 16;

    private readonly SambaClient _samba;
    public TimeSpan CommandSettleSeconds { get; set; }

    public FlashCalw(SambaClient samba, TimeSpan commandSettleSeconds = default)
    {
        _samba = samba;
        CommandSettleSeconds = commandSettleSeconds;
    }

    public DeviceIdentity Identify()
    {
        var cpuid = _samba.ReadWord(CpuIdAddress);
        var cidr = _samba.ReadWord(ChipIdCidr);
        var exid = _samba.ReadWord(ChipIdExid);
        return Sam4LChipTable.Identify(cidr, exid, cpuid);
    }

    public byte[] ReadApplication(Action<double>? progress = null)
    {
        var total = FlashLayout.ExpectedFirmwareByteCount;
        var data = new byte[total];
        var offset = 0;
        while (offset < total)
        {
            var chunk = Math.Min(SambaClient.BlockSize, total - offset);
            var part = _samba.Read(FlashLayout.ApplicationStart + (uint)offset, chunk);
            Buffer.BlockCopy(part, 0, data, offset, chunk);
            offset += chunk;
            progress?.Invoke((double)offset / total);
        }
        return data;
    }

    public void WriteApplication(
        byte[] data,
        Action<double>? progress = null,
        Action<double>? verifyProgress = null,
        Action<FlashPageWrite>? pageProgress = null,
        bool verify = true)
    {
        FirmwareImage.Validate(data, requireExactSize: true);
        var address = FlashLayout.ApplicationStart;
        if (!FlashLayout.IsSafeApplicationRange(address, (uint)data.Length))
            throw FlasherError.WriteWouldTouchBootloader(address);

        var identity = WithTimeout("identifying the calculator", Identify);
        if (!identity.IsSupported15C)
            throw FlasherError.UnsupportedDevice(identity.Name, identity.Cidr, identity.Exid);

        var applet = new SambaFlashApplet(_samba, CommandSettleSeconds);
        WithTimeout("uploading the flash applet", () => applet.Upload());
        var info = WithTimeout("starting the flash applet", () => applet.Initialize());
        WithTimeout("unlocking flash", () => UnlockApplicationRegions(applet, info));

        var pageSize = (int)(info.PageSize == 0 ? PageSize : info.PageSize);
        var pageCount = (data.Length + pageSize - 1) / pageSize;
        for (var index = 0; index < pageCount; index++)
        {
            var flashOffset = address + (uint)(index * pageSize);
            if (flashOffset < FlashLayout.ApplicationStart)
                throw FlasherError.WriteWouldTouchBootloader(flashOffset);

            var start = index * pageSize;
            var end = Math.Min(start + pageSize, data.Length);
            var pageData = new byte[pageSize];
            Buffer.BlockCopy(data, start, pageData, 0, end - start);
            if (end - start < pageSize)
                Array.Fill(pageData, (byte)0xFF, end - start, pageSize - (end - start));

            WithTimeout($"writing page {index + 1} of {pageCount}", () => applet.Write(flashOffset, pageData));
            pageProgress?.Invoke(new FlashPageWrite
            {
                FlashOffset = flashOffset,
                PageIndex = index,
                PageCount = pageCount,
                PageData = pageData,
            });
            progress?.Invoke((double)(index + 1) / pageCount);
        }
        progress?.Invoke(1.0);

        if (!verify)
            return;

        VerifyApplication(data, verifyProgress);
    }

    public void VerifyApplication(byte[] data, Action<double>? progress = null)
    {
        FirmwareImage.Validate(data, requireExactSize: true);
        progress?.Invoke(0);
        var readback = WithTimeout("verifying firmware", () => ReadApplication(progress));
        if (!readback.AsSpan().SequenceEqual(data))
            throw FlasherError.VerifyMismatch();
        progress?.Invoke(1.0);
    }

    private void UnlockApplicationRegions(SambaFlashApplet applet, SambaAppletInfo info)
    {
        var lockBits = Math.Max(1, (int)(info.LockBitCount == 0 ? LockRegions : info.LockBitCount));
        var pageCount = (int)(info.PageCount == 0 ? 256 : info.PageCount);
        var pagesPerRegion = Math.Max(1, pageCount / lockBits);
        var firstAppPage = (int)(info.AppStartPage == 0 ? 32 : info.AppStartPage);
        var firstRegion = firstAppPage / pagesPerRegion;

        for (var region = firstRegion; region < lockBits; region++)
        {
            try
            {
                applet.UnlockRegion(region);
            }
            catch (FlasherError ex) when (ex.Kind == FlasherErrorKind.AppletFailed)
            {
                // Some regions may already be unlocked.
            }
        }
    }

    private T WithTimeout<T>(string stage, Func<T> body)
    {
        try
        {
            return body();
        }
        catch (FlasherError ex) when (ex.Kind == FlasherErrorKind.SambaTimeout)
        {
            throw FlasherError.SambaTimeout(
                $"Timed out {stage}. Leave the cable plugged in. If Status is not Connected, hold ERASE, press RESET, then release ERASE.");
        }
    }

    private void WithTimeout(string stage, Action body) => WithTimeout(stage, () => { body(); return 0; });
}
