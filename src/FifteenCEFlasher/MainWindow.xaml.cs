using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FifteenCEFlasherCore;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace FifteenCEFlasher;

public partial class MainWindow : Window
{
    private readonly FlasherStore _store = new();

    public MainWindow()
    {
        InitializeComponent();
        SubtitleText.Text = AppVersionLabel();
        _store.PropertyChanged += (_, _) =>
        {
            if (Dispatcher.CheckAccess())
                UpdateUi();
            else
                Dispatcher.Invoke(UpdateUi);
        };
        _store.Start();
        UpdateUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        _store.Dispose();
        base.OnClosed(e);
    }

    private static string AppVersionLabel()
    {
        var path = Environment.ProcessPath;
        if (path is null)
            return "Mach II Labs · Windows";

        var info = FileVersionInfo.GetVersionInfo(path);
        return $"Mach II Labs · Windows · {info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart} ({info.FilePrivatePart})";
    }

    private void UpdateUi()
    {
        StatusText.Text = _store.StatusMessage;
        DetailText.Text = _store.DetailMessage;
        ProgressBar.Value = _store.Progress;
        ProgressBar.IsIndeterminate = _store.Wizard.IsBusy && _store.Progress <= 0;
        ProgressBar.Visibility = _store.Wizard.IsBusy || _store.Progress > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusBar.Visibility = ShowStatusBar() ? Visibility.Visible : Visibility.Collapsed;

        if (_store.ShowWelcome)
            MainContent.Content = Scroll(BuildWelcome());
        else if (_store.SelectedMode is AppMode.Probe || _store.IsBatchActive)
            MainContent.Content = Scroll(BuildActiveView());
        else
            MainContent.Content = BuildWizardView();
    }

    private UIElement BuildWelcome()
    {
        var panel = new StackPanel { MaxWidth = 520 };

        panel.Children.Add(new TextBlock
        {
            Text = "Flash HP 15c CE firmware on Windows.",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(MakeModeButton("FLASH", "Guided 7-step wizard", AppMode.Flash));
        panel.Children.Add(MakeModeButton("BATCH", "Flash many calculators in a row", AppMode.Batch));
        panel.Children.Add(MakeModeButton("DEMO", "Try the wizard without hardware", AppMode.Demo));
        panel.Children.Add(MakeModeButton("Connection Probe", "Test cable detection only (beta)", AppMode.Probe));

        return panel;
    }

    private Button MakeModeButton(string title, string subtitle, AppMode mode)
    {
        var btn = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 16 });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        btn.Content = stack;
        btn.Click += (_, _) => _store.SelectMode(mode);
        return btn;
    }

    private UIElement BuildActiveView()
    {
        if (_store.SelectedMode == AppMode.Probe)
            return BuildProbeView();

        if (_store.IsBatchActive)
            return BuildBatchView();

        return BuildWizardView();
    }

    private UIElement BuildProbeView()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Connection Probe (beta)",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Hold ERASE, press RESET, then release ERASE.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(WizardDiagram("wizard-programming-mode.png"));

