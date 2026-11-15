using Pad8Lab.Core.Firmware;

namespace Pad8Lab.Core.Ops;

/// <summary>The tweaks to name in a plan or backup summary.</summary>
public static class Tweaks
{
    /// <summary>Every chosen key is shown; a profile decides what it offers in the first place.</summary>
    public static IReadOnlyList<string> Shown(IEnumerable<string> keys) => keys.ToList();
}
