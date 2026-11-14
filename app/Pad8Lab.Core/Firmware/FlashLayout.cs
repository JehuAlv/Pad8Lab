namespace Pad8Lab.Core.Firmware;

/// <summary>Where the bootloader, the app banks and the unit data live in one controller family.</summary>
public sealed record FlashLayout(
    int FlashSize,
    int BootEnd,
    int Base,
    int BankASize,
    int BankAEnd,
    int BankAEraseEnd,
    int AppEnd,
    int AppSize,
    int BankB,
    int ConfigStart,
    int MacSector,
    int Sector,
    int UpdateRecord,
    int Settings,
    int SettingsSize,
    int RingSaved,
    int NewAddresses)
{
    /// <summary>Whether two copies of the address sector come from one controller.</summary>
    public bool SameUnit(byte[] a, byte[] b)
    {
        var near = Math.Abs(IntFromBytes(a, 0, 3) - IntFromBytes(b, 0, 3)) <= NewAddresses;
        return near && ByteRangesEqual(a, 3, b, 3, 3) && ByteRangesEqual(a, 7, b, 7, a.Length - 7);
    }

    /// <summary>Rejects a map that would let a write touch the bootloader or the unit data.</summary>
    public void Validate()
    {
        if (FlashSize <= 0 || Sector <= 0)
            throw new ArgumentException("The flash layout needs a positive flash size and sector size.");
        if (AppSize != AppEnd - Base || BankAEnd != Base + BankASize)
            throw new ArgumentException("The app size or bank A end doesn't match the addresses.");
        if (Base < BootEnd || BankAEnd > AppEnd || AppEnd > BankAEraseEnd)
            throw new ArgumentException("The app area must sit between the bootloader and the erase end.");
        if (BankAEraseEnd > ConfigStart || ConfigStart >= FlashSize)
            throw new ArgumentException("The config area must start after the app area and inside the flash.");
        if (BankB < AppEnd || BankB + BankASize > FlashSize)
            throw new ArgumentException("Bank B must fit after the app area and inside the flash.");
        if (MacSector + Sector > FlashSize || UpdateRecord + 0x24 > FlashSize || Settings + SettingsSize > FlashSize)
            throw new ArgumentException("A record in the layout falls outside the flash.");
        if (RingSaved < Settings || RingSaved >= Settings + SettingsSize)
            throw new ArgumentException("The saved ring level must fall inside the settings record.");
    }

    private static int IntFromBytes(byte[] data, int offset, int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
            value |= data[offset + i] << (8 * i);
        return value;
    }

    private static bool ByteRangesEqual(byte[] a, int aOffset, byte[] b, int bOffset, int count)
    {
        if (aOffset + count > a.Length || bOffset + count > b.Length)
            return false;
        for (var i = 0; i < count; i++)
        {
            if (a[aOffset + i] != b[bOffset + i])
                return false;
        }
        return true;
    }
}
