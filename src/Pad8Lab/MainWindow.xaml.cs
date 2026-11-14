using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Pad8Lab.Core;
using Pad8Lab.Core.Ops;
using Pad8Lab.Core.Profiles;

namespace Pad8Lab;

/// <summary>Main window: controller state, the guided flashing steps and a live log of the .NET core.</summary>
public partial class MainWindow : Window
{
    private const string RepoUrl = "https://github.com/JehuAlv/Pad8Lab";
    private const string ReleaseUrl = RepoUrl + "/releases/latest";
    private const string CreditsUrl = RepoUrl + "/blob/main/CREDITS.md";

    private readonly FlashService _service = new();
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _busy;
    private bool _probing;
    private bool _cableShown;
    private IReadOnlyList<string> _modes = Array.Empty<string>();
    private string _deviceSignature = string.Empty;
    private bool _planChanges;
    private int _planDiffBytes;
    private ControllerProfile _profile = ProfileRegistry.Default.Profiles[0];

    private enum StatusKind
    {
        Ok,
        Warn,
        Info,
        Muted,
        Busy,
    }

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        // the header and the About tab list whatever the registry supports today
        var profiles = ProfileRegistry.Default.Profiles;
        SubtitleText.Text = $"Custom firmware for 8BitDo controllers. {Plan.Listed(profiles.Select(p => p.ShortName))} supported today.";
        SupportedText.Text = "Supported today: " + string.Join("; ",
            profiles.Select(p => $"{p.Name} on firmware {p.SupportedVersions}."));
        ApplyProfileText(_profile);

