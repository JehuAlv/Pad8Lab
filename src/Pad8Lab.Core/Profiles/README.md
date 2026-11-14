# Controller profiles

A **profile** describes one controller family. The generic core (device discovery, backup, probe, patch, restore and the write planner) asks the profile for everything model-specific, so adding a controller does not mean changing those files.

| Piece | What it answers |
| --- | --- |
| `ControllerProfile` | Identity, flash map, HID modes, firmware targets, protocol and disclaimer |
| `FlashLayout` | Where the bootloader, bank A, bank B and unit data live |
| `DeviceMode` | Which VID/PID/usage page means which mode (`cable`, `boot`, `dongle`, `bluetooth`…) |
| `FirmwareTarget` | One supported firmware version, its stock hash and its catalog |
| `IFirmwareCatalog` | The tweak table, image builder and recogniser for one firmware version |
| `IUpdateProtocol` | How to read, erase, write, enter update mode and identify the unit |

## What is registered today

`ProfileRegistry.Default` registers only `U2cWireless.Profile`, the 8BitDo Ultimate 2C Wireless on firmware 1.09. The GUI and every operation work from that registry, so an unregistered profile is invisible in the app.

`TemplateProfile.cs` is a worked example and is **deliberately not registered**. It has placeholder IDs and addresses, and its catalog and protocol throw `NotSupportedException` so it cannot be used by accident. Use it as a guide; do not add it to the registry as-is.

## Adding another model

1. **Copy the guide.** Copy `TemplateProfile.cs` to a new file such as `VendorModel.cs`. Keep `TemplateProfile` untouched so it stays a readable example.
2. **Fill the flash map.** Replace every placeholder in `FlashLayout`. `ControllerProfile` calls `FlashLayout.Validate()` and refuses maps that could let a write reach the bootloader, the config area, the MAC sector or a record outside the flash.
3. **Map the HID modes.** Add one `DeviceMode` per interface the model shows. Use the real VID, PID, usage page and transport. `PairedVersion`/`PairedMode` is for identities that mean different things depending on the device version, as the U2C does for a paired dongle.
4. **Add one firmware target per supported version.** The stock hash must be the SHA-256 of that version's bank A. Do not guess it: read it from a known-stock unit or a trusted vendor image.
5. **Implement the catalog.** `IFirmwareCatalog` is the model's firmware knowledge:
   - `All` / `Order` / `RingOrder`: the tweak table and its order.
   - `Whole`: turn a bank A payload into the whole app area.
   - `Build`: refuse anything except the exact stock hash, then apply edits.
   - `Identify`: recognise stock, each tweak, and unknown firmware.
   The U2C implementation in `Firmware/U2cFirmwareCatalog.cs` is the reference.
6. **Implement the protocol.** `IUpdateProtocol` is the model's transport and bootloader protocol. The U2C implementation in `U2cProtocol.cs` wraps `AppProtocol` and `BootProtocol`.
7. **Register it.** Add the new profile to `ProfileRegistry.Default` in `ProfileRegistry.cs`. That single line is what makes it appear in the app.
8. **Add the firmware fragments.** If the catalog installs blobs, add them under `Firmware/Blobs/`; the `.csproj` embeds every `*.bin` there. Never embed a full stock firmware unless its licence allows it.
9. **Test before writing.** Build the solution, then run the read-only checks:
   - `ParityCheck` against a full backup of a stock unit, to prove the builder still produces the validated image.
   - `HardwareCheck devices`, `backup`, `check` and `restore-check`, which never write.
   Only then try a real install, on a unit you can restore.

## Safety rules for contributors

- **Never register an unfinished profile.** An unregistered profile is harmless; a registered one is offered to users.
- **Never write outside the app area.** `Plan.CheckRanges` and `FlashLayout.Validate` enforce the limits, but the layout must be correct in the first place.
- **Refuse unknown stock.** `Build` must check the stock hash before touching a byte, as `U2cFirmwareCatalog` does.
- **Keep backups unit-specific.** Backups are matched by bootloader and address; a restore must never write another unit's config or pairing data.
- **Do not infer hashes, addresses or protocol commands.** If you cannot verify a value, the profile is not ready.

## Suggested and implicit tweaks

`SuggestedTweaks` is what the app offers for the model. `ImplicitTweaks` is added to every install when the user chooses at least one tweak (the U2C adds `ble_interval`). Both are plain recipe keys; a template can leave them empty.

The vendor, short name, disclaimer and update-mode instructions are part of the profile so the core does not hardcode a manufacturer or a physical button combination in user-facing messages.
