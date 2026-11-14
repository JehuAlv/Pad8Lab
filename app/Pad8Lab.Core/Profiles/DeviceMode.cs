using Pad8Lab.Core.Hid;

namespace Pad8Lab.Core.Profiles;

/// <summary>One HID identity a controller shows, and the mode it means.</summary>
public sealed record DeviceMode(
    ushort Vendor,
    ushort Product,
    ushort? UsagePage,
    string Mode,
    bool BluetoothOnly = false,
    bool Once = false,
    ushort? PairedVersion = null,
    string? PairedMode = null)
{
    public const string Cable = "cable";
    public const string Cable360 = "cable_360";
    public const string Boot = "boot";
    public const string Dongle = "dongle";
    public const string DonglePaired = "dongle_paired";
    public const string Bluetooth = "bluetooth";
    public const string BluetoothXbox = "bluetooth_xbox";

    public bool Matches(DeviceInfo device) =>
        device.Vendor == Vendor
        && device.Product == Product
        && (UsagePage is null || device.UsagePage == UsagePage)
        && (!BluetoothOnly || device.Transport.Contains("Bluetooth", StringComparison.Ordinal));

    public string Resolve(DeviceInfo device) =>
        PairedVersion is not null && device.Version == PairedVersion ? PairedMode ?? Mode : Mode;
}
