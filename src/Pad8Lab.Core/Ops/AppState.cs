using System.Security.Cryptography;
using System.Text;

namespace Pad8Lab.Core.Ops;

/// <summary>Per-user state: where backups live, whether the disclaimer has been accepted, and the device lock.</summary>
public static class AppState
{
    public static string DefaultStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var store = Path.Combine(root, "Pad8Lab");
        var legacy = Path.Combine(root, "u2c-customfw");

        // renames the old store once, or keeps using it if the move fails
        if (!Directory.Exists(store) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, store);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return legacy;
            }
        }
        return store;
    }

    /// <summary>Whether this profile's disclaimer was accepted; the old global file still counts for existing users.</summary>
    public static bool Accepted(string store, string? profileId = null)
    {
        if (File.Exists(Path.Combine(store, AcceptanceFile(profileId))))
            return true;
        return profileId is not null && File.Exists(Path.Combine(store, "disclaimer-accepted"));
    }

    public static void Accept(string store, string? profileId = null)
    {
        Directory.CreateDirectory(store);
        File.WriteAllText(Path.Combine(store, AcceptanceFile(profileId)), "accepted\n");
    }

    // one acceptance file per profile, with the id reduced to a safe file name
    // and a short hash so two ids never collapse into the same file
    private static string AcceptanceFile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return "disclaimer-accepted";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(profileId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)))[..8].ToLowerInvariant();
        return $"disclaimer-accepted-{safe}-{hash}";
    }

    /// <summary>
    /// Taken while a front end uses the controller, so two of them can't interleave their requests. The open file,
    /// or null if another process holds it.
    /// </summary>
    public static FileStream? LockDevice(string store)
    {
        Directory.CreateDirectory(store);
        try
        {
            return new FileStream(Path.Combine(store, "device.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void UnlockDevice(FileStream? lockFile) => lockFile?.Dispose();
}
