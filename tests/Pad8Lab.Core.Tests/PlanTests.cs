using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Ops;
using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Tests;

public sealed class PlanTests
{
    private static readonly FlashLayout Layout = U2cWireless.Layout;

    // the app may erase and write anywhere between the app end and the config
    [Fact]
    public void Sectors_inside_the_app_area_are_accepted()
    {
        Plan.CheckRanges(Layout, PlanAt(Layout.BankAEraseEnd));
        Plan.CheckRanges(Layout, PlanAt(Layout.ConfigStart - Layout.Sector));
        Plan.CheckRanges(Layout, PlanAt(Layout.BankAEraseEnd, Layout.ConfigStart - Layout.Sector));
    }

    // the bootloader and the bank A area are never a target sector
    [Fact]
    public void Sectors_before_the_app_end_are_rejected()
    {
        Assert.Throws<OpException>(() => Plan.CheckRanges(Layout, PlanAt(Layout.BootEnd)));
        Assert.Throws<OpException>(() => Plan.CheckRanges(Layout, PlanAt(Layout.BankAEraseEnd - Layout.Sector)));
    }

    // the config, the MAC and the settings beyond it are protected
    [Fact]
    public void Sectors_from_the_config_on_are_rejected()
    {
        Assert.Throws<OpException>(() => Plan.CheckRanges(Layout, PlanAt(Layout.ConfigStart)));
        Assert.Throws<OpException>(() => Plan.CheckRanges(Layout, PlanAt(Layout.MacSector)));
        Assert.Throws<OpException>(() => Plan.CheckRanges(Layout, PlanAt(Layout.Settings)));
    }

    // a sector address that is not aligned would erase the wrong range
    [Fact]
    public void Misaligned_sectors_are_rejected() =>
        Assert.Throws<OpException>(() => Plan.CheckRanges(Layout, PlanAt(Layout.BankAEraseEnd + 1)));

    // an empty plan has nothing to check
    [Fact]
    public void A_plan_without_sectors_passes() => Plan.CheckRanges(Layout, PlanAt());

    // counts and lists read naturally in the UI
    [Fact]
    public void Formatting_helpers_read_naturally()
    {
        Assert.Equal("0 tweaks", Plan.Count(0, "tweak"));
        Assert.Equal("1 tweak", Plan.Count(1, "tweak"));
        Assert.Equal("1,024 tweaks", Plan.Count(1024, "tweak"));
        Assert.Equal("", Plan.Listed(Array.Empty<string>()));
        Assert.Equal("a", Plan.Listed(new[] { "a" }));
        Assert.Equal("a and b", Plan.Listed(new[] { "a", "b" }));
        Assert.Equal("a, b and c", Plan.Listed(new[] { "a", "b", "c" }));
    }

    // builds a plan that only carries the sectors under test
    private static Plan PlanAt(params int[] sectors) =>
        new(Array.Empty<byte>(), false, sectors, 0, "test");
}
