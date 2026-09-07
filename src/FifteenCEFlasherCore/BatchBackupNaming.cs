namespace FifteenCEFlasherCore;

public static class BatchBackupNaming
{
    public static string Filename(string sessionStamp, int unitNumber) =>
        $"hp15c-{sessionStamp}-{unitNumber:D3}.bin";

    public static string FilePath(string folder, string sessionStamp, int unitNumber) =>
        Path.Combine(folder, Filename(sessionStamp, unitNumber));
}
