using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Tests;

public sealed class ControllerProfileTests
{
    private static readonly ControllerProfile U2c = U2cWireless.Profile;

    // the identity is what the GUI and the backup matching show
    [Fact]
    public void Identity_matches_the_supported_model()
    {
        Assert.Equal("8bitdo-u2c-wireless", U2c.Id);
        Assert.Equal("8BitDo Ultimate 2C Wireless", U2c.Name);
        Assert.Equal("Ultimate 2C Wireless", U2c.ShortName);
        Assert.Equal("8BitDo", U2c.Vendor);
    }

    // only the firmware the catalog knows is accepted
    [Fact]
    public void Only_firmware_1_09_is_supported()
    {
        Assert.True(U2c.Supports(1.09));
        Assert.False(U2c.Supports(1.08));
        Assert.False(U2c.Supports(1.10));
        Assert.Equal("1.09", U2c.SupportedVersions);
        Assert.Equal(1.09, U2c.Primary.Version);
    }

    // the version lookup returns the target with its catalog
    [Fact]
    public void Target_for_version_returns_the_catalog()
    {
        var target = U2c.TargetForVersion(1.09);
        Assert.NotNull(target);
        Assert.Same(U2c.Primary.Catalog, target!.Catalog);
        Assert.Null(U2c.TargetForVersion(1.08));
    }

    // an empty or random bank A is not a stock image
    [Fact]
    public void Unknown_bank_a_is_not_stock()
    {
        var zeros = new byte[U2c.Layout.BankASize];
        Assert.Null(U2c.TargetForStock(zeros));
        Assert.False(U2c.Primary.IsStock(zeros));
        Assert.False(U2c.State(zeros).Known);
    }

    // the cable interface resolves to the cable mode
    [Fact]
    public void Cable_interface_matches_cable_mode() =>
        Assert.Equal(DeviceMode.Cable, U2c.Match(Device(0x2DC8, 0x310A, 0xFF7A, "USB", 0)));

    // the Xbox identity over cable depends on the dongle version
    [Fact]
    public void Xbox_cable_identity_resolves_the_paired_mode()
    {
        Assert.Equal(DeviceMode.DonglePaired, U2c.Match(Device(0x045E, 0x028E, 0xFF7A, "USB", 0x0103)));
        Assert.Equal(DeviceMode.Cable360, U2c.Match(Device(0x045E, 0x028E, 0xFF7A, "USB", 0x0102)));
    }

    // Bluetooth identities only match when Windows reports a Bluetooth transport
    [Fact]
    public void Bluetooth_mode_requires_a_bluetooth_transport()
    {
        Assert.Equal(DeviceMode.BluetoothXbox, U2c.Match(Device(0x045E, 0x0B13, 0, "Bluetooth", 0)));
        Assert.Null(U2c.Match(Device(0x045E, 0x0B13, 0, "USB", 0)));
    }

    // unknown hardware is ignored instead of guessed
    [Fact]
    public void Unknown_interface_does_not_match() =>
        Assert.Null(U2c.Match(Device(0xFFFF, 0xFFFF, 0xFF7A, "USB", 0)));

    // the model-specific copy the GUI shows comes from the profile
    [Fact]
    public void Metadata_describes_the_model()
    {
        Assert.Equal("USB-C cable", U2c.CableName);
        Assert.Equal("Flashing only works over USB-C. Wireless modes cannot flash.", U2c.ConnectHelp);
        Assert.Contains("Home", U2c.PowerOffHelp);
        Assert.Equal("Windows 11", U2c.SupportedOs);
        Assert.False(string.IsNullOrWhiteSpace(U2c.Credits));
    }

    // a profile without optional metadata falls back to neutral wording
    [Fact]
    public void Missing_metadata_falls_back_to_neutral_copy()
    {
        var profile = new ControllerProfile(
            "copy", "Copy", "Copy", "Vendor", "disclaimer", "boot help",
            U2c.Layout, U2c.Modes, U2c.Firmware, U2c.Protocol);

        Assert.Equal("USB cable", profile.CableName);
        Assert.Equal("Windows 10 and 11", profile.SupportedOs);
        Assert.Null(profile.Credits);
        Assert.False(string.IsNullOrWhiteSpace(profile.ReconnectHelp));
    }

    // the offered tweaks are narrowed to the ones the catalog knows
    [Fact]
    public void Tweak_helpers_skip_keys_the_catalog_lacks()
    {
        Assert.Equal(new[] { "bt_xbox", "bt_rumble" }, U2c.SuggestedFor(U2c.Primary));
        Assert.Equal(new[] { "ble_interval" }, U2c.ImplicitFor(U2c.Primary));

        var profile = new ControllerProfile(
            "copy", "Copy", "Copy", "Vendor", "disclaimer", "boot help",
            U2c.Layout, U2c.Modes, U2c.Firmware, U2c.Protocol,
            suggestedTweaks: new[] { "bt_xbox", "not_in_catalog" },
            implicitTweaks: new[] { "missing" });

        Assert.Equal(new[] { "bt_xbox" }, profile.SuggestedFor(profile.Primary));
        Assert.Empty(profile.ImplicitFor(profile.Primary));
    }

    // only the USB modes can read or write the controller
    [Fact]
    public void Usb_modes_are_the_only_ones_that_can_flash()
    {
        Assert.True(ControllerProfile.IsCableMode(DeviceMode.Cable));
        Assert.True(ControllerProfile.IsCableMode(DeviceMode.Cable360));
        Assert.True(ControllerProfile.IsUsbMode(DeviceMode.Boot));
        Assert.True(ControllerProfile.IsUsbMode(DeviceMode.Dongle));
        Assert.False(ControllerProfile.IsUsbMode(DeviceMode.Bluetooth));
        Assert.True(ControllerProfile.IsDongleMode(DeviceMode.DonglePaired));
        Assert.False(ControllerProfile.IsDongleMode(DeviceMode.Cable));
    }

    // builds a device with the fields the mode matching reads
    private static DeviceInfo Device(ushort vendor, ushort product, ushort usagePage, string transport, ushort version) =>
        new(vendor, product, usagePage, transport, "controller", @"\\?\hid", version);
}
