using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using FifteenCEFlasherCore;
using Microsoft.Win32;

namespace FifteenCEFlasher;

public enum AppMode
{
    Demo,
    Flash,
    Batch,
    Probe,
}

public enum BatchPhase
{
    Setup,
    Waiting,
    BackingUp,
    Flashing,
    Done,
    Error,
}

public enum BatchBackupChoice
{
    Skip,
    AutoSave,
}

public sealed class FlasherStore : INotifyPropertyChanged, IDisposable
{
    private const string BatchFirmwarePathKey = "BatchFirmwarePath";
    private readonly DispatcherTimer _pollTimer;
    private CancellationTokenSource? _workCts;
    private SambaClient? _connectedClient;
    private SimulatedCalculatorTransport? _demoTransport;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowWelcome { get; private set; } = true;
    public AppMode SelectedMode { get; private set; } = AppMode.Flash;
    public WizardState Wizard { get; } = new();
    public ConnectionProbeResult? ProbeResult { get; private set; }
    public bool ShowAllPorts { get; set; }

    public string StatusMessage { get; private set; } = "Welcome";
    public string DetailMessage { get; private set; } = "";
    public double Progress { get; private set; }
    public string? FirmwarePath { get; private set; }
    public string? BackupPath { get; private set; }
    public string? PagePreviewHeader { get; private set; }
    public IEnumerable<string> PagePreviewLines { get; private set; } = [];
    public BackupChecksumAssessment? BackupAssessment { get; private set; }
    public FirmwareFileAssessment? FirmwareAssessment { get; private set; }

    public BatchPhase BatchPhase { get; private set; } = BatchPhase.Setup;
    public int BatchUnitNumber { get; private set; }
    public BatchBackupChoice BatchBackupChoice { get; set; } = BatchBackupChoice.Skip;
    public string? BatchBackupFolder { get; private set; }
    public string? BatchSessionStamp { get; private set; }

    public bool IsBatchActive => !ShowWelcome && SelectedMode == AppMode.Batch;
    public bool CanStartBatch =>
        Wizard.FirmwareOk && (BatchBackupChoice == BatchBackupChoice.Skip || BatchBackupFolder is not null);

    public Flasher Flasher { get; private set; }

    public FlasherStore()
    {
        Flasher = HardwareFlasher();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += (_, _) => RefreshConnection();
    }

    private static Flasher HardwareFlasher() => new()
    {
        FlashCommandSettleSeconds = TimeSpan.FromMilliseconds(10),
        Ports = new WindowsComPortListing(),
        OpenTransport = port => new WindowsSerialLink(port.PortName),
    };

    public void Start()
    {
        _pollTimer.Start();
        RefreshConnection();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _workCts?.Cancel();
        Disconnect();
    }

    public void SelectMode(AppMode mode)
    {
        SelectedMode = mode;
        ShowWelcome = false;
        Wizard.Step = WizardStep.Cable;
        ResetWizardFlags();

        if (mode == AppMode.Demo)
        {
            var transport = new SimulatedCalculatorTransport(
                operationDelay: TimeSpan.FromMilliseconds(80),
                preloadApplication: true);
            _demoTransport = transport;
            Flasher = new Flasher
            {
                FlashCommandSettleSeconds = TimeSpan.FromMilliseconds(10),
                Ports = new SimulatedPortListing(),
                OpenTransport = _ => transport,
            };
            Wizard.IdentitySupported = true;
            StatusMessage = "DEMO mode — simulated calculator";
            DetailMessage = "No hardware required.";
        }
        else
        {
            _demoTransport = null;
            Flasher = HardwareFlasher();
            if (mode == AppMode.Batch)
                BeginBatchSession();
        }

        NotifyAll();
    }

    public void ReturnToWelcome()
    {
        _workCts?.Cancel();
        Disconnect();
        ShowWelcome = true;
        BatchPhase = BatchPhase.Setup;
        BatchUnitNumber = 0;
        FirmwarePath = null;
        BackupPath = null;
        ResetWizardFlags();
        StatusMessage = "Welcome";
        DetailMessage = "";
        NotifyAll();
    }

    public void WizardAdvance()
    {
        Wizard.Advance();
        Notify(nameof(Wizard));
    }

    public void WizardBack()
    {
        Wizard.GoBack();
        Notify(nameof(Wizard));
    }

    public void PickFirmware()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Firmware (*.bin)|*.bin|All files|*.*",
            Title = "Choose firmware file",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            FirmwareImage.Validate(File.ReadAllBytes(dialog.FileName));
            FirmwarePath = dialog.FileName;
            Wizard.FirmwareOk = true;
            FirmwareAssessment = VoyagerFirmwareChecksum.FirmwareFileAssessment(
                File.ReadAllBytes(dialog.FileName),
                BackupAssessment,
                backupSkipped: BackupPath is null);

            if (SelectedMode == AppMode.Batch)
                SaveBatchFirmwarePath(dialog.FileName);

