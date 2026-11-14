using System.Globalization;
using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Ops;

/// <summary>An operation refused or failed; the message says why and what to do.</summary>
public sealed class OpException : Exception
{
    public OpException(string message) : base(message)
    {
    }
}

/// <summary>A write plan and its execution: the stream proven on hardware, with the hard limits enforced here.</summary>
public sealed record Plan(
    byte[] Target,
    bool BankA,
    IReadOnlyList<int> OtherSectors,
    int DiffBytes,
    string Summary)
{
    private static string Interrupted(ControllerProfile profile) =>
        "The task was interrupted, and the controller may not start until it's finished. Use Restore to install your backup or " +
        $"{profile.Vendor}'s original firmware {profile.Primary.Version:0.00}. Update mode isn't affected.";

    public static string Count(int n, string noun) =>
        n.ToString("N0", CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? "" : "s");

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string Listed(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }

    public static void CheckRanges(FlashLayout layout, Plan plan)
    {
        foreach (var addr in plan.OtherSectors)
        {
            if (addr < layout.BankAEraseEnd || addr >= layout.ConfigStart || addr % layout.Sector != 0)
                throw new OpException($"The app won't write to the memory at 0x{addr:x}.");
        }
    }

    // merges adjacent sectors into runs
    private static List<(int Start, int End)> Runs(IEnumerable<int> sectors, int sector)
    {
        var output = new List<(int Start, int End)>();
        foreach (var addr in sectors.OrderBy(a => a))
        {
            if (output.Count > 0 && output[^1].End == addr)
                output[^1] = (output[^1].Start, addr + sector);
            else
                output.Add((addr, addr + sector));
        }
        return output;
    }

    public static void Apply(ControllerProfile profile, IChannel channel, Plan plan,
        Action<string, int, int> progress)
    {
        var protocol = profile.Protocol;
        var layout = profile.Layout;
        CheckRanges(layout, plan);
        var total = (plan.BankA ? layout.AppSize : 0) + plan.OtherSectors.Count * layout.Sector;
        var done = 0;
        try
        {
            if (plan.BankA)
            {
                protocol.Erase(channel, layout.Base, layout.BankAEraseEnd - layout.Base);
                for (var addr = layout.Base; addr < layout.BankAEnd; addr += protocol.Chunk)
                {
                    var block = Slice(plan.Target, addr, Math.Min(addr + protocol.Chunk, layout.BankAEnd));
                    protocol.WriteChunk(channel, addr, layout.BankASize, block);
                    done += block.Length;
                    progress("write", done, total);
                }

                for (var addr = layout.BankAEnd; addr < layout.AppEnd; addr += protocol.Chunk)   // the same param worked on hardware
                {
                    var block = Slice(plan.Target, addr, Math.Min(addr + protocol.Chunk, layout.AppEnd));
                    if (!block.All(b => b == 0xFF))
                        protocol.WriteChunk(channel, addr, layout.BankASize, block);
                    done += block.Length;
                    progress("write", done, total);
                }

                if (!protocol.Read(channel, protocol.BootMode, layout.Base, layout.AppSize).SequenceEqual(Slice(plan.Target, layout.Base, layout.AppEnd)))
                    throw new OpException("The firmware read back from the controller doesn't match what was written. " + Interrupted(profile));
            }

            foreach (var (lo, hi) in Runs(plan.OtherSectors, layout.Sector))
            {
                protocol.Erase(channel, lo, hi - lo);
                for (var addr = lo; addr < hi; addr += protocol.Chunk)
                {
                    var block = Slice(plan.Target, addr, Math.Min(addr + protocol.Chunk, hi));
                    if (!block.All(b => b == 0xFF))
                        protocol.WriteChunk(channel, addr, hi - lo, block);
                    done += block.Length;
                    progress("write", done, total);
                }
                if (!protocol.Read(channel, protocol.BootMode, lo, hi - lo).SequenceEqual(Slice(plan.Target, lo, hi)))
                    throw new OpException($"The memory read back at 0x{lo:x}-0x{hi:x} doesn't match what was written. " + Interrupted(profile));
            }
        }
        catch (ProtocolException e)
        {
            throw new OpException($"{e.Message}. {Interrupted(profile)}");
        }
    }

    private static byte[] Slice(byte[] data, int start, int end) => data[start..end];
}
