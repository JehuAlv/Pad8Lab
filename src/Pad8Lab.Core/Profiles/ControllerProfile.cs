using System.Globalization;
using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Profiles;

/// <summary>Everything that changes from one controller model to another: identity, layout, firmware and protocol.</summary>
public sealed class ControllerProfile
{
    public ControllerProfile(
        string id,
        string name,
        string shortName,
        string vendor,
        string disclaimer,
        string bootHelp,
        FlashLayout layout,
        IReadOnlyList<DeviceMode> modes,
        IReadOnlyList<FirmwareTarget> firmware,
        IUpdateProtocol protocol,
        IReadOnlyList<string>? suggestedTweaks = null,
        IReadOnlyList<string>? implicitTweaks = null,
        string cableName = "USB cable",
        string? connectHelp = null,
        string? reconnectHelp = null,
        string? powerOffHelp = null,
        string? supportedOs = null,
        string? credits = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A profile needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A profile needs a name.", nameof(name));
        if (string.IsNullOrWhiteSpace(shortName))
            throw new ArgumentException("A profile needs a short name.", nameof(shortName));
        if (string.IsNullOrWhiteSpace(vendor))
            throw new ArgumentException("A profile needs a vendor.", nameof(vendor));
        if (modes.Count == 0)
            throw new ArgumentException("A profile needs at least one device mode.", nameof(modes));
        if (firmware.Count == 0)
            throw new ArgumentException("A profile needs at least one firmware version.", nameof(firmware));
        layout.Validate();

        Id = id;
        Name = name;
        ShortName = shortName;
        Vendor = vendor;
        Disclaimer = disclaimer;
        BootHelp = bootHelp;
        Layout = layout;
        Modes = modes;
        Firmware = firmware;
        Protocol = protocol;
        SuggestedTweaks = suggestedTweaks ?? Array.Empty<string>();
        ImplicitTweaks = implicitTweaks ?? Array.Empty<string>();
        CableName = cableName;
        ConnectHelp = connectHelp ?? "Flashing only works over the cable. Wireless modes cannot flash.";
        ReconnectHelp = reconnectHelp ??
            "Unplug the cable and connect the controller the way you normally use it. " +
            "If Windows shows it as a new device, remove it in Settings and pair it again.";
        PowerOffHelp = powerOffHelp ?? "turn it fully off and on again";
        SupportedOs = supportedOs ?? "Windows 10 and 11";
        Credits = credits;
    }

    public string Id { get; }
    public string Name { get; }

    /// <summary>The model name without the vendor, for compact labels.</summary>
    public string ShortName { get; }

    /// <summary>Who makes the controller, used in the restore wording.</summary>
    public string Vendor { get; }

    /// <summary>What the app's disclaimer says about this model, after the shared warning.</summary>
    public string Disclaimer { get; }

    /// <summary>How to put this model into update mode.</summary>
    public string BootHelp { get; }
    public FlashLayout Layout { get; }
    public IReadOnlyList<DeviceMode> Modes { get; }
    public IReadOnlyList<FirmwareTarget> Firmware { get; }
    public IUpdateProtocol Protocol { get; }

    /// <summary>The tweaks the app offers for this model; a template can leave it empty.</summary>
    public IReadOnlyList<string> SuggestedTweaks { get; }

    /// <summary>Tweaks every install adds on top of the chosen ones (the Bluetooth interval on the 2C).</summary>
    public IReadOnlyList<string> ImplicitTweaks { get; }

    /// <summary>How the model connects for a flash, as the wizard names it.</summary>
    public string CableName { get; }

    /// <summary>What the first wizard step says about the cable and the wireless modes.</summary>
    public string ConnectHelp { get; }

    /// <summary>What to tell the user after a flash, when the controller may need pairing again.</summary>
    public string ReconnectHelp { get; }

    /// <summary>How to power the model off, used when it does not come back after a write.</summary>
    public string PowerOffHelp { get; }

    /// <summary>The operating systems this profile is known to work on.</summary>
    public string SupportedOs { get; }

    /// <summary>Optional per-model credits, shown in the About tab.</summary>
    public string? Credits { get; }

    public FirmwareTarget Primary => Firmware[0];

    /// <summary>The suggested tweaks that exist in this firmware target.</summary>
    public IReadOnlyList<string> SuggestedFor(FirmwareTarget target) =>
        SuggestedTweaks.Where(key => target.Catalog.All.ContainsKey(key)).ToList();

    /// <summary>The implicit tweaks that exist in this firmware target.</summary>
    public IReadOnlyList<string> ImplicitFor(FirmwareTarget target) =>
        ImplicitTweaks.Where(key => target.Catalog.All.ContainsKey(key)).ToList();

    public bool Supports(double version) =>
        Firmware.Any(f => Math.Abs(f.Version - version) < 1e-6);

    public string SupportedVersions => string.Join(", ",
        Firmware.Select(f => f.Version.ToString("0.00", CultureInfo.InvariantCulture)));

    public FirmwareTarget? TargetForVersion(double version) =>
        Firmware.FirstOrDefault(f => Math.Abs(f.Version - version) < 1e-6);

    public FirmwareTarget? TargetForStock(byte[] stock) =>
        Firmware.FirstOrDefault(f => f.IsStock(stock));

    /// <summary>The first firmware target that recognises this app area, with its state.</summary>
    public (FirmwareTarget Target, FirmwareState State)? Identify(byte[] bankA)
    {
        foreach (var target in Firmware)
        {
            var state = target.Catalog.Identify(bankA);
            if (state.Known)
                return (target, state with { Version = target.Version });
        }
        return null;
    }

    public FirmwareState State(byte[] bankA) => Identify(bankA)?.State ?? FirmwareState.Unknown;

    /// <summary>The mode this profile assigns to a HID interface, or null if the interface isn't one of its own.</summary>
    public string? Match(DeviceInfo device)
    {
        foreach (var mode in Modes)
        {
            if (mode.Matches(device))
                return mode.Resolve(device);
        }
        return null;
    }

    /// <summary>Modes that can read or write the controller over the cable.</summary>
    public static bool IsCableMode(string mode) =>
        mode is DeviceMode.Cable or DeviceMode.Cable360;

    /// <summary>Modes that can read or write the controller over USB; Bluetooth is not flashable.</summary>
    public static bool IsUsbMode(string mode) =>
        IsCableMode(mode) || mode is DeviceMode.Boot or DeviceMode.Dongle or DeviceMode.DonglePaired;

    public static bool IsDongleMode(string mode) =>
        mode is DeviceMode.Dongle or DeviceMode.DonglePaired;
}