            NotifyAll();
        }
        catch (FlasherError ex)
        {
            MessageBox.Show(ex.Message, "Invalid firmware", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task SaveBackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Firmware backup (*.bin)|*.bin",
            FileName = $"hp15c-backup-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
            Title = "Save backup",
        };
        if (dialog.ShowDialog() != true)
            return;

        BackupPath = dialog.FileName;
        NotifyAll();
        await RunBackupAsync();
    }

    public void SkipBackup()
    {
        BackupPath = null;
        BackupAssessment = null;
        Wizard.BackupResolved = true;
        NotifyAll();
    }

    public void PickBatchBackupFolder()
    {
        // WPF has no built-in folder picker — use OpenFileDialog hack or WinForms.
        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select folder",
            Title = "Choose backup folder",
        };
        if (dialog.ShowDialog() != true)
            return;

        BatchBackupFolder = Path.GetDirectoryName(dialog.FileName);
        Notify(nameof(BatchBackupFolder));
        Notify(nameof(CanStartBatch));
    }

    public void BeginBatchSession()
    {
        BatchPhase = BatchPhase.Setup;
        BatchUnitNumber = 0;
        BatchSessionStamp = null;
        BatchBackupChoice = BatchBackupChoice.Skip;
        BatchBackupFolder = null;
        RestoreBatchFirmwarePath();
        NotifyAll();
    }

    public void StartBatchRun()
    {
        if (!CanStartBatch)
            return;

        BatchSessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        BatchUnitNumber = 1;
        BatchPhase = BatchPhase.Waiting;
        NotifyAll();
    }

    public void StopBatch() => Application.Current.Shutdown();

    public async Task RunBackupAsync()
    {
        if (Wizard.IsBusy)
            return;

        Wizard.IsBusy = true;
        StatusMessage = "Reading firmware…";
        Progress = 0;
        NotifyAll();
        try
        {
            await Task.Run(() =>
            {
                var path = BackupPath ?? throw new InvalidOperationException("No backup path");
                var client = EnsureConnected();
                Flasher.Read(path, client, (f, _) => ReportProgress(f, "Reading firmware…"));
            });

            var data = File.ReadAllBytes(BackupPath!);
            BackupAssessment = VoyagerFirmwareChecksum.BackupAssessment(data);
            Wizard.BackupResolved = true;
            StatusMessage = "Backup saved";
            DetailMessage = BackupAssessment.Message;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Wizard.IsBusy = false;
            Progress = 0;
            NotifyAll();
        }
    }

    public async Task RunFlashAsync()
    {
        if (Wizard.IsBusy || FirmwarePath is null)
            return;

        Wizard.IsBusy = true;
        StatusMessage = "Writing firmware…";
        Progress = 0;
        PagePreviewHeader = null;
        PagePreviewLines = [];
        NotifyAll();
        try
        {
            FlashPageWrite? latestPage = null;
            await Task.Run(() =>
            {
                var client = EnsureConnected();
                Flasher.Write(
                    FirmwarePath,
                    client: client,
                    progress: (f, phase) =>
                    {
                        var status = phase switch
                        {
                            FlashProgressPhase.Writing => "Writing firmware…",
                            FlashProgressPhase.Verifying => "Verifying…",
                            _ => StatusMessage,
                        };
                        if (phase == FlashProgressPhase.Verifying)
                            ReportProgress(f, status, clearPagePreview: true);
                        else
                            ReportProgress(f, status, latestPage);
                    },
                    pageProgress: page => latestPage = page);
            });

            Wizard.FlashSucceeded = true;
            PagePreviewHeader = null;
            PagePreviewLines = [];
            StatusMessage = "Flashed and verified.";
            DetailMessage = "Press RESET on the calculator to restart.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Flash failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Wizard.IsBusy = false;
            Progress = 0;
            NotifyAll();
        }
    }

    private void RefreshConnection()
    {
        if (ShowWelcome)
            return;

        if (SelectedMode == AppMode.Probe)
        {
            var probe = new ConnectionProbeService
            {
                PortListing = Flasher.Ports,
                OpenTransport = Flasher.OpenTransport,
            }.Poll();
            ProbeResult = probe;
            StatusMessage = probe.StatusText;
            DetailMessage = probe.DetailText ?? "";
            NotifyAll();
            return;
        }

        if (SelectedMode == AppMode.Demo)
        {
            Wizard.IdentitySupported = true;
            StatusMessage = "Connected: ATSAM4LC2C (DEMO)";
            DetailMessage = "Simulated calculator";
            NotifyAll();
            return;
        }

        if (Wizard.IsBusy || BatchPhase is BatchPhase.BackingUp or BatchPhase.Flashing)
            return;

        if (Wizard.FlashSucceeded && Wizard.Step == WizardStep.Flash)
            return;

        try
        {
            Disconnect();
            var connected = Flasher.Connect(TimeSpan.FromSeconds(2));
            _connectedClient = connected.Client;
            Wizard.IdentitySupported = connected.Identity.IsSupported15C;
            StatusMessage = connected.Identity.IsSupported15C
                ? $"Connected: {connected.Identity.Name}"
                : "Unsupported chip";
            DetailMessage = $"{connected.Port.PortName} · {connected.Client.Version.Trim()}";

            if (IsBatchActive && BatchPhase == BatchPhase.Waiting)
                _ = RunBatchUnitAsync();
        }
        catch
        {
            Disconnect();
            Wizard.IdentitySupported = false;
            StatusMessage = "Waiting…";
            DetailMessage = "Hold ERASE, press RESET, then release ERASE.";
        }

        NotifyAll();
    }

    private async Task RunBatchUnitAsync()
    {
        if (BatchPhase != BatchPhase.Waiting || FirmwarePath is null)
            return;

        _workCts?.Cancel();
        _workCts = new CancellationTokenSource();
        var token = _workCts.Token;

        try
        {
            if (BatchBackupChoice == BatchBackupChoice.AutoSave && BatchBackupFolder is not null && BatchSessionStamp is not null)
            {
                BatchPhase = BatchPhase.BackingUp;
                Notify(nameof(BatchPhase));
                var backupPath = BatchBackupNaming.FilePath(BatchBackupFolder, BatchSessionStamp, BatchUnitNumber);
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var client = EnsureConnected();
                    Flasher.Read(backupPath, client);
                }, token);
            }

            BatchPhase = BatchPhase.Flashing;
            Notify(nameof(BatchPhase));
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var client = EnsureConnected();
                Flasher.Write(FirmwarePath, client: client);
            }, token);

            BatchPhase = BatchPhase.Done;
            StatusMessage = $"Unit {BatchUnitNumber} complete";
            DetailMessage = "Press RESET, then connect the next calculator.";
            BatchUnitNumber++;
            BatchPhase = BatchPhase.Waiting;
        }
        catch (OperationCanceledException)
        {
            // Ignored.
        }
        catch (Exception ex)
        {
            BatchPhase = BatchPhase.Error;
            StatusMessage = "Batch error";
            DetailMessage = ex.Message;
        }

        NotifyAll();
    }

    private SambaClient EnsureConnected()
    {
        if (_connectedClient is not null)
            return _connectedClient;

        if (SelectedMode == AppMode.Demo && _demoTransport is not null)
        {
            var client = new SambaClient(_demoTransport);
            client.Connect();
            _connectedClient = client;
            return client;
        }

        var connected = Flasher.Connect();
        _connectedClient = connected.Client;
        return connected.Client;
    }

    private void Disconnect()
    {
        _connectedClient?.Close();
        _connectedClient = null;
    }

    private void ResetWizardFlags()
    {
        Wizard.IdentitySupported = false;
        Wizard.BackupResolved = false;
        Wizard.FirmwareOk = false;
        Wizard.FlashSucceeded = false;
        Wizard.IsBusy = false;
        BackupAssessment = null;
        FirmwareAssessment = null;
        PagePreviewHeader = null;
        PagePreviewLines = [];
    }

    private static void SaveBatchFirmwarePath(string path) =>
        Registry.CurrentUser.CreateSubKey(@"Software\MachII\15CEFlasher")?.SetValue(BatchFirmwarePathKey, path);

    private void RestoreBatchFirmwarePath()
    {
        var path = Registry.CurrentUser.OpenSubKey(@"Software\MachII\15CEFlasher")?.GetValue(BatchFirmwarePathKey) as string;
        if (path is null || !File.Exists(path))
            return;

        try
        {
            FirmwareImage.Validate(File.ReadAllBytes(path));
            FirmwarePath = path;
            Wizard.FirmwareOk = true;
        }
        catch
        {
            Registry.CurrentUser.CreateSubKey(@"Software\MachII\15CEFlasher")?.DeleteValue(BatchFirmwarePathKey, false);
        }
    }

    private void ReportProgress(double fraction, string status, FlashPageWrite? page = null, bool clearPagePreview = false)
    {
        RunOnUi(() =>
        {
            Progress = fraction;
            StatusMessage = status;
            if (clearPagePreview)
            {
                PagePreviewHeader = null;
                PagePreviewLines = [];
            }
            else if (page is not null)
            {
                PagePreviewHeader = page.Header;
                PagePreviewLines = page.FormattedWordLines().ToList();
            }

            Notify(nameof(Progress));
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private void NotifyAll()
    {
        Notify(nameof(ShowWelcome));
        Notify(nameof(SelectedMode));
        Notify(nameof(Wizard));
        Notify(nameof(ProbeResult));
        Notify(nameof(StatusMessage));
        Notify(nameof(DetailMessage));
        Notify(nameof(Progress));
        Notify(nameof(FirmwarePath));
        Notify(nameof(BackupPath));
        Notify(nameof(PagePreviewHeader));
        Notify(nameof(PagePreviewLines));
        Notify(nameof(BackupAssessment));
        Notify(nameof(FirmwareAssessment));
        Notify(nameof(BatchPhase));
        Notify(nameof(BatchUnitNumber));
        Notify(nameof(BatchBackupFolder));
        Notify(nameof(IsBatchActive));
        Notify(nameof(CanStartBatch));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
