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
        string vendor,
        string disclaimer,
        string bootHelp,
        FlashLayout layout,
        IReadOnlyList<DeviceMode> modes,
        IReadOnlyList<FirmwareTarget> firmware,
        IUpdateProtocol protocol,
        IReadOnlyList<string>? suggestedTweaks = null,
        IReadOnlyList<string>? implicitTweaks = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A profile needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A profile needs a name.", nameof(name));
        if (string.IsNullOrWhiteSpace(vendor))
            throw new ArgumentException("A profile needs a vendor.", nameof(vendor));
        if (modes.Count == 0)
            throw new ArgumentException("A profile needs at least one device mode.", nameof(modes));
        if (firmware.Count == 0)
            throw new ArgumentException("A profile needs at least one firmware version.", nameof(firmware));
        layout.Validate();

        Id = id;
        Name = name;
        Vendor = vendor;
        Disclaimer = disclaimer;
        BootHelp = bootHelp;
        Layout = layout;
        Modes = modes;
        Firmware = firmware;
        Protocol = protocol;
        SuggestedTweaks = suggestedTweaks ?? Array.Empty<string>();
        ImplicitTweaks = implicitTweaks ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string Name { get; }

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

    public FirmwareTarget Primary => Firmware[0];

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
