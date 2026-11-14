using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Ops;

/// <summary>Patch: stock bank A plus tweaks, written through the bootloader.</summary>
public static class Patch
{
    public const string Pending =
        "A firmware update is pending on this controller, and it could replace the tweaks the next time the controller starts. " +
        "Nothing was written. Start the controller normally once, then check again.";

    /// <summary>The controller must have this backup's bootloader and address.</summary>
    public static void CheckUnit(ControllerProfile profile, IChannel channel, Backup backup, string mode = DeviceMode.Boot)
    {
        var layout = profile.Layout;
        if (backup.Data.Length != layout.FlashSize)
            throw new OpException("This isn't a full backup of the controller.");
        try
        {
            if (profile.Protocol.IsBoot(mode)
                && profile.Protocol.Kind(channel, profile.Protocol.Identify(channel)) != "controller")
                throw new OpException("The device in update mode isn't a controller. The 2.4 GHz dongle isn't supported.");
            if (!Probe.Read(profile, channel, mode, 0, layout.BootEnd).SequenceEqual(backup.Data[..layout.BootEnd]))
                throw new OpException("This device doesn't match the backup, because its update-mode code is different. Nothing was written.");
            var mac = layout.MacSector;
            if (!layout.SameUnit(Probe.Read(profile, channel, mode, mac, layout.Sector), backup.Data[mac..(mac + layout.Sector)]))
                throw new OpException("This backup is from another controller, because the address is different. Nothing was written.");
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. Nothing was written.");
        }
    }

    /// <summary>A staged update could replace what is written at the next start.</summary>
    public static void CheckNoPendingUpdate(ControllerProfile profile, IChannel channel, string message = Pending,
        string mode = DeviceMode.Boot)
    {
        bool pending;
        try
        {
            pending = Probe.PendingUpdate(profile, channel, mode);
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. Nothing was written.");
        }
        if (pending)
            throw new OpException(message);
    }

    public static byte[] CurrentBankA(ControllerProfile profile, IChannel channel, string mode = DeviceMode.Boot)
    {
        try
        {
            return Probe.Read(profile, channel, mode, profile.Layout.Base, profile.Layout.AppSize);
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. Nothing was written.");
        }
    }

    public static Plan PlanPatch(ControllerProfile profile, IChannel channel, Backup backup, byte[] stock,
        IReadOnlyList<string> keys, string mode = DeviceMode.Boot)
    {
        var layout = profile.Layout;
        CheckUnit(profile, channel, backup, mode);
        CheckNoPendingUpdate(profile, channel, mode: mode);
        var current = CurrentBankA(profile, channel, mode);
        var replacing = profile.Identify(current) is null;
        var firmware = profile.TargetForStock(stock) ?? profile.Primary;
        if (replacing)   // the whole bank A is written from stock either way, so the supported versions are all that matter
        {
            var (version, _) = Probe.DetectVersion(profile, channel, mode, current);
            if (version is null)
                throw new OpException($"This controller's firmware isn't the original {firmware.Version:0.00}, with or without this app's tweaks, " +
                                      "and its version can't be identified, so nothing was written. It may be another firmware version, " +
                                      $"which you can update with {profile.Vendor}'s own updater, or firmware modified outside this app, " +
                                      "which restoring your backup replaces.");
            if (!profile.Supports(version.Value))
                throw new OpException($"This controller has firmware {version:F2}, and this app only supports " +
                                      $"{profile.SupportedVersions}, so nothing was written. Update it with {profile.Vendor}'s own updater first.");
        }

        byte[] targetBank;
        try
        {
            targetBank = firmware.Catalog.Build(stock, keys);
        }
        catch (RecipeException e)
        {
            throw new OpException(e.Message);
        }

        var target = new byte[layout.FlashSize];
        Array.Copy(targetBank, 0, target, layout.Base, targetBank.Length);
        var diff = current.Zip(targetBank).Count(pair => pair.First != pair.Second);
        var note = replacing ? $"This replaces firmware {firmware.Version:0.00} that this app doesn't recognise. " : "";
        var shown = Tweaks.Shown(keys);
        var tweaks = shown.Count > 0 ? Plan.Listed(shown.Select(k => firmware.Catalog.All[k].Title)) : "no tweaks";
        return new Plan(target, diff > 0, Array.Empty<int>(), diff,
            $"{note}Installing changes {Plan.Count(diff, "byte")} of the firmware, with {tweaks}.");
    }
}
