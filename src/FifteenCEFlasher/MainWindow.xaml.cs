using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FifteenCEFlasherCore;

namespace FifteenCEFlasher;

public partial class MainWindow : Window
{
    private readonly FlasherStore _store = new();

    public MainWindow()
    {
        InitializeComponent();
        _store.PropertyChanged += (_, _) => Dispatcher.Invoke(UpdateUi);
        _store.Start();
        UpdateUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        _store.Dispose();
        base.OnClosed(e);
    }

    private void UpdateUi()
    {
        StatusText.Text = _store.StatusMessage;
        DetailText.Text = _store.DetailMessage;
        ProgressBar.Value = _store.Progress;
        ProgressBar.Visibility = _store.Progress > 0 ? Visibility.Visible : Visibility.Collapsed;

        MainContent.Content = _store.ShowWelcome ? BuildWelcome() : BuildActiveView();
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
        var panel = new StackPanel();
        var step = _store.Wizard.Step;

        panel.Children.Add(new TextBlock
        {
            Text = $"Step {step.Number()} of {Enum.GetValues<WizardStep>().Length}: {step.Title()}",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(new TextBlock
        {
            Text = StepBody(step),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        switch (step)
        {
            case WizardStep.Backup:
                panel.Children.Add(MakeActionButton("Save backup…", _store.PickBackupDestination));
                panel.Children.Add(MakeActionButton("Skip backup", _store.SkipBackup));
                if (_store.BackupAssessment is not null)
                    panel.Children.Add(new TextBlock { Text = _store.BackupAssessment.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                break;
            case WizardStep.Firmware:
                panel.Children.Add(MakeActionButton("Choose firmware…", _store.PickFirmware));
                if (_store.FirmwareAssessment is not null)
                    panel.Children.Add(new TextBlock { Text = _store.FirmwareAssessment.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                break;
            case WizardStep.Flash:
                panel.Children.Add(MakeActionButton("Flash firmware", async () => await _store.RunFlashAsync(), primary: true, enabled: !_store.Wizard.IsBusy && _store.FirmwarePath is not null));
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

        if (step == WizardStep.Backup && _store.BackupPath is not null && !_store.Wizard.IsBusy)
            panel.Children.Add(MakeActionButton("Run backup now", async () => await _store.RunBackupAsync(), primary: true));

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        if (_store.Wizard.CanGoBack)
            nav.Children.Add(MakeActionButton("Back", _store.WizardBack));
        if (_store.Wizard.CanAdvance && step != WizardStep.Flash)
            nav.Children.Add(MakeActionButton("Next", _store.WizardAdvance, primary: true));
        if (step == WizardStep.Finish)
            nav.Children.Add(MakeActionButton("Done", () => Application.Current.Shutdown(), primary: true));
        panel.Children.Add(nav);

        panel.Children.Add(MakeFooterButtons(showBack: true, doneLabel: null));
        return panel;
    }

    private static string StepBody(WizardStep step) => step switch
    {
        WizardStep.Cable => "Connect the official HP programming cable to this PC.",
        WizardStep.ProgrammingMode => "Hold ERASE on the calculator, press RESET, then release ERASE. Wait for Connected status below.",
        WizardStep.Backup => "Save a backup of the current firmware before flashing.",
        WizardStep.Firmware => "Choose the .bin firmware file to install (112 KB).",
        WizardStep.Flash => "Write firmware to the calculator. Do not disconnect the cable.",
        WizardStep.Finish => "Press RESET on the calculator to leave programming mode and restart.",
        WizardStep.Checksum => "On the calculator: g ENTER ON, then 2 to view the firmware checksum.",
        _ => "",
    };

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
            IsEnabled = enabled,
            Background = primary ? (Brush)FindResource("AccentBrush") : Brushes.White,
            Foreground = primary ? Brushes.White : (Brush)FindResource("InkBrush"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            BorderThickness = new Thickness(1),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
