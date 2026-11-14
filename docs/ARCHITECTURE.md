# Architecture

Pad8Lab is a small firmware tool with a strict separation between the generic flashing pipeline and the
knowledge about one controller model.

## Projects

| Project | Kind | Responsibility |
| --- | --- | --- |
| `src/Pad8Lab.Core` | class library | Discovery, backup, planning, writing and restoring. Has no UI and no command-line parsing. |
| `src/Pad8Lab` | WPF app | The window, the log, the disclaimer gate and the live controller state. Calls only `FlashService`. |
| `tests/Pad8Lab.Core.Tests` | xUnit | Hardware-free invariants: layout, registry, mode matching, tweak table and plan ranges. |
| `tests/ParityCheck` | console | Proves the C# firmware builder matches the validated reference image. |
| `tests/HardwareCheck` | console | Runs devices, backup and check from a terminal. Never writes unless asked. |

The core targets `net10.0-windows` because it talks to Win32 HID directly, with no external packages.

## Layers inside the core

```
Pad8Lab (WPF)
    |
FlashService                  public facade: Backup, Install, Restore, Find
    |
Profiles                      ControllerProfile, ProfileRegistry, FirmwareTarget, DeviceMode
    |
Ops                           Backup, Probe, Plan, Patch, Restore, Devices, AppState
    |            \                        |
Protocol          Firmware                Hid
App/Boot          FlashLayout, catalogs   Native, HidTransport, DeviceInfo
```

- **`FlashService`** owns the orchestration: lock the store, find one device, verify it, build a plan,
  optionally write it, restart the controller and report the result. The GUI never talks to HID itself.
- **`Profiles`** is the only model-specific area. A profile supplies identity, flash layout, HID modes,
  firmware targets, the update protocol and the offered tweaks.
- **`Ops`** is the generic pipeline. It reads the unit data, matches backups to the unit, builds a
  `Plan` and applies it. It asks the profile for every model-specific value.
- **`Protocol`** implements the byte-level app and bootloader conversations.
- **`Firmware`** holds the flash map, the tweak table and the image builder for each firmware version.
- **`Hid`** wraps the Win32 calls and the HID channel.

Dependencies point downwards only. `Profiles` may reference `Firmware`, `Protocol` and `Hid`; `Ops`
may reference all of them; the UI depends on `FlashService` and the profile types it displays.

## The write path

1. `Find()` lists HID interfaces and asks each registered profile which mode each one is.
2. The user backs up first. `BackupFile.Take` reads the whole flash and stores it under
   `%LOCALAPPDATA%\Pad8Lab` with the bootloader and unit data needed to match it later.
3. `Install` or `Restore` builds a `Plan` over the cable. `Plan.CheckRanges` and `FlashLayout.Validate`
   reject anything that would reach the bootloader, the config sector, the MAC sector or the unit data.
4. Only a plan with real changes and `write: true` enters update mode. The app erases and writes exactly
   the planned sectors, reads them back to verify, then restarts the controller.
5. A no-op plan is reported as unchanged and writes nothing.

`SAFEGUARDS.md` lists every check in user-facing language. The write path must stay in this order:
plan, verify ranges, erase, write, read back, restart.

## Adding a controller model

The generic core is complete; a new model is a new profile. See
[`src/Pad8Lab.Core/Profiles/README.md`](../src/Pad8Lab.Core/Profiles/README.md) for the step-by-step
guide. `ProfileRegistry.Default` is the only list that determines what the app supports, and
`TemplateProfile` shows the shape of a profile without being registered.

## Testing

- `Pad8Lab.Core.Tests` covers everything that needs no controller: `FlashLayout.Validate` and
  `SameUnit`, the profile registry, the HID mode matching, the tweak table and its stock hashes, and
  `Plan.CheckRanges`.
- `ParityCheck` needs a full backup of a stock unit. It rebuilds the firmware and compares the size,
  SHA-256, recognised tweaks and the stock hash against the validated reference image.
- `HardwareCheck` exercises the read-only paths on real hardware.
- CI builds the whole solution and runs the unit tests on Windows on every push and pull request.
  Hardware tests stay manual because backups contain unit-specific data and must never be committed.
