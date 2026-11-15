using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private bool _buildingModels;
    private bool _buildingTweaks;
    private IReadOnlyList<string> _modes = Array.Empty<string>();
    private string _deviceSignature = string.Empty;
    private bool _planChanges;
    private int _planDiffBytes;
    private ControllerProfile? _profile;

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

        // the window starts neutral and follows whichever model is connected or selected
        BuildModelPicker();
        ApplyRegistryText();
        ApplyNeutralText();

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

    // builds one tab per registered profile instead of hardcoding a model in the window
    private void BuildModelPicker()
    {
        _buildingModels = true;
        ModelPicker.Children.Clear();
        foreach (var profile in _service.Profiles)
        {
            var button = new RadioButton
            {
                Content = profile.ShortName,
                GroupName = "ControllerModel",
                Style = (Style)FindResource("TabNav"),
                Tag = profile,
                ToolTip = profile.Name,
                Margin = new Thickness(0, 0, 4, 4),
            };
            button.Checked += Model_Checked;
            ModelPicker.Children.Add(button);
        }
        _buildingModels = false;
    }

    // the header and the About tab describe the registry, not one vendor
    private void ApplyRegistryText()
    {
        var profiles = _service.Profiles;
        var vendors = profiles.Select(p => p.Vendor).Distinct(StringComparer.Ordinal).ToList();
        SubtitleText.Text = $"Custom firmware for {Plan.Listed(vendors)} controllers.";
        SupportedText.Text = "Supported today: " + string.Join("; ",
            profiles.Select(p => $"{p.Name} on firmware {p.SupportedVersions}."));
        AboutText.Text =
            "Pad8Lab flashes custom firmware profiles onto supported controllers. A profile holds the model's " +
            "flash map, firmware catalog, update protocol and tweaks, so a new model is a new profile rather than " +
            "a change to the flashing core. The change lives inside the controller: after one flash it works on " +
            "any Windows PC, with no drivers and no virtual pad.";
        var credits = profiles.Select(p => p.Credits)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        CreditsText.Text = credits.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, credits)
            : "Pad8Lab is free and open source under GPL-3.0-or-later. The full credits and the origin of each profile are listed in the repository.";
        FooterText.Text = "GPL-3.0-OR-LATER  ·  NOT AFFILIATED WITH OR ENDORSED BY THE CONTROLLER VENDORS";
    }

    private void Model_Checked(object sender, RoutedEventArgs e)
    {
        if (_buildingModels || sender is not RadioButton { Tag: ControllerProfile profile, IsChecked: true })
            return;
        UseProfile(profile);
        ApplyDeviceState(_modes);
    }

    // keeps the radio buttons in sync without firing their Checked handler again
    private void SelectModel(ControllerProfile? profile)
    {
        _buildingModels = true;
        foreach (var child in ModelPicker.Children.OfType<RadioButton>())
            child.IsChecked = child.Tag is ControllerProfile candidate && ReferenceEquals(candidate, profile);
        _buildingModels = false;
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
            UseProfile(ConnectedProfile(found) ?? _profile);
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
            var selected = ConnectedProfile(found) ?? _profile;
            var sameProfile = ReferenceEquals(selected, _profile);
            UseProfile(selected);
            if (SameModes(modes, _modes) && sameProfile)
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

    // picks the connected model only when exactly one is present, so two pads don't steal the selection
    private static ControllerProfile? ConnectedProfile(IEnumerable<Found> found)
    {
        var profiles = found.Select(f => f.Profile).Distinct().ToList();
        return profiles.Count == 1 ? profiles[0] : null;
    }

    // follows the model that is connected or selected; null means no model chosen yet
    private void UseProfile(ControllerProfile? profile)
    {
        if (ReferenceEquals(profile, _profile))
            return;
        _profile = profile;

        // each model's disclaimer is accepted on its own, so a model change asks again
        DisclaimerCheck.IsChecked = false;
        SelectModel(profile);
        if (profile is null)
            ApplyNeutralText();
        else
            ApplyProfileText(profile);
        ApplyButtonState();
    }

    // every model-specific sentence in the flow comes from the profile
    private void ApplyProfileText(ControllerProfile profile)
    {
        Title = $"Pad8Lab - {profile.Name}";
        ConnectTitleText.Text = $"Connect the controller by {profile.CableName}";
        ConnectHelpText.Text = profile.ConnectHelp;
        ReconnectHelpText.Text = profile.ReconnectHelp;
        RestoreHelpText.Text =
            $"Restore puts back the backup that still holds the original firmware {profile.Primary.Version:0.00}.";
        DisclaimerText.Text =
            $"I understand this modifies {profile.Vendor}'s firmware and probably voids the warranty, and that my backup can undo it.";
        RecoveryText.Text = "The bootloader is never written, so recovery is always possible over USB. " +
                            profile.BootHelp + " Restore works from there.";
        ModelHelpText.Text = $"{profile.Name} on firmware {profile.SupportedVersions}. Tested on {profile.SupportedOs}.";
        BuildTweakList(profile);
    }

    // neutral copy for the first start and for when nothing is connected
    private void ApplyNeutralText()
    {
        Title = "Pad8Lab - controller firmware toolkit";
        ConnectTitleText.Text = "Connect the controller by cable";
        ConnectHelpText.Text = "Choose a supported model or connect a controller. Flashing only works over the cable.";
        ReconnectHelpText.Text = "Unplug the cable and connect the controller the way you normally use it.";
        RestoreHelpText.Text = "Restore puts back a backup from this unit.";
        DisclaimerText.Text =
            "I understand this modifies the controller's firmware and probably voids the warranty, and that my backup can undo it.";
        RecoveryText.Text = "The bootloader is never written, so recovery is always possible over USB. " +
                            "Put the controller in update mode and use Restore.";
        ModelHelpText.Text = "No controller detected. Connect one by cable or choose a supported model.";
        InstallHelpText.Text = "Choose a supported model to see its tweaks.";
        TweakList.Children.Clear();
        TweakList.Visibility = Visibility.Collapsed;
    }

    // builds a checkbox per offered tweak, with the profile's own title and description
    private void BuildTweakList(ControllerProfile profile)
    {
        _buildingTweaks = true;
        TweakList.Children.Clear();
        var target = profile.Primary;
        var catalog = target.Catalog;
        foreach (var key in profile.SuggestedFor(target))
        {
            if (!catalog.All.TryGetValue(key, out var recipe))
                continue;

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = recipe.Title,
                Style = (Style)FindResource("Body"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
            });
            if (!string.IsNullOrWhiteSpace(recipe.Description))
            {
                content.Children.Add(new TextBlock
                {
                    Text = recipe.Description,
                    Style = (Style)FindResource("Muted"),
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            var check = new CheckBox
            {
                Style = (Style)FindResource("DisclaimerCheck"),
                Content = content,
                Tag = key,
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 7),
            };
            AutomationProperties.SetName(check, recipe.Title);
            if (!string.IsNullOrWhiteSpace(recipe.Description))
                AutomationProperties.SetHelpText(check, recipe.Description);
            check.Checked += Tweak_Changed;
            check.Unchecked += Tweak_Changed;
            TweakList.Children.Add(check);
        }

        var implicitTitles = profile.ImplicitFor(target)
            .Where(catalog.All.ContainsKey)
            .Select(key => catalog.All[key].Title)
            .ToList();
        if (implicitTitles.Count > 0)
        {
            TweakList.Children.Add(new TextBlock
            {
                Text = "Always included: " + Plan.Listed(implicitTitles) + ".",
                Style = (Style)FindResource("Muted"),
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        TweakList.Visibility = TweakList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _buildingTweaks = false;
        UpdateTweakHelp(profile);
    }

    private IReadOnlyList<string> SelectedTweakKeys() =>
        TweakList.Children.OfType<CheckBox>()
            .Where(check => check.IsChecked == true && check.Tag is string)
            .Select(check => (string)check.Tag!)
            .ToList();

    private void Tweak_Changed(object sender, RoutedEventArgs e)
    {
        if (_buildingTweaks || _profile is null)
            return;
        UpdateTweakHelp(_profile);
        ApplyButtonState();
    }

    // the install card names the checked tweaks and refuses an empty selection
    private void UpdateTweakHelp(ControllerProfile profile)
    {
        var catalog = profile.Primary.Catalog;
        var titles = SelectedTweakKeys()
            .Where(catalog.All.ContainsKey)
            .Select(key => catalog.All[key].Title)
            .ToList();
        InstallHelpText.Text = titles.Count == 0
            ? "Choose at least one tweak to install."
            : $"Adds {Plan.Listed(titles)}. Writes only those regions, then verifies them.";
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
            ConnectionText.Text = _profile?.CableName ?? "Cable";
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

        var count = _profile is null ? 0 : _service.BackupCount(_profile);
        BackupText.Text = _profile is null
            ? "No model selected"
            : count switch
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
        var hasProfile = _profile is not null;
        var byCable = _modes.Any(m => ControllerProfile.IsCableMode(m) || m == DeviceMode.Boot);
        var accepted = DisclaimerCheck.IsChecked == true;

        BackupButton.IsEnabled = ready && hasProfile && byCable;
        CheckButton.IsEnabled = ready && hasProfile && byCable;

        InstallButton.IsEnabled = ready && hasProfile && byCable && accepted && SelectedTweakKeys().Count > 0;
        RestoreButton.IsEnabled = ready && hasProfile && byCable && accepted;
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
        var version = _profile?.Primary.Version ?? 0;
        _planChanges = !installed;
        _planDiffBytes = plan.DiffBytes;
        Dispatcher.BeginInvoke(
            () => SetFirmware(installed ? "Tweaks installed" : $"Original {version:0.00}",
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
        var profile = _profile;
        if (profile is null)
            return;

        if (!ConfirmWindow.Ask(this, "Back up the controller?",
                "This reads the controller's whole flash over the cable and saves it in your app data. " +
                "It writes nothing. The backup is what Restore uses later, so keep it.",
                "Back up"))
            return;

        if (await RunAsync("backup", () => _service.Backup(Log, ShowProgress, profile.Id)))
            ShowResult(true, "Backup saved. It holds your unit's bootloader check, calibration and pairing data.");
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var profile = _profile;
        if (profile is null)
            return;
        var keys = SelectedTweakKeys();
        if (keys.Count == 0)
        {
            ShowResult(false, "Choose at least one tweak to check.");
            return;
        }

        _planChanges = false;
        var ok = await RunAsync("check", () => _service.Install(
            keys, write: false, disclaimerAccepted: false, Log, ShowProgress, ReportPlan, profile.Id));
        if (!ok)
            return;

        ShowResult(true, _planChanges
            ? $"The tweaks are not installed yet. Installing them writes {Plan.Count(_planDiffBytes, "byte")} of the firmware."
            : "The tweaks are already installed. There is nothing to write.");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var profile = _profile;
        if (profile is null)
            return;
        var keys = SelectedTweakKeys();
        if (keys.Count == 0)
        {
            ShowResult(false, "Choose at least one tweak to install.");
            return;
        }

        if (!ConfirmWindow.Ask(this, "Install the tweaks?",
                "This writes only the firmware regions the tweaks change, reads them back to verify and restarts the controller. " +
                $"Make sure the {profile.CableName} is connected and that you have a backup.",
                "Install"))
            return;

        var accepted = DisclaimerCheck.IsChecked == true;
        var ok = await RunAsync("install", () => _service.Install(
            keys, write: true, disclaimerAccepted: accepted, Log, ShowProgress, ReportPlan, profile.Id));
        if (!ok)
            return;

        SetFirmware("Tweaks installed", StatusKind.Ok);
        ShowResult(true, _planChanges
            ? "Installed and verified. " + profile.ReconnectHelp
            : "The tweaks were already installed, so nothing was written.");
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var profile = _profile;
        if (profile is null)
            return;

        if (!ConfirmWindow.Ask(this, "Restore the original firmware?",
                $"This writes the original firmware {profile.Primary.Version:0.00} from your backup back to the controller, verifies it and undoes the tweaks.",
                "Restore"))
            return;

        var accepted = DisclaimerCheck.IsChecked == true;
        var ok = await RunAsync("restore", () => _service.Restore(
            "backup", write: true, forceUnknown: false,
            disclaimerAccepted: accepted, Log, ShowProgress, ReportPlan, profile.Id));
        if (!ok)
            return;

        SetFirmware($"Original {profile.Primary.Version:0.00}", StatusKind.Muted);
        ShowResult(true, _planChanges
            ? "Original firmware restored and verified. " + profile.ReconnectHelp
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