        var ports = _store.ProbeResult?.AllPorts ?? [];
        if (ports.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "COM ports:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            foreach (var port in ports)
            {
                var label = port.Kind switch
                {
                    UsbPortKind.AtmelSamBa => $"{port.PortName} · Atmel SAM-BA (03EB:6124)",
                    UsbPortKind.Ftdi => $"{port.PortName} · FTDI (0403:6015) — ignore for SAM-BA",
                    _ => $"{port.PortName} · unknown",
                };
                panel.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 0, 0, 2) });
            }
        }

        panel.Children.Add(MakeFooterButtons(showBack: true, doneLabel: "Quit"));
        return panel;
    }

    private UIElement BuildBatchView()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "BATCH mode",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (_store.BatchPhase == BatchPhase.Setup)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Pick firmware once, then flash each calculator as it connects.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            panel.Children.Add(MakeActionButton("Choose firmware…", _store.PickFirmware));
            if (_store.FirmwarePath is not null)
                panel.Children.Add(new TextBlock { Text = _store.FirmwarePath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Foreground = (Brush)FindResource("MutedBrush") });

            var backupPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var skip = new RadioButton { Content = "Skip backups", IsChecked = _store.BatchBackupChoice == BatchBackupChoice.Skip, Margin = new Thickness(0, 0, 16, 0) };
            var auto = new RadioButton { Content = "Auto-save backups", IsChecked = _store.BatchBackupChoice == BatchBackupChoice.AutoSave };
            skip.Checked += (_, _) => _store.BatchBackupChoice = BatchBackupChoice.Skip;
            auto.Checked += (_, _) => _store.BatchBackupChoice = BatchBackupChoice.AutoSave;
            backupPanel.Children.Add(skip);
            backupPanel.Children.Add(auto);
            panel.Children.Add(backupPanel);

            if (_store.BatchBackupChoice == BatchBackupChoice.AutoSave)
                panel.Children.Add(MakeActionButton("Choose backup folder…", _store.PickBatchBackupFolder));

            panel.Children.Add(MakeActionButton("Start batch", () =>
            {
                if (!_store.CanStartBatch)
                {
                    MessageBox.Show("Choose firmware first.", "BATCH", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _store.StartBatchRun();
            }, primary: true));
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Unit {_store.BatchUnitNumber} · {_store.BatchPhase}",
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 8),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Connect a calculator in programming mode. The app flashes automatically when connected.",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        panel.Children.Add(MakeFooterButtons(showBack: true, doneLabel: "Stop batch", onDone: _store.StopBatch));
        return panel;
    }

    private UIElement BuildWizardView()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var panel = new StackPanel();
        var step = _store.Wizard.Step;

        panel.Children.Add(new TextBlock
        {
            Text = $"Step {step.Number()} of {Enum.GetValues<WizardStep>().Length}: {step.Title()}",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        switch (step)
        {
            case WizardStep.Cable:
                panel.Children.Add(WizardSvgDiagram("step1-cable-diagram-pc.svg", maxHeight: 220));
                break;
            case WizardStep.ProgrammingMode:
                panel.Children.Add(WizardDiagram("wizard-programming-mode.png"));
                break;
            case WizardStep.Finish:
                panel.Children.Add(WizardDiagram("wizard-finish.png"));
                break;
            case WizardStep.Checksum:
                panel.Children.Add(WizardDiagram("wizard-checksum.png", maxHeight: 200));
                break;
        }

        foreach (var line in StepBodyLines(step))
            panel.Children.Add(BodyText(line));

        if (step == WizardStep.Cable)
            panel.Children.Add(WarningBox("Use the POGO cable only on the HP 15C Collector’s Edition. This app will not flash other calculators. Do not use the cable on an HP 15C Limited Edition, a pre-2015 12C, an HP 20b, or an HP 30b, as it could permanently damage your calculator."));

        if (_store.SelectedMode == AppMode.Demo && step == WizardStep.Cable)
            panel.Children.Add(MutedText("DEMO uses a simulated calculator. You do not need a cable. Follow the same steps so FLASH is familiar later."));

        if (_store.SelectedMode == AppMode.Demo && step == WizardStep.ProgrammingMode)
            panel.Children.Add(MutedText("DEMO connects the simulated calculator automatically. Continue when it appears below."));

        if (step == WizardStep.Checksum && _store.FirmwareAssessment is not null)
        {
            var expected = VoyagerFirmwareChecksum.TestMenuDisplay(_store.FirmwareAssessment.Displayed);
            var shortForm = VoyagerFirmwareChecksum.Formatted(_store.FirmwareAssessment.Displayed);
            panel.Children.Add(BodyText($"You should see {expected}."));
            panel.Children.Add(BodyText($"If you received the expected checksum of {shortForm}, the firmware update succeeded."));
        }

        switch (step)
        {
            case WizardStep.Backup:
                panel.Children.Add(MakeActionButton(
                    "Save backup…",
                    async () => await _store.SaveBackupAsync(),
                    primary: !_store.Wizard.BackupResolved || _store.BackupPath is null,
                    enabled: !_store.Wizard.IsBusy));
                panel.Children.Add(MakeActionButton("Skip backup", _store.SkipBackup, enabled: !_store.Wizard.IsBusy));
                if (_store.BackupPath is not null)
                    panel.Children.Add(new TextBlock
                    {
                        Text = _store.BackupPath,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0),
                        Foreground = (Brush)FindResource("MutedBrush"),
                    });
                if (_store.BackupAssessment is not null)
                    panel.Children.Add(new TextBlock { Text = _store.BackupAssessment.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                break;
            case WizardStep.Firmware:
                panel.Children.Add(MakeActionButton("Choose firmware…", _store.PickFirmware));
                if (_store.FirmwareAssessment is not null)
                    panel.Children.Add(new TextBlock { Text = _store.FirmwareAssessment.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                break;
            case WizardStep.Flash:
                var flashLabel = _store.SelectedMode == AppMode.Demo
                    ? "Flash simulated calculator"
                    : "Flash calculator";
                if (_store.Wizard.FlashSucceeded)
                    panel.Children.Add(new TextBlock
                    {
                        Text = "Flashed and verified.",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.DarkGreen,
                        Margin = new Thickness(0, 0, 0, 8),
                    });
                if (!_store.Wizard.IsBusy)
                    panel.Children.Add(MakeActionButton(
                        flashLabel,
                        async () =>
                        {
                            if (!ConfirmFlash())
                                return;
                            await _store.RunFlashAsync();
                        },
                        primary: !_store.Wizard.FlashSucceeded,
                        enabled: _store.FirmwarePath is not null));
                if (_store.PagePreviewHeader is not null)
                {
                    panel.Children.Add(new TextBlock { Text = _store.PagePreviewHeader, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush"), Margin = new Thickness(0, 12, 0, 4) });
                    foreach (var line in _store.PagePreviewLines.Take(8))
                        panel.Children.Add(new TextBlock { Text = line, FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = (Brush)FindResource("InkBrush") });
                }
                break;
            case WizardStep.ProgrammingMode:
                if (_store.Wizard.IdentitySupported)
                    panel.Children.Add(new TextBlock { Text = "Calculator detected in programming mode.", Foreground = Brushes.DarkGreen });
                break;
        }

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 0);
        grid.Children.Add(scroll);

        var buttons = BuildWizardButtons(step);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private UIElement BuildWizardButtons(WizardStep step)
    {
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        nav.Children.Add(MakeActionButton("← Welcome", _store.ReturnToWelcome));

        if (step == WizardStep.Checksum)
        {
            nav.Children.Add(MakeActionButton("Done", () => Application.Current.Shutdown(), primary: true));
            return nav;
        }

        if (_store.Wizard.CanGoBack)
            nav.Children.Add(MakeActionButton("Back", _store.WizardBack));
        if (_store.Wizard.CanAdvance)
            nav.Children.Add(MakeActionButton("Next", _store.WizardAdvance, primary: true));
        return nav;
    }

    private bool ShowStatusBar()
    {
        if (_store.ShowWelcome)
            return false;
        if (_store.SelectedMode is AppMode.Probe or AppMode.Batch)
            return true;
        return _store.Wizard.Step != WizardStep.Cable;
    }

    private bool ConfirmFlash()
    {
        var demo = _store.SelectedMode == AppMode.Demo;
        var title = demo ? "Flash the simulated calculator?" : "Flash the calculator?";
        var message = demo
            ? "DEMO writes only the simulated calculator on this PC. A real HP 15C is not changed.\n\nWrite starts at address 0x04000."
            : "FLASH writes a real calculator. User memory will be wiped. The bootloader at 0x0000–0x3FFF is not overwritten.\n\nWrite starts at address 0x04000.";

        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    private static string[] StepBodyLines(WizardStep step) => step switch
    {
        WizardStep.Cable =>
        [
            "Open the calculator's battery door and insert the POGO cable. The connector is keyed; the POGO can only be inserted one way. Make sure the plug snaps securely into place.",
            "Plug the other end of the cable (USB-A or USB-C) into this PC.",
        ],
        WizardStep.ProgrammingMode =>
        [
            "On the cable's switch box, hold ERASE, press RESET, then release ERASE. The display stays off. The calculator's ON button is ignored in this state.",
            "Once the calculator is recognized (“Connected: ATSAM4LC2C” is shown), continue with the next step.",
        ],
        WizardStep.Backup =>
        [
            "Save a copy of the currently installed firmware in case you want to restore it later. Choosing a location reads the calculator immediately.",
        ],
        WizardStep.Firmware =>
        [
            "Choose a 114,688 (0x1C000) byte file with .bin extension. This app does not download HP firmware.",
        ],
        WizardStep.Flash =>
        [
            "Write starts at address 0x04000. The SAM-BA bootloader below that address is left intact.",
        ],
        WizardStep.Finish =>
        [
            "Press RESET on the cable switch-box, then turn the calculator ON. “Pr Error” in the display is expected. Press any key to see 0.0000.",
        ],
        WizardStep.Checksum =>
        [
            "Turn the calculator OFF (press ON). Hold g and ENTER, then press ON. Release ON, then release g and ENTER.",
            "The display shows the test menu: “1.L 2.C 3.H”. Press 2.",
        ],
        _ => [],
    };

    private static ScrollViewer Scroll(UIElement child) =>
        new()
        {
            Content = child,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

    private static Image WizardDiagram(string fileName, double maxHeight = 160)
    {
        return new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{fileName}")),
            Stretch = Stretch.Uniform,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            SnapsToDevicePixels = true,
        };
    }

    private static Image WizardSvgDiagram(string fileName, double maxHeight)
    {
        var uri = new Uri($"pack://application:,,,/Assets/{fileName}");
        using var stream = Application.GetResourceStream(uri)?.Stream
            ?? throw new InvalidOperationException($"Missing diagram {fileName}");
        using var reader = new FileSvgReader(new WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = true,
        }, isEmbedded: true);
        var drawing = reader.Read(stream)
            ?? throw new InvalidOperationException($"Could not render {fileName}");
        return new Image
        {
            Source = new DrawingImage(drawing),
            Stretch = Stretch.Uniform,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            SnapsToDevicePixels = true,
        };
    }

    private TextBlock BodyText(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("InkBrush"),
            Margin = new Thickness(0, 0, 0, 10),
        };

    private TextBlock MutedText(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 10),
        };

    private UIElement WarningBox(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 173, 51)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 16),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black,
            },
        };
    }

    private UIElement MakeFooterButtons(bool showBack, string? doneLabel, Action? onDone = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        if (showBack)
            row.Children.Add(MakeActionButton("← Welcome", _store.ReturnToWelcome));
        if (doneLabel is not null)
            row.Children.Add(MakeActionButton(doneLabel, onDone ?? (() => Application.Current.Shutdown())));
        return row;
    }

    private Button MakeActionButton(string label, Action onClick, bool primary = false, bool enabled = true)
    {
        var btn = new Button
        {
            Content = label,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = enabled,
            Background = primary ? (Brush)FindResource("AccentBrush") : Brushes.White,
            Foreground = primary ? Brushes.White : (Brush)FindResource("InkBrush"),
            BorderBrush = primary
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            BorderThickness = new Thickness(1),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