        // keeps the window inside the screen on small displays or high scaling
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width - 32);
        Height = Math.Min(Height, work.Height - 8);

        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            LogHint.Text = "every step is listed here";
        };

        Log($"Store: {_service.Store}");

        // keeps the cable and Bluetooth state live while the window is open
        _watchTimer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) =>
        {
            await RefreshDevicesAsync();
            _watchTimer.Start();
        };
        Closed += (_, _) => _watchTimer.Stop();
    }

    // makes the title bar match the dark window on Windows 11
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));       // immersive dark mode
        var caption = 0x00120D0B;                                   // COLORREF of #0B0D12
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));  // caption colour
        var text = 0x00F6EDE9;                                      // COLORREF of #E9EDF6
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));     // caption text colour
    }

    // lists the devices and turns their modes into the state the window shows
    private async Task RefreshDevicesAsync()
    {
        SetBusy(true);
        SetStatus("Looking for the controller...", StatusKind.Busy);
        try
        {
            var (found, lines) = await Task.Run(() => (_service.Find(), _service.DescribeDevices()));

            // only logs the device list when it changes, so repeated refreshes stay quiet
            var signature = string.Join("\n", lines);
            if (signature != _deviceSignature)
            {
                _deviceSignature = signature;
                foreach (var line in lines)
                    Log(line);
            }

            _modes = found.Select(f => f.Mode).ToList();
            UseProfile(found.Select(f => f.Profile).FirstOrDefault());
            ApplyDeviceState(_modes);
        }
        catch (Exception ex)
        {
            SetStatus("Could not look for the controller.", StatusKind.Warn);
            Log(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // polls for a plugged or unplugged controller and redraws only when the state changes
    private async Task PollAsync()
    {
        if (_busy || _probing)
            return;

        _probing = true;
        try
        {
            var found = await Task.Run(() => _service.Find());
            var modes = found.Select(f => f.Mode).ToList();
            UseProfile(found.Select(f => f.Profile).FirstOrDefault());
            if (SameModes(modes, _modes))
                return;

            var lines = await Task.Run(() => _service.DescribeDevices());
            var signature = string.Join("\n", lines);
            if (signature != _deviceSignature)
            {
                _deviceSignature = signature;
                foreach (var line in lines)
                    Log(line);
            }

            _modes = modes;
            ApplyDeviceState(modes);
        }
        catch
        {
            // a poll that fails must not interrupt whatever the window is doing
        }
        finally
        {
            _probing = false;
        }
    }

    // the order devices come back in is not stable, so compare them sorted
    private static bool SameModes(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.OrderBy(m => m).SequenceEqual(b.OrderBy(m => m));

    // follows the model that is connected, or the first registered one when nothing is
    private void UseProfile(ControllerProfile? profile)
    {
        profile ??= ProfileRegistry.Default.Profiles[0];
        if (ReferenceEquals(profile, _profile))
            return;
        _profile = profile;
        ApplyProfileText(profile);
    }

    // the flow names the model's own tweaks and firmware version
    private void ApplyProfileText(ControllerProfile profile)
    {
        var titles = profile.SuggestedTweaks
            .Where(key => profile.Primary.Catalog.All.ContainsKey(key))
            .Select(key => profile.Primary.Catalog.All[key].Title)
            .ToList();
        InstallHelpText.Text = titles.Count == 0
            ? "Writes only the firmware regions the tweaks change, then reads them back to verify."
            : $"Adds {Plan.Listed(titles)}. Writes only those regions, then verifies them.";
        RestoreHelpText.Text =
            $"Restore puts back the backup that still holds the original firmware {profile.Primary.Version:0.00}.";
    }

    // maps the device modes onto the status pill, the connection row and the buttons
    private void ApplyDeviceState(IReadOnlyList<string> modes)
    {
        var boot = modes.Contains(DeviceMode.Boot);
        var cable = modes.Any(ControllerProfile.IsCableMode);
        var bluetooth = modes.Any(m => m.StartsWith("bluetooth", StringComparison.Ordinal));
        var dongle = modes.Any(ControllerProfile.IsDongleMode);

        if (boot)
        {
            SetStatus("Update mode - ready to flash", StatusKind.Warn);
            ConnectionChipText.Text = "Update mode";
            ConnectionChipText.Foreground = Brush("WarnBrush");
            ConnectionChip.Background = Solid("#2A2113");
            ConnectionText.Text = "USB";
        }
        else if (cable)
        {
            SetStatus("Connected by cable - ready", StatusKind.Ok);
            ConnectionChipText.Text = "Connected";
            ConnectionChipText.Foreground = Brush("SuccessBrush");
            ConnectionChip.Background = Solid("#16261F");
            ConnectionText.Text = "USB-C cable";
        }
        else if (bluetooth)
        {
            SetStatus("Wireless - connect the cable to flash", StatusKind.Info);
            ConnectionChipText.Text = "Wireless";
            ConnectionChipText.Foreground = Brush("Accent2Brush");
            ConnectionChip.Background = Solid("#11222B");
            ConnectionText.Text = "Wireless";
        }
        else if (dongle)
        {
            SetStatus("Only the wireless adapter is connected", StatusKind.Warn);
            ConnectionChipText.Text = "Adapter only";
            ConnectionChipText.Foreground = Brush("WarnBrush");
            ConnectionChip.Background = Solid("#2A2113");
            ConnectionText.Text = "Wireless adapter";
        }
        else
        {
            SetStatus("No controller found", StatusKind.Muted);
            ConnectionChipText.Text = "Not found";
            ConnectionChipText.Foreground = Brush("MutedBrush");
            ConnectionChip.Background = Solid("#1A2130");
            ConnectionText.Text = "Not connected";
        }

        var count = BackupCount();
        BackupText.Text = count switch
        {
            0 => "None yet",
            1 => "1 backup",
            _ => $"{count} backups",
        };
        BackupText.Foreground = count > 0 ? Brush("SuccessBrush") : Brush("MutedBrush");

        SetCableVisual(boot || cable);
        ApplyButtonState();
    }

    // fades the drawn cable in over USB, and out when the controller is unplugged
    private void SetCableVisual(bool shown)
    {
        if (shown == _cableShown)
            return;

        _cableShown = shown;
        var pulse = (Storyboard)CableLayer.Resources["CablePulse"];

        if (shown)
        {
            CableLayer.Visibility = Visibility.Visible;
            CableLayer.Opacity = 1;
            CableLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            {
                FillBehavior = FillBehavior.Stop,
            });
            pulse.Begin(this, true);
            return;
        }

        pulse.Stop(this);
        CableLayer.Opacity = 0;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        {
            FillBehavior = FillBehavior.Stop,
        };
        fade.Completed += (_, _) =>
        {
            if (!_cableShown)
                CableLayer.Visibility = Visibility.Collapsed;
        };
        CableLayer.BeginAnimation(OpacityProperty, fade);
    }

    // the steps that need the controller by cable stay disabled until it is there
    private void ApplyButtonState()
    {
        var ready = !_busy;
        var byCable = _modes.Any(m => ControllerProfile.IsCableMode(m) || m == DeviceMode.Boot);

        BackupButton.IsEnabled = ready && byCable;
        CheckButton.IsEnabled = ready && byCable;

        var canWrite = ready && byCable && DisclaimerCheck.IsChecked == true;
        InstallButton.IsEnabled = canWrite;
        RestoreButton.IsEnabled = canWrite;
    }

    // runs one core operation on a background thread and streams its messages into the log
    private async Task<bool> RunAsync(string what, Action action)
    {
        SetBusy(true);
        ResultBanner.Visibility = Visibility.Collapsed;
        SetStatus("Working...", StatusKind.Busy);
        Log(string.Empty);
        Log($"> {what}");

        var ok = true;
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            ok = false;
            Log(ex.ToString());
            ShowResult(false, ex.Message);
        }
        finally
        {
            SetBusy(false);
            await RefreshDevicesAsync();
        }

        return ok;
    }

    // shows the core's progress in the strip above the log
    private void ShowProgress(string label, int done, int total)
    {
        var percent = total <= 0 ? 0 : Math.Min(100, done * 100 / total);
        var text = label switch
        {
            "pass 1" => "Reading the firmware - pass 1 of 2",
            "pass 2" => "Reading the firmware - pass 2 of 2",
            "write" => "Writing and verifying the firmware",
            _ => label,
        };

        Dispatcher.BeginInvoke(() =>
        {
            ProgressLabel.Text = text;
            ProgressPercent.Text = percent + " %";
            ProgressBar.Value = percent;
        });
    }

    // picks the firmware state from a plan: no changes means the tweaks are already there
    private void ReportPlan(Plan plan)
    {
        var installed = !plan.BankA && plan.OtherSectors.Count == 0;
        _planChanges = !installed;
        _planDiffBytes = plan.DiffBytes;
        Dispatcher.BeginInvoke(
            () => SetFirmware(installed ? "Tweaked" : $"Original {_profile.Primary.Version:0.00}",
                installed ? StatusKind.Ok : StatusKind.Muted));
    }

    private void SetFirmware(string text, StatusKind kind)
    {
        FirmwareText.Text = text;
        FirmwareDot.Fill = BrushFor(kind);
    }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusText.Foreground = BrushFor(kind);
        StatusDot.Fill = BrushFor(kind);
        StatusPill.Background = Solid(kind switch
        {
            StatusKind.Ok => "#16261F",
            StatusKind.Warn => "#2A2113",
            StatusKind.Info or StatusKind.Busy => "#11222B",
            _ => "#1A2130",
        });
    }

    private void ShowResult(bool ok, string message)
    {
        ResultBanner.Visibility = Visibility.Visible;
        ResultBanner.Background = Solid(ok ? "#14261E" : "#2A1618");
        ResultIcon.Data = (Geometry)FindResource(ok ? "IconCheck" : "IconWarning");
        ResultIcon.Fill = BrushFor(ok ? StatusKind.Ok : StatusKind.Warn);
        ResultText.Foreground = BrushFor(ok ? StatusKind.Ok : StatusKind.Warn);
        ResultText.Text = message;
        ResultBanner.BringIntoView();
    }

    // disables the buttons while the core works, and shows the progress strip
    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            ProgressBar.Value = 0;
            ProgressPercent.Text = string.Empty;
        }

        LogHint.Text = busy ? "working..." : "every step is listed here";
        ApplyButtonState();
    }

    private int BackupCount()
    {
        try
        {
            return Directory.Exists(_service.Store) ? Directory.GetFiles(_service.Store, "controller-*.bin").Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private Brush BrushFor(StatusKind kind) => Brush(kind switch
    {
        StatusKind.Ok => "SuccessBrush",
        StatusKind.Warn => "WarnBrush",
        StatusKind.Info or StatusKind.Busy => "Accent2Brush",
        _ => "MutedBrush",
    });

    private Brush Brush(string key) => (Brush)FindResource(key);

    private static SolidColorBrush Solid(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    // starts a process and says so in the log if it can't
    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (SetupView is null || AboutView is null)
            return;
        var about = NavAbout.IsChecked == true;
        SetupView.Visibility = about ? Visibility.Collapsed : Visibility.Visible;
        AboutView.Visibility = about ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Back up the controller?",
                "This reads the controller's whole flash over the cable and saves it in your app data. " +
                "It writes nothing. The backup is what Restore uses later, so keep it.",
                "Back up"))
            return;

        if (await RunAsync("backup", () => _service.Backup(Log, ShowProgress)))
            ShowResult(true, "Backup saved. It holds your unit's bootloader check, calibration and pairing data.");
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        _planChanges = false;
        var ok = await RunAsync("check", () => _service.Install(
            _service.SuggestedTweaks(), write: false, disclaimerAccepted: false, Log, ShowProgress, ReportPlan));
        if (!ok)
            return;

        ShowResult(true, _planChanges
            ? $"The tweaks are not installed yet. Installing them writes {Plan.Count(_planDiffBytes, "byte")} of the firmware."
            : "The tweaks are already installed. There is nothing to write.");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Install the tweaks?",
                "This writes only the firmware regions the tweaks change, reads them back to verify and restarts the controller. " +
                "Make sure the USB-C cable is connected and that you have a backup.",
                "Install"))
            return;

        var accepted = DisclaimerCheck.IsChecked == true;
        var ok = await RunAsync("install", () => _service.Install(
            _service.SuggestedTweaks(), write: true,
            disclaimerAccepted: accepted, Log, ShowProgress, ReportPlan));
        if (!ok)
            return;

        SetFirmware("Tweaked", StatusKind.Ok);
        ShowResult(true, _planChanges
            ? "Installed and verified. Unplug the cable and reconnect the controller; if Windows shows it as a new device, remove it in Settings and pair it again."
            : "The tweaks were already installed, so nothing was written.");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, "Restore the original firmware?",
                $"This writes the original firmware {_profile.Primary.Version:0.00} from your backup back to the controller, verifies it and undoes the tweaks.",
                "Restore"))
            return;

        var accepted = DisclaimerCheck.IsChecked == true;
        var ok = await RunAsync("restore", () => _service.Restore(
            "backup", write: true, forceUnknown: false,
            disclaimerAccepted: accepted, Log, ShowProgress, ReportPlan));
        if (!ok)
            return;

        SetFirmware($"Original {_profile.Primary.Version:0.00}", StatusKind.Muted);
        ShowResult(true, _planChanges
            ? "Original firmware restored and verified. Reconnect the controller; if Windows still shows its old identity, remove it in Settings and pair it again."
            : "The controller already has the original firmware, so nothing was written.");
    }

    private void Disclaimer_Changed(object sender, RoutedEventArgs e)
    {
        var accepted = DisclaimerCheck.IsChecked == true;
        InstallCard.BorderBrush = accepted ? Brush("SuccessBrush") : Solid("#3B2F7E");
        InstallButton.ToolTip = accepted ? null : "Accept the notice below to enable this";
        ApplyButtonState();
    }

    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = LogBox.Height < 150;
        LogBox.Height = expanded ? 210 : 84;
        LogChevronRotation.Angle = expanded ? 0 : 180;
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogBox.Text);
            LogHint.Text = "copied to the clipboard";
            _hintTimer.Stop();
            _hintTimer.Start();
        }
        catch (Exception ex)
        {
            Log("Could not copy the log: " + ex.Message);
        }
    }

    private void OpenStore_Click(object sender, RoutedEventArgs e) => Open(_service.Store);

    private void OpenRelease_Click(object sender, RoutedEventArgs e) => Open(ReleaseUrl);

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => Open(RepoUrl);

    private void OpenCredits_Click(object sender, RoutedEventArgs e) => Open(CreditsUrl);

    // the core runs on a background thread, so marshal every line onto the UI thread
    private void Log(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(line));
            return;
        }
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
