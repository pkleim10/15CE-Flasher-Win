using System.IO.Ports;
using System.Management;

namespace FifteenCEFlasherCore;

/// <summary>Win32 COM enumeration with USB VID/PID via WMI.</summary>
public sealed class WindowsComPortListing : ISerialPortListing
{
    public IReadOnlyList<SerialPortInfo> ListPorts()
    {
        var byPort = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            byPort[name] = new SerialPortInfo
            {
                PortName = name,
                DevicePath = name,
                Kind = UsbPortKind.Unknown,
            };
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var com = ExtractComPort(name);
                if (com is null)
                    continue;

                var pnp = obj["PNPDeviceID"]?.ToString() ?? obj["DeviceID"]?.ToString() ?? "";
                var (vid, pid) = ParseVidPid(pnp);
                var kind = Classify(vid, pid);

                byPort[com] = new SerialPortInfo
                {
                    PortName = com,
                    DevicePath = com,
                    Kind = kind,
                    VendorId = vid,
                    ProductId = pid,
                };
            }
        }
        catch (ManagementException)
        {
            // WMI unavailable in some CI environments — fall back to bare COM names.
        }

        return byPort.Values.OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Windows PnP IDs put VID and PID in one segment: USB\VID_03EB&amp;PID_6124\...
    /// FTDI often uses plus: FTDIBUS\VID_0403+PID_6015+...
    /// </summary>
    internal static (ushort? Vid, ushort? Pid) ParseVidPid(string pnpDeviceId)
    {
        var upper = pnpDeviceId.ToUpperInvariant();
        return (ParseHexToken(upper, "VID_"), ParseHexToken(upper, "PID_"));
    }

    private static ushort? ParseHexToken(string upper, string token)
    {
        var index = upper.IndexOf(token, StringComparison.Ordinal);
        if (index < 0)
            return null;
        var hexStart = index + token.Length;
        if (hexStart + 4 > upper.Length)
            return null;
        var hex = upper.Substring(hexStart, 4);
        return ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? value
            : null;
    }

    internal static UsbPortKind Classify(ushort? vid, ushort? pid)
    {
        if (vid == UsbVidPid.AtmelVendor && pid == UsbVidPid.AtmelSamBaProduct)
            return UsbPortKind.AtmelSamBa;
        if (vid == UsbVidPid.FtdiVendor && pid == UsbVidPid.FtdiProduct)
            return UsbPortKind.Ftdi;
        return UsbPortKind.Unknown;
    }

    private static string? ExtractComPort(string name)
    {
        var start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        var end = name.IndexOf(')', start);
        if (end < 0)
            return null;
        return name[(start + 1)..end];
    }
}
