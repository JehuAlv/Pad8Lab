using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Tests;

public sealed class FlashLayoutTests
{
    // the registered model must always describe a map the core accepts
    [Fact]
    public void U2c_layout_is_valid() => U2cWireless.Layout.Validate();

    // a map without a flash or a sector size cannot describe a controller
    [Fact]
    public void Missing_flash_or_sector_is_rejected()
    {
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { FlashSize = 0 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { Sector = 0 }).Validate);
    }

    // the app size must match the addresses it claims to cover
    [Fact]
    public void App_size_must_match_the_addresses()
    {
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { AppSize = 0x12000 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { BankAEnd = 0x18000 }).Validate);
    }

    // a write must never start before the bootloader ends
    [Fact]
    public void App_area_must_start_after_the_bootloader()
    {
        var layout = U2cWireless.Layout with { Base = 0x5000, BankAEnd = 0x17400, AppEnd = 0x18000 };
        Assert.Throws<ArgumentException>(layout.Validate);
    }

    // the config area must sit after the app area and inside the flash
    [Fact]
    public void Config_area_must_follow_the_app_area()
    {
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { ConfigStart = 0x18000 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { ConfigStart = 0x90000 }).Validate);
    }

    // bank B is the second app copy and must fit inside the flash
    [Fact]
    public void Bank_b_must_fit_inside_the_flash()
    {
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { BankB = 0x18000 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { BankB = 0x70000 }).Validate);
    }

    // every unit record must end inside the flash
    [Fact]
    public void Unit_records_must_fit_inside_the_flash()
    {
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { MacSector = 0x7F800 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { UpdateRecord = 0x7FFF0 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { Settings = 0x7FFF0 }).Validate);
    }

    // the saved ring level lives inside the settings record
    [Fact]
    public void Saved_ring_level_must_be_inside_the_settings()
    {
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { RingSaved = 0x74000 }).Validate);
        Assert.Throws<ArgumentException>((U2cWireless.Layout with { RingSaved = 0x72FFF }).Validate);
    }

    // two address sectors from one unit differ only by the moving counter
    [Fact]
    public void Same_unit_accepts_a_near_counter_and_equal_data()
    {
        var first = new byte[32];
        var second = new byte[32];
        second[1] = 0x08;
        Assert.True(U2cWireless.Layout.SameUnit(first, second));
    }

    // a counter outside the moving window means another unit
    [Fact]
    public void Same_unit_rejects_a_far_counter()
    {
        var first = new byte[32];
        var second = new byte[32];
        second[1] = 0x20;
        Assert.False(U2cWireless.Layout.SameUnit(first, second));
    }

    // the MAC and the rest of the sector must match exactly
    [Fact]
    public void Same_unit_rejects_different_unit_data()
    {
        var first = new byte[32];
        var second = new byte[32];
        second[7] = 1;
        Assert.False(U2cWireless.Layout.SameUnit(first, second));

        var shortCopy = new byte[16];
        Assert.False(U2cWireless.Layout.SameUnit(first, shortCopy));
    }
}
