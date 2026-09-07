namespace FifteenCEFlasherCore;

public sealed class FirmwareImage
{
    public string FilePath { get; }
    public byte[] Data { get; }

    public int ByteCount => Data.Length;

    public FirmwareImage(string filePath, byte[] data)
    {
        FilePath = filePath;
        Data = data;
    }

    public static FirmwareImage Load(string path, bool requireExactSize = true)
    {
        if (!File.Exists(path))
            throw FlasherError.FirmwareNotFound(path);

        var data = File.ReadAllBytes(path);
        Validate(data, requireExactSize);
        return new FirmwareImage(System.IO.Path.GetFullPath(path), data);
    }

    public static void Validate(byte[] data, bool requireExactSize = true)
    {
        if (data.Length == 0)
            throw FlasherError.FirmwareEmpty();

        if (data.Length > FlashLayout.ExpectedFirmwareByteCount)
            throw FlasherError.FirmwareTooLarge(data.Length, FlashLayout.ExpectedFirmwareByteCount);

        if (requireExactSize && data.Length != FlashLayout.ExpectedFirmwareByteCount)
            throw FlasherError.FirmwareWrongSize(data.Length, FlashLayout.ExpectedFirmwareByteCount);
    }
}
