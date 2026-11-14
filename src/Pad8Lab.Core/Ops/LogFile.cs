using System.Globalization;

namespace Pad8Lab.Core.Ops;

/// <summary>The app's log, next to the backups.</summary>
public static class LogFile
{
    public static void Write(string store, string message)
    {
        Directory.CreateDirectory(store);
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + "\n";
        File.AppendAllText(Path.Combine(store, "pad8lab.log"), line);
    }
}
