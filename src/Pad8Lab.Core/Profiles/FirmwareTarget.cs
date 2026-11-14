using Pad8Lab.Core.Firmware;

namespace Pad8Lab.Core.Profiles;

/// <summary>One firmware version a profile supports, and the stock image that identifies it.</summary>
public sealed record FirmwareTarget(double Version, string StockSha256, IFirmwareCatalog Catalog)
{
    public bool IsStock(byte[] bankA) => FirmwareHash.Sha256Hex(bankA) == StockSha256;
}
