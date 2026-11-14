using Pad8Lab.Core.Firmware;

namespace Pad8Lab.Core.Profiles;

/// <summary>The 8BitDo Ultimate 2C Wireless (PC SKU) on firmware 1.09.</summary>
public static class U2cWireless
{
    public const string Id = "8bitdo-u2c-wireless";
    public const string StockSha256 = "dfd443c77f831008aa453ee2c1c9cb6871c36f8947c548e61d4387fba0a9da70";
    public const double FirmwareVersion = 1.09;

    public static readonly FlashLayout Layout = new(
        FlashSize: 0x80000,
        BootEnd: 0x6000,
        Base: 0x6000,
        BankASize: 0x12400,
        BankAEnd: 0x18400,
        BankAEraseEnd: 0x19000,
        AppEnd: 0x19000,
        AppSize: 0x13000,
        BankB: 0x40000,
        ConfigStart: 0x73000,
        MacSector: 0x76000,
        Sector: 0x1000,
        UpdateRecord: 0x3F000,
        Settings: 0x73000,
        SettingsSize: 26,
        RingSaved: 0x73019,
        NewAddresses: 0x1000);

    public static readonly ControllerProfile Profile = new(
        Id,
        "8BitDo Ultimate 2C Wireless",
        "8BitDo",
        "It only works with the Ultimate 2C on firmware 1.09.",
        "Turn the controller fully off, then hold LB + RB while plugging in the cable. The small light below the Home ring will blink white.",
        Layout,
        new DeviceMode[]
        {
            new(0x2DC8, 0x310A, 0xFF7A, DeviceMode.Cable),
            new(0x045E, 0x028E, 0xFF7A, DeviceMode.Cable360,
                PairedVersion: 0x0103, PairedMode: DeviceMode.DonglePaired),
            new(0x2DC8, 0x3208, null, DeviceMode.Boot),
            new(0x2DC8, 0x301C, 0xFFA0, DeviceMode.Dongle),
            new(0x045E, 0x0B13, null, DeviceMode.BluetoothXbox, BluetoothOnly: true, Once: true),
            new(0x2DC8, 0x301B, null, DeviceMode.Bluetooth, BluetoothOnly: true, Once: true),
        },
        new[]
        {
            new FirmwareTarget(FirmwareVersion, StockSha256, new U2cFirmwareCatalog(Layout, StockSha256)),
        },
        new U2cProtocol(),
        suggestedTweaks: new[] { "bt_xbox", "bt_rumble" },
        implicitTweaks: new[] { "ble_interval" });
}
