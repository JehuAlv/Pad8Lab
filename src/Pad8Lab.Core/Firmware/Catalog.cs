using System.Security.Cryptography;

namespace Pad8Lab.Core.Firmware;

/// <summary>What a profile needs from one firmware version: its tweak table, builder and recogniser.</summary>
public interface IFirmwareCatalog
{
    IReadOnlyDictionary<string, Recipe> All { get; }
    IReadOnlyList<string> Order { get; }
    IReadOnlyList<string> RingOrder { get; }
    byte[] Whole(byte[] image);
    byte[] Build(byte[] stock, IReadOnlyList<string> keys);
    FirmwareState Identify(byte[] bankA);
}

public sealed class RecipeException : Exception
{
    public RecipeException(string message) : base(message)
    {
    }
}

public sealed record Edit(int Addr, byte[] New, string? SupersededBy, IReadOnlyList<byte[]> Accepts)
{
    public int Size => New.Length;
}

public sealed record Recipe(
    string Key,
    string Title,
    IReadOnlyList<string> Requires,
    IReadOnlyList<Edit> Edits,
    string? Group = null,
    string? RequiresGroup = null,
    string? EarlierFormOf = null);

public sealed record FirmwareState(
    bool Known,
    IReadOnlyList<string> Recipes,
    bool Outdated = false,
    double Version = 0,
    IFirmwareCatalog? Catalog = null)
{
    public FirmwareState(bool known) : this(known, Array.Empty<string>())
    {
    }

    public static readonly FirmwareState Unknown = new(false);
}

internal static class ByteArrayExtensions
{
    // pads with a byte, or trims, to the requested length
    public static byte[] Pad(this byte[] data, int length, byte pad = 0)
    {
        if (data.Length == length)
            return data;
        var output = new byte[length];
        Array.Copy(data, output, Math.Min(data.Length, length));
        for (var i = data.Length; i < length; i++)
            output[i] = pad;
        return output;
    }
}

internal static class FirmwareHash
{
    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
