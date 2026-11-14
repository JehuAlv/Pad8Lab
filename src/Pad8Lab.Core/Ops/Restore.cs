using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Ops;

/// <summary>Restore: put back this unit's backup, or the vendor's stock bank A.</summary>
public static class Restore
{
    /// <summary>Bank A may be in any state (this exists for damaged firmware); the unit checks still apply.</summary>
    public static Plan PlanRestore(ControllerProfile profile, IChannel channel, Backup backup, string source,
        byte[]? stock, bool forceUnknown = false, string mode = DeviceMode.Boot)
    {
        var layout = profile.Layout;
        var firmware = profile.TargetForStock(stock ?? Array.Empty<byte>()) ?? profile.Primary;
        Patch.CheckUnit(profile, channel, backup, mode);
        if (source is not ("backup" or "stock"))
            throw new OpException($"unknown restore source '{source}'");

        var current = Patch.CurrentBankA(profile, channel, mode);
        if (source == "stock")   // a backup restore puts the update record back too
        {
            Patch.CheckNoPendingUpdate(profile, channel,
                "A firmware update is pending on this controller, and it could replace the restored firmware the next time " +
                "the controller starts. Nothing was written. Restoring your backup still works.", mode);

            var (version, _) = Probe.DetectVersion(profile, channel, mode, current);
            if (version is null && !forceUnknown)
                throw new OpException("This controller's firmware version can't be identified, so nothing was written. Restoring your " +
                                      $"own backup still works, or you can use \"Install Firmware {profile.Primary.Version:0.00} Anyway…\" in the Diagnostics tab if you accept the risk.");
            if (version is not null && !profile.Supports(version.Value))
                throw new OpException($"This controller has firmware {version:F2}, and this app only supports {profile.SupportedVersions}. " +
                                      $"Update it with {profile.Vendor}'s own updater first. Restoring your own backup still works.");
        }

        var target = (byte[])backup.Data.Clone();
        if (source == "stock")
        {
            if (stock is null || stock.Length != layout.BankASize)
                throw new OpException($"There's no original firmware {profile.Primary.Version:0.00} to restore.");
            Array.Copy(firmware.Catalog.Whole(stock), 0, target, layout.Base, layout.AppSize);
        }

        var want = target[layout.Base..layout.AppEnd];
        var others = new List<int>();
        if (source == "backup")
        {
            byte[] now;
            try
            {
                now = Probe.Read(profile, channel, mode, layout.BankAEraseEnd, layout.ConfigStart - layout.BankAEraseEnd);
            }
            catch (ProtocolException e)
            {
                throw new OpException($"{e.Message}. Nothing was written.");
            }
            for (var addr = layout.BankAEraseEnd; addr < layout.ConfigStart; addr += layout.Sector)
            {
                var offset = addr - layout.BankAEraseEnd;
                if (!now[offset..(offset + layout.Sector)].SequenceEqual(target[addr..(addr + layout.Sector)]))
                    others.Add(addr);
            }
        }

        var diff = current.Zip(want).Count(pair => pair.First != pair.Second);
        var what = source == "backup"
            ? $"This restores the backup \u201c{System.IO.Path.GetFileName(backup.FilePath)}\u201d, made on {BackupFile.Taken(backup.FilePath)}, " +
              $"which has {BackupFile.Describe(backup.State)}."
            : $"This installs {profile.Vendor}'s original firmware {firmware.Version:0.00}.";
        var blocks = others.Count > 0
            ? $" and {Plan.Count(others.Count, "other block")} of the controller's memory"
            : "";
        return new Plan(target, !current.SequenceEqual(want), others, diff,
            $"{what} It changes {Plan.Count(diff, "byte")} of the firmware{blocks}.");
    }
}
