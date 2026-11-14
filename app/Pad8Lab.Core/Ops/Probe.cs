using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Ops;

/// <summary>Read the connected controller (read-only): which firmware it holds and which version.</summary>
public static class Probe
{
    /// <summary>Read n bytes by either channel; progress(done, total) is called along the way.</summary>
    public static byte[] Read(ControllerProfile profile, IChannel channel, string mode, int addr, int count,
        Action<int, int>? progress = null) =>
        profile.Protocol.Read(channel, mode, addr, count, progress);

    public static void CheckDevice(ControllerProfile profile, IChannel channel, string mode, int? identity)
    {
        if (profile.Protocol.IsBoot(mode))
        {
            if (profile.Protocol.Kind(channel, identity) != "controller")
                throw new OpException("The device in update mode isn't a controller. The 2.4 GHz dongle isn't supported.");
            return;
        }

        if (!profile.Protocol.OpenSession(channel))
            throw new OpException("The controller isn't answering. Unplug it, plug it back in and try again.");
        profile.Protocol.CheckNotDongle(channel);
    }

    /// <summary>Whether the update record holds a staged update.</summary>
    public static bool PendingUpdate(ControllerProfile profile, IChannel channel, string mode)
    {
        var record = Read(profile, channel, mode, profile.Layout.UpdateRecord, 0x24);
        return HasValue(record[0x18..0x1C]) || HasValue(record[0x20..0x24]);
    }

    private static bool HasValue(byte[] field) =>
        field.Any(b => b != 0x00) && field.Any(b => b != 0xFF);

    /// <summary>Firmware version from the update record (byte +0x0c is the version times 100), or null if blank.</summary>
    public static double? ParseVersion(byte[] record)
    {
        var value = record.Length > 0x0C ? record[0x0C] : 0;
        return value is 0 or 0xFF ? null : value / 100.0;
    }

    /// <summary>The firmware version and where it was read from.</summary>
    public static (double? Version, string From) DetectVersion(ControllerProfile profile, IChannel channel,
        string mode, byte[]? bankA = null)
    {
        try
        {
            var version = ParseVersion(Read(profile, channel, mode, profile.Layout.UpdateRecord, 16));
            if (version is not null)
                return (version, "update record");

            bankA ??= Read(profile, channel, mode, profile.Layout.Base, profile.Layout.AppSize);
            var found = profile.Identify(bankA);
            if (found is { } fromA)
                return (fromA.Target.Version, "bank A hash");

            found = profile.Identify(Read(profile, channel, mode, profile.Layout.BankB, profile.Layout.BankASize));
            if (found is { } fromB)
                return (fromB.Target.Version, "bank B hash");
        }
        catch (ProtocolException e)
        {
            throw new OpException(e.Message);
        }
        return (null, "unknown");
    }
}
