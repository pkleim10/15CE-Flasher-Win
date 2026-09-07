namespace FifteenCEFlasherCore;

public enum UsbPortKind
{
    Unknown,
    Ftdi,
    AtmelSamBa,
}

public sealed class SerialPortInfo
{
    public string PortName { get; init; } = "";
    public string DevicePath { get; init; } = "";
    public UsbPortKind Kind { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }

    public string DisplayName => string.IsNullOrEmpty(PortName) ? DevicePath : PortName;

    public bool IsLikelyProgrammingCable =>
        Kind == UsbPortKind.AtmelSamBa || Kind == UsbPortKind.Ftdi;
}

public interface ISerialPortListing
{
    IReadOnlyList<SerialPortInfo> ListPorts();
}

public static class SerialPortInfoExtensions
{
    public static IEnumerable<SerialPortInfo> ProgrammingCables(this IEnumerable<SerialPortInfo> ports) =>
        ports.Where(p => p.IsLikelyProgrammingCable);

    public static IEnumerable<SerialPortInfo> AtmelSamBaPorts(this IEnumerable<SerialPortInfo> ports) =>
        ports.Where(p => p.Kind == UsbPortKind.AtmelSamBa);
}

public static class UsbVidPid
{
    public const ushort FtdiVendor = 0x0403;
    public const ushort FtdiProduct = 0x6015;
    public const ushort AtmelVendor = 0x03EB;
    public const ushort AtmelSamBaProduct = 0x6124;
}
