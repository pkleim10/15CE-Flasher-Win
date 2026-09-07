using FifteenCEFlasherCore;
using Xunit;

namespace FifteenCEFlasherCore.Tests;

public class FlashLayoutTests
{
    [Fact]
    public void ApplicationSizeMatchesOfficialDump()
    {
        Assert.Equal(112 * 1024, FlashLayout.ExpectedFirmwareByteCount);
        Assert.Equal(0x4000u, FlashLayout.ApplicationStart);
        Assert.Equal(0x1C000u, FlashLayout.ApplicationSize);
    }

    [Fact]
    public void SafeRangeRejectsBootloader()
    {
        Assert.False(FlashLayout.IsSafeApplicationRange(0, 16));
        Assert.False(FlashLayout.IsSafeApplicationRange(0x3FF0, 32));
        Assert.True(FlashLayout.IsSafeApplicationRange(0x4000, 0x1C000));
        Assert.False(FlashLayout.IsSafeApplicationRange(0x4000, 0x1C001));
        Assert.False(FlashLayout.IsSafeApplicationRange(0x4000, 0));
    }
}

public class SambaClientTests
{
    [Fact]
    public void ConnectReadsVersion()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        Assert.Contains("v1.1", client.Version);
    }

    [Fact]
    public void ReadAndWriteWord()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        Assert.Equal(0xAB0A07E0u, client.ReadWord(FlashCalw.ChipIdCidr));
        client.WriteWord(0x20000000, 0x11223344);
        Assert.Equal(0x11223344u, client.ReadWord(0x20000000));
    }

    [Fact]
    public void BlockReadWrite()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)(i & 0xFF)).ToArray();
        client.Write(0x20001000, payload);
        Assert.Equal(payload, client.Read(0x20001000, payload.Length));
    }
}

public class FlashCalwTests
{
    [Fact]
    public void IdentifyATSAM4LC2C()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var identity = new FlashCalw(client).Identify();
        Assert.Equal("ATSAM4LC2C", identity.Name);
        Assert.True(identity.IsSupported15C);
    }

    [Fact]
    public void WriteApplicationVerifies()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var image = new byte[FlashLayout.ExpectedFirmwareByteCount];
        for (var i = 0; i < image.Length; i += 17)
            image[i] = (byte)(i & 0xFF);

        new FlashCalw(client).WriteApplication(image);
        Assert.Equal(image, client.Read(FlashLayout.ApplicationStart, image.Length));
        Assert.False(mock.Memory.ContainsKey(0x0000));
    }
}

public class SambaFlashAppletTests
{
    [Fact]
    public void OfficialImageHasVectorTable()
    {
        var image = SambaFlashApplet.Image.ToArray();
        Assert.Equal(2652, image.Length);
        var sp = BitConverter.ToUInt32(image, 0);
        var reset = BitConverter.ToUInt32(image, 4);
        Assert.Equal(0x20007FF0u, sp);
        Assert.Equal(0x20002809u, reset);
    }

    [Fact]
    public void InitializeAndWriteOnePage()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var applet = new SambaFlashApplet(client);
        var info = applet.LoadAndInitialize();
        Assert.Equal(0x20000u, info.MemorySize);
        Assert.Equal(0x200u, info.BufferSize);
        Assert.Equal(0x200u, info.PageSize);
        Assert.Equal(32u, info.AppStartPage);
        Assert.Equal(0x20002C00u, info.BufferAddress);
        Assert.Equal(~(uint)SambaAppletCommand.Initialize, client.ReadWord(SambaFlashApplet.MailboxAddress));

        var page = Enumerable.Range(0, 512).Select(i => (byte)(i & 0xFF)).ToArray();
        Assert.Equal(512, applet.Write(0x4000, page));
        Assert.Equal(~(uint)SambaAppletCommand.Write, client.ReadWord(SambaFlashApplet.MailboxAddress));
        Assert.Equal(page, client.Read(0x4000, 512));
    }
}

public class FlasherTests
{
    [Fact]
    public void WriteWithMockCableProgramsApplicationFlash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hp15c-fw-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[FlashLayout.ExpectedFirmwareByteCount]);
        try
        {
            using var mock = new SimulatedCalculatorTransport();
            var client = new SambaClient(mock);
            client.Connect();
            var flasher = new Flasher
            {
                Ports = new FixedPortListing([SimulatedCalculatorTransport.DemoPort]),
                FlashCommandSettleSeconds = TimeSpan.Zero,
            };
            flasher.Write(path, client: client);
            var dumped = client.Read(FlashLayout.ApplicationStart, FlashLayout.ExpectedFirmwareByteCount);
            Assert.Equal(FlashLayout.ExpectedFirmwareByteCount, dumped.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal sealed class FixedPortListing(IReadOnlyList<SerialPortInfo> ports) : ISerialPortListing
{
    public IReadOnlyList<SerialPortInfo> ListPorts() => ports;
}
