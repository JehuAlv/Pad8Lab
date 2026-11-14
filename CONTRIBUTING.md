# Contributing to Pad8Lab

Pad8Lab is a firmware tool for 8BitDo controllers. It is small on purpose: the flashing core stays
readable and the model-specific knowledge lives in profiles. Contributions that keep that shape are
welcome.

## Set up

- Windows 10 or 11.
- The **.NET 10 SDK**. Nothing else: the core and the app have no external packages.
- Optional: a controller you can restore, for the manual hardware checks.

```sh
git clone https://github.com/JehuAlv/Pad8Lab
cd Pad8Lab
dotnet build Pad8Lab.slnx -c Release
dotnet run --project src/Pad8Lab/Pad8Lab.csproj
```

## Before you open a pull request

Run the checks that match your change:

```sh
# always
dotnet build Pad8Lab.slnx -c Release

# if you touched the firmware builder, the tweak table or a profile
dotnet run --project tests/ParityCheck/ParityCheck.csproj -c Release -- path\to\stock-backup.bin

# read-only hardware checks, with a controller connected by cable
dotnet run --project tests/HardwareCheck/HardwareCheck.csproj -- devices
dotnet run --project tests/HardwareCheck/HardwareCheck.csproj -- backup
dotnet run --project tests/HardwareCheck/HardwareCheck.csproj -- check
```

`ParityCheck` must print `PARITY OK`. If you changed how firmware is built, explain in the pull request
why the expected image is still correct.

### Never commit

- **Controller backups.** They contain unit-specific calibration and Bluetooth pairing keys.
- **Full stock firmware images.** Use the hash and the unit data from a backup; do not publish firmware
  you do not have the right to redistribute.
- **Artifacts.** `artifacts/`, `bin/` and `obj/` stay out of git.

## Code style

- C# only, with the settings in `.editorconfig`.
- File-scoped namespaces, 4 spaces, one statement per line.
- Comments in English, one line, lowercase, no final period. Comment the reason, not the obvious.
- Keep `Pad8Lab.Core` free of external packages and of UI code.
- Keep the safety order in the write path: plan, check ranges, erase, write, read back, restart.

## Adding a controller model

Read [`src/Pad8Lab.Core/Profiles/README.md`](src/Pad8Lab.Core/Profiles/README.md). In short:

1. Copy `TemplateProfile.cs` to a new file and fill in the real values.
2. Implement the firmware catalog and the update protocol for that model.
3. Test with read-only checks before writing anything.
4. Register the profile in `ProfileRegistry.Default` only when it is validated.

A profile that is not registered is invisible in the app, so work in progress is safe to commit.

## Commits and pull requests

- One topic per commit. The summary says what changed for the user.
- Pull requests should include the build result and, when relevant, the parity result.
- CI must pass. The workflow only builds; hardware tests stay manual because they need a controller.
- By contributing you agree to license your work under GPL-3.0-or-later.

## Releasing

1. Update `Version` in `Directory.Build.props`.
2. Build the single-file executable:

   ```powershell
   dotnet publish src/Pad8Lab/Pad8Lab.csproj -c Release -r win-x64 --self-contained true `
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
   ```

3. Rename it to `Pad8Lab-<version>-win-x64.exe`, sign it if you have a certificate (see the README),
   and write the SHA-256 to `SHA256SUMS.txt`.
4. Publish a GitHub release with both files and mark it as latest.
