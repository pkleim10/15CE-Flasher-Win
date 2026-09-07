namespace FifteenCEFlasherCore;

public enum WizardStep
{
    Cable = 0,
    ProgrammingMode = 1,
    Backup = 2,
    Firmware = 3,
    Flash = 4,
    Finish = 5,
    Checksum = 6,
}

public static class WizardStepExtensions
{
    public static int Number(this WizardStep step) => (int)step + 1;

    public static string Title(this WizardStep step) => step switch
    {
        WizardStep.Cable => "Cable",
        WizardStep.ProgrammingMode => "Programming mode",
        WizardStep.Backup => "Backup",
        WizardStep.Firmware => "Firmware",
        WizardStep.Flash => "Flash",
        WizardStep.Finish => "Restart",
        WizardStep.Checksum => "Checksum",
        _ => step.ToString(),
    };
}

public sealed class WizardState
{
    public WizardStep Step { get; set; } = WizardStep.Cable;
    public bool IdentitySupported { get; set; }
    public bool BackupResolved { get; set; }
    public bool FirmwareOk { get; set; }
    public bool FlashSucceeded { get; set; }
    public bool IsBusy { get; set; }

    public bool CanGoBack => !IsBusy && Step != WizardStep.Cable;

    public bool CanAdvance => !IsBusy && Step switch
    {
        WizardStep.Cable => true,
        WizardStep.ProgrammingMode => IdentitySupported,
        WizardStep.Backup => BackupResolved,
        WizardStep.Firmware => FirmwareOk,
        WizardStep.Flash => FlashSucceeded,
        WizardStep.Finish => true,
        WizardStep.Checksum => false,
        _ => false,
    };

    public void Advance()
    {
        if (!CanAdvance)
            return;
        if ((int)Step < Enum.GetValues<WizardStep>().Length - 1)
            Step = (WizardStep)((int)Step + 1);
    }

    public void GoBack()
    {
        if (!CanGoBack)
            return;
        Step = (WizardStep)((int)Step - 1);
    }

    public bool IsComplete(WizardStep other) => (int)Step > (int)other;
    public bool IsUpcoming(WizardStep other) => (int)Step < (int)other;
}
