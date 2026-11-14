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

    public static bool Accepted(string store) => File.Exists(Path.Combine(store, "disclaimer-accepted"));

    public static void Accept(string store)
    {
        Directory.CreateDirectory(store);
        File.WriteAllText(Path.Combine(store, "disclaimer-accepted"), "accepted\n");
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
