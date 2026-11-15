using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Ops;

/// <summary>A full read-only backup of the controller's flash.</summary>
public sealed record Backup(string FilePath, string Sha256, FirmwareState State, byte[] Data);

/// <summary>Full read-only backup of the controller's flash, over the cable (update channel) or in boot mode.</summary>
public static partial class BackupFile
{
    public static Backup Take(ControllerProfile profile, IChannel channel, string mode, string store,
        Action<string, int, int> progress)
    {
        byte[] a;
        byte[] b;
        try
        {
            var boot = profile.Protocol.IsBoot(mode);
            if (ControllerProfile.IsCableMode(mode) || boot)
                Probe.CheckDevice(profile, channel, mode, boot ? profile.Protocol.Identify(channel) : null);

            a = Probe.Read(profile, channel, mode, 0, profile.Layout.FlashSize, (done, total) => progress("pass 1", done, total));
            b = Probe.Read(profile, channel, mode, 0, profile.Layout.FlashSize, (done, total) => progress("pass 2", done, total));
        }
        catch (ProtocolException e)
        {
            throw new OpException(e.Message);
        }

        if (!a.SequenceEqual(b))
        {
            var places = 0;
            for (var i = 0; i < a.Length; i += profile.Protocol.Chunk)
            {
                var end = Math.Min(i + profile.Protocol.Chunk, a.Length);
                if (!a[i..end].SequenceEqual(b[i..end]))
                    places++;
            }
            throw new OpException($"The two reads of the controller's memory differ in {places} place(s), so nothing was saved. " +
                                  "Try again with another cable.");
        }

        Directory.CreateDirectory(store);
        var path = System.IO.Path.Combine(store,
            $"{Prefix(profile)}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.bin");
        var suffix = 1;
        while (File.Exists(path))
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(path).Split('+')[0];
            path = System.IO.Path.Combine(store, $"{stem}+{suffix}.bin");
            suffix++;
        }

        File.WriteAllBytes(path, a);
        var sha = Convert.ToHexString(SHA256.HashData(a)).ToLowerInvariant();
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() != sha)
            throw new OpException($"{path} didn't read back identically.");

        File.AppendAllText(System.IO.Path.Combine(store, "SHA256SUMS"), $"{sha}  {System.IO.Path.GetFileName(path)}\n");
        return new Backup(path, sha, IdentifyFrom(profile, a), a);
    }

    public static Backup Load(ControllerProfile profile, string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length != profile.Layout.FlashSize)
            throw new OpException($"{path} isn't a full backup of the controller ({data.Length} bytes).");
        return new Backup(path, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), IdentifyFrom(profile, data), data);
    }

    private static FirmwareState IdentifyFrom(ControllerProfile profile, byte[] data) =>
        profile.State(data[profile.Layout.Base..profile.Layout.AppEnd]);

    private static IEnumerable<Backup> Stored(ControllerProfile profile, string store)
    {
        if (!Directory.Exists(store))
            return Enumerable.Empty<Backup>();
        return Directory.GetFiles(store, "*.bin")
            .Where(p => IsForProfile(profile, p))
            .OrderByDescending(p => p, StringComparer.Ordinal)
            .Select(p =>
            {
                try
                {
                    return Load(profile, p);
                }
                catch (Exception e) when (e is IOException or OpException)
                {
                    return null;
                }
            })
            .Where(b => b is not null)!;
    }

    /// <summary>The filename prefix a profile uses; the old controller-* names still count as legacy.</summary>
    public static string Prefix(ControllerProfile profile)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(profile.Id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static bool IsForProfile(ControllerProfile profile, string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return name.StartsWith(Prefix(profile) + "-", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("controller-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How many backup files in the store belong to this profile; cheap enough for the live UI.</summary>
    public static int Count(ControllerProfile profile, string store) =>
        Directory.Exists(store) ? Directory.GetFiles(store, "*.bin").Count(p => IsForProfile(profile, p)) : 0;

    /// <summary>Backups in the store taken from this unit (same bootloader and address), newest first.</summary>
    public static IReadOnlyList<Backup> FindBackups(ControllerProfile profile, string store, byte[] bootloader, byte[] mac)
    {
        var layout = profile.Layout;
        return Stored(profile, store)
            .Where(b => b.Data[..layout.BootEnd].SequenceEqual(bootloader)
                        && layout.SameUnit(b.Data[layout.MacSector..(layout.MacSector + layout.Sector)], mac))
            .ToList();
    }

    /// <summary>
    /// The backup Restore uses: the newest recognised one that still holds the original firmware, so restoring undoes
    /// the tweaks; if there is none, the newest recognised one.
    /// </summary>
    public static Backup? Restorable(IEnumerable<Backup> backups)
    {
        var known = backups.Where(b => b.State.Known).ToList();
        return known.FirstOrDefault(b => b.State.Recipes.Count == 0) ?? known.FirstOrDefault();
    }

    /// <summary>When a backup was made, from its name, or else from the file.</summary>
    public static string Taken(string path)
    {
        var match = BackupName().Match(System.IO.Path.GetFileName(path));
        var date = match.Success
            ? DateTime.ParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            : File.GetLastWriteTime(path);
        return $"{date.Day} {date.ToString("MMM yyyy", CultureInfo.InvariantCulture)} at {date:HH:mm}";
    }

    /// <summary>The firmware in words: "the original firmware 1.09", "firmware 1.09 with A and B", or that it isn't recognised.</summary>
    public static string Describe(FirmwareState state)
    {
        if (!state.Known || state.Catalog is null)
            return "firmware this app doesn't recognise";
        var version = state.Version.ToString("0.00", CultureInfo.InvariantCulture);
        var titles = Tweaks.Shown(state.Recipes).Select(k => state.Catalog.All[k].Title).ToList();
        return titles.Count == 0 ? $"the original firmware {version}" : $"firmware {version} with " + Plan.Listed(titles);
    }

    /// <summary>The original firmware from this backup or any other, in bank A or in bank B.</summary>
    public static byte[]? FindOriginal(ControllerProfile profile, Backup backup, string store)
    {
        var layout = profile.Layout;
        foreach (var candidate in new[] { backup }.Concat(Stored(profile, store)))
        {
            foreach (var start in new[] { layout.Base, layout.BankB })
            {
                var bank = candidate.Data[start..(start + layout.BankASize)];
                var state = profile.State(bank);
                if (state.Known && state.Recipes.Count == 0)
                    return bank;
            }
        }
        return null;
    }

    [GeneratedRegex(@"-(\d{8}-\d{6})")]
    private static partial Regex BackupName();
}
