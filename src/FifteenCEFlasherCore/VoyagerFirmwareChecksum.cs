namespace FifteenCEFlasherCore;

public static class VoyagerFirmwareChecksum
{
    public const ushort FactoryDisplayed = 0x9090;
    public const ushort Official2024Displayed = 0x0A0A;

    public static ushort DisplayedValue(byte[] data)
    {
        var payload = TrimTrailingZeros(data);
        if (payload.Length < 2)
            return 0;

        ushort sum = 0;
        for (var i = 0; i < payload.Length - 1; i++)
            sum = (ushort)((sum + payload[i]) & 0xFF);

        return (ushort)((sum << 8) | sum);
    }

    public static string Formatted(ushort value) => $"{value:X4}h";

    public static string Formatted(byte[] data) => Formatted(DisplayedValue(data));

    public static string TestMenuDisplay(ushort value) => $"ChE - - {Formatted(value)}";

    public static BackupChecksumAssessment BackupAssessment(byte[] data) =>
        new(DisplayedValue(data));

    public static FirmwareFileAssessment FirmwareFileAssessment(byte[] data, BackupChecksumAssessment? backup, bool backupSkipped) =>
        new(DisplayedValue(data), backupSkipped ? null : backup);

    private static byte[] TrimTrailingZeros(byte[] data)
    {
        var end = data.Length;
        while (end > 0 && data[end - 1] == 0)
            end--;
        return data.AsSpan(0, end).ToArray();
    }
}

public enum BackupChecksumKind
{
    FactoryOriginal,
    Official2024,
    Unrecognized,
}

public sealed class BackupChecksumAssessment
{
    public BackupChecksumKind Kind { get; }
    public ushort Displayed { get; }

    public BackupChecksumAssessment(ushort displayed)
    {
        Displayed = displayed;
        Kind = displayed switch
        {
            VoyagerFirmwareChecksum.FactoryDisplayed => BackupChecksumKind.FactoryOriginal,
            VoyagerFirmwareChecksum.Official2024Displayed => BackupChecksumKind.Official2024,
            _ => BackupChecksumKind.Unrecognized,
        };
    }

    public bool IsRecognized => Kind is BackupChecksumKind.FactoryOriginal or BackupChecksumKind.Official2024;

    public string Message
    {
        get
        {
            var label = VoyagerFirmwareChecksum.Formatted(Displayed);
            return Kind switch
            {
                BackupChecksumKind.FactoryOriginal =>
                    $"Checksum {label}. This is the recognized factory installed firmware. It is safe to proceed.",
                BackupChecksumKind.Official2024 =>
                    $"Checksum {label}: This is a recognized version of the firmware. It is safe to proceed.",
                _ =>
                    $"Checksum {label}. This is not a recognized firmware version. If you know you are currently using a custom version of the firmware, proceed at your own risk. If you are currently using the factory installed firmware, there may be a problem with the backup.",
            };
        }
    }
}

public enum FirmwareFileKind
{
    AlreadyOnCalculator,
    KnownLatest,
    DowngradeToFactory,
    FactoryNotLatest,
    Unrecognized,
}

public sealed class FirmwareFileAssessment
{
    public FirmwareFileKind Kind { get; }
    public ushort Displayed { get; }

    public FirmwareFileAssessment(ushort displayed, BackupChecksumAssessment? backup)
    {
        Displayed = displayed;
        if (backup is not null && backup.Displayed == displayed)
        {
            Kind = FirmwareFileKind.AlreadyOnCalculator;
            return;
        }

        Kind = displayed switch
        {
            VoyagerFirmwareChecksum.Official2024Displayed => FirmwareFileKind.KnownLatest,
            VoyagerFirmwareChecksum.FactoryDisplayed when backup?.Kind == BackupChecksumKind.Official2024 =>
                FirmwareFileKind.DowngradeToFactory,
            VoyagerFirmwareChecksum.FactoryDisplayed => FirmwareFileKind.FactoryNotLatest,
            _ => FirmwareFileKind.Unrecognized,
        };
    }

    public bool IsCaution => Kind != FirmwareFileKind.KnownLatest;

    public string Message
    {
        get
        {
            var label = VoyagerFirmwareChecksum.Formatted(Displayed);
            return Kind switch
            {
                FirmwareFileKind.AlreadyOnCalculator =>
                    $"Checksum {label}. This firmware is already on the calculator. You don't need to install it again.",
                FirmwareFileKind.KnownLatest =>
                    $"Checksum {label}. This is the latest known firmware version. It is safe to proceed.",
                FirmwareFileKind.DowngradeToFactory =>
                    $"Checksum {label}. This is the factory-installed firmware. The calculator currently has a newer recognized version. Are you sure you want to install it?",
                FirmwareFileKind.FactoryNotLatest =>
                    $"Checksum {label}. This is the factory-installed firmware. It is not the latest known version. Are you sure you want to install it?",
                _ =>
                    $"Checksum {label}. This is not a known firmware version. You may have selected the wrong file, or firmware meant for a different calculator. Are you sure you want to install it?",
            };
        }
    }
}
