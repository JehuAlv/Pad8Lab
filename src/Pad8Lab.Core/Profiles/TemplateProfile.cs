using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Profiles;

/// <summary>
/// A worked example for a new model. Nothing references it, so it never appears in the GUI:
/// copy it, replace every placeholder and register the result in <see cref="ProfileRegistry.Default"/>.
/// </summary>
public static class TemplateProfile
{
    // the fake addresses below only exist to show the shape; replace them with the model's real map
    public static ControllerProfile Create()
    {
        var layout = new FlashLayout(
            FlashSize: 0x100000,
            BootEnd: 0x8000,
            Base: 0x8000,
            BankASize: 0x20000,
            BankAEnd: 0x28000,
            BankAEraseEnd: 0x30000,
            AppEnd: 0x30000,
            AppSize: 0x28000,
            BankB: 0x40000,
            ConfigStart: 0xE0000,
            MacSector: 0xE1000,
            Sector: 0x1000,
            UpdateRecord: 0x3F000,
            Settings: 0xE0000,
            SettingsSize: 32,
            RingSaved: 0xE0010,
            NewAddresses: 0x1000);

        return new ControllerProfile(
            id: "vendor-model",
            name: "Example controller (template, not registered)",
            shortName: "Example controller",
            vendor: "Your vendor",
            disclaimer: "Replace this with which model and firmware versions the app supports.",
            bootHelp: "Replace this with how to put the model into update mode.",
            layout: layout,
            modes: new[]
            {
                // fill in the real VID/PID/usage page and add every mode the model shows
                new DeviceMode(0xFFFF, 0xFFFF, null, DeviceMode.Cable),
            },
            firmware: new[]
            {
                // one target per firmware version the model can run
                new FirmwareTarget(
                    Version: 1.00,
                    StockSha256: "replace-with-the-sha256-of-stock-bank-a",
                    Catalog: new TemplateCatalog()),
            },
            protocol: new TemplateProtocol(),
            suggestedTweaks: Array.Empty<string>(),
            implicitTweaks: Array.Empty<string>(),
            cableName: "USB cable",
            connectHelp: "Replace this with how the model connects for a flash.",
            reconnectHelp: "Replace this with what to do after a flash if the model pairs again.",
            powerOffHelp: "turn it fully off and on again",
            supportedOs: "Windows 11",
            credits: null);
    }

    // the catalog is where the model's tweak table, builder and recogniser live
    private sealed class TemplateCatalog : IFirmwareCatalog
    {
        public IReadOnlyDictionary<string, Recipe> All { get; } =
            new Dictionary<string, Recipe>(StringComparer.Ordinal);

        public IReadOnlyList<string> Order { get; } = Array.Empty<string>();

        public IReadOnlyList<string> RingOrder { get; } = Array.Empty<string>();

        public byte[] Whole(byte[] image) => (byte[])image.Clone();

        public byte[] Build(byte[] stock, IReadOnlyList<string> keys) =>
            throw Todo(nameof(Build));

        public FirmwareState Identify(byte[] bankA) => FirmwareState.Unknown;

        private static NotSupportedException Todo(string member) =>
            new($"{member}: add this model's tweak table and builder first.");
    }

    // the protocol is where the model's update channel, bootloader commands and identity checks live
    private sealed class TemplateProtocol : IUpdateProtocol
    {
        public int AppReportId => 0x81;
        public int BootReportId => 0;
        public int Chunk => 46;
        public string BootMode => DeviceMode.Boot;

        public bool IsBoot(string mode) => mode == BootMode;

        public byte[] Read(IChannel channel, string mode, int addr, int count, Action<int, int>? progress = null) =>
            throw Todo(nameof(Read));

        public void Erase(IChannel channel, int addr, int count) => throw Todo(nameof(Erase));

        public void WriteChunk(IChannel channel, int addr, int param, byte[] data) =>
            throw Todo(nameof(WriteChunk));

        public bool OpenSession(IChannel channel) => throw Todo(nameof(OpenSession));

        public void EnterUpdateMode(IChannel channel) => throw Todo(nameof(EnterUpdateMode));

        public void Restart(IChannel channel) => throw Todo(nameof(Restart));

        public int? Identify(IChannel channel) => throw Todo(nameof(Identify));

        public string? Kind(IChannel channel, int? identity) => throw Todo(nameof(Kind));

        public void CheckNotDongle(IChannel channel) => throw Todo(nameof(CheckNotDongle));

        private static NotSupportedException Todo(string member) =>
            new($"{member}: implement this for the model's protocol.");
    }
}
