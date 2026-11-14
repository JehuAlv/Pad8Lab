using Pad8Lab.Core.Firmware;
using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Tests;

public sealed class FirmwareCatalogTests
{
    private static readonly FlashLayout Layout = U2cWireless.Layout;
    private static readonly IFirmwareCatalog Catalog = U2cWireless.Profile.Primary.Catalog;

    // the order names the same recipes the map holds, once each
    [Fact]
    public void Order_and_map_describe_the_same_recipes()
    {
        Assert.Equal(Catalog.All.Count, Catalog.Order.Count);
        Assert.Equal(Catalog.Order.Count, Catalog.Order.Distinct(StringComparer.Ordinal).Count());
        foreach (var key in Catalog.Order)
            Assert.True(Catalog.All.ContainsKey(key), $"the order lists '{key}' without a recipe");
    }

    // every tweak the profile offers must exist in the catalog
    [Fact]
    public void Profile_tweaks_exist_in_the_catalog()
    {
        var profile = U2cWireless.Profile;
        foreach (var key in profile.SuggestedTweaks.Concat(profile.ImplicitTweaks))
        {
            Assert.True(Catalog.All.TryGetValue(key, out var recipe), $"the profile offers '{key}' without a recipe");
            Assert.Null(recipe.EarlierFormOf);
        }
    }

    // a dependency on a key that does not exist would block every install
    [Fact]
    public void Every_reference_points_to_a_recipe()
    {
        foreach (var recipe in Catalog.All.Values)
        {
            foreach (var need in recipe.Requires)
                Assert.True(Catalog.All.ContainsKey(need), $"{recipe.Key} needs '{need}'");
            if (recipe.RequiresGroup is { } group)
                Assert.Contains(Catalog.All.Values, other => other.Group == group);
            if (recipe.EarlierFormOf is { } earlier)
                Assert.True(Catalog.All.ContainsKey(earlier), $"{recipe.Key} replaces '{earlier}'");
            foreach (var edit in recipe.Edits)
            {
                if (edit.SupersededBy is { } next)
                {
                    Assert.True(Catalog.All.TryGetValue(next, out var other), $"{recipe.Key} is superseded by '{next}'");
                    Assert.Contains(other!.Edits, candidate => candidate.Addr == edit.Addr);
                }
            }
        }
    }

    // a write outside the app area would reach the bootloader or the unit data
    [Fact]
    public void Every_edit_stays_inside_the_app_area()
    {
        foreach (var recipe in Catalog.All.Values)
        {
            foreach (var edit in recipe.Edits)
            {
                Assert.True(edit.Addr >= Layout.Base, $"{recipe.Key} writes below the app area at 0x{edit.Addr:x}");
                Assert.True(edit.Addr + edit.Size <= Layout.AppEnd, $"{recipe.Key} writes past the app area at 0x{edit.Addr:x}");
                Assert.InRange(edit.Addr - Layout.Base, 0, Layout.AppSize - edit.Size);
            }
        }
    }

    // a missing stock hash would make an install fail halfway through the write
    [Fact]
    public void Every_edit_region_has_a_stock_hash()
    {
        foreach (var recipe in Catalog.All.Values)
        {
            foreach (var edit in recipe.Edits)
                Assert.True(StockHashes.Region.ContainsKey((edit.Addr, edit.Size)),
                    $"{recipe.Key} has no stock hash for 0x{edit.Addr:x} ({edit.Size} bytes)");
        }
    }

    // every stock hash is a lowercase sha-256 over a region inside the app area
    [Fact]
    public void Stock_hashes_are_well_formed()
    {
        foreach (var ((addr, size), hash) in StockHashes.Region)
        {
            Assert.True(addr >= Layout.Base);
            Assert.True(size > 0);
            Assert.True(addr + size <= Layout.AppEnd);
            Assert.Matches("^[0-9a-f]{64}$", hash);
        }
        Assert.Matches("^[0-9a-f]{64}$", StockHashes.Remainder);
    }

    // the ring choices share regions, so the same group holds every level and its earlier forms
    [Fact]
    public void Ring_group_holds_every_level()
    {
        var ring = Catalog.All.Values.Where(r => r.Group == "ring").Select(r => r.Key).ToList();
        Assert.NotEmpty(ring);
        Assert.Equal(ring.Count, ring.Distinct(StringComparer.Ordinal).Count());
        foreach (var key in Catalog.RingOrder)
            Assert.Contains(key, ring);
        foreach (var key in ring.Where(k => !k.Contains('@')))
            Assert.Contains(key, Catalog.RingOrder);
    }

    // an older ring form only rewrites regions its current form also knows
    [Fact]
    public void Earlier_ring_forms_only_touch_known_addresses()
    {
        foreach (var recipe in Catalog.All.Values.Where(r => r.Group == "ring" && r.EarlierFormOf is not null))
        {
            var current = Catalog.All[recipe.EarlierFormOf!];
            foreach (var edit in recipe.Edits)
            {
                var same = Assert.Single(current.Edits, candidate => candidate.Addr == edit.Addr);
                Assert.Equal(same.Size, edit.Size);
            }
        }
    }

    // a catalog only builds from the stock image it was registered with
    [Fact]
    public void Build_refuses_an_image_that_is_not_stock()
    {
        var zeros = new byte[Layout.BankASize];
        Assert.Throws<RecipeException>(() => Catalog.Build(zeros, Array.Empty<string>()));
        Assert.Throws<RecipeException>(() => Catalog.Build(new byte[16], Array.Empty<string>()));
    }

    // anything that is not a known tweak is refused before a write starts
    [Fact]
    public void Identify_reports_an_unknown_image()
    {
        Assert.False(Catalog.Identify(new byte[Layout.BankASize]).Known);
        Assert.False(Catalog.Identify(new byte[Layout.AppSize]).Known);
    }

    // a short bank A grows to the app area with the blank tail the chip reads
    [Fact]
    public void Whole_pads_bank_a_with_blank_bytes()
    {
        var bankA = new byte[Layout.BankASize];
        var whole = Catalog.Whole(bankA);

        Assert.Equal(Layout.AppSize, whole.Length);
        Assert.Equal(bankA, whole[..Layout.BankASize]);
        Assert.All(whole[Layout.BankASize..], b => Assert.Equal(0xFF, b));
    }

    // a full app area is already whole and comes back untouched
    [Fact]
    public void Whole_keeps_a_full_app_area()
    {
        var image = new byte[Layout.AppSize];
        Assert.Same(image, Catalog.Whole(image));
    }

    // the masked image zeroes exactly the regions the recipes rewrite
    [Fact]
    public void Masked_clears_every_edit_region()
    {
        var catalog = (U2cFirmwareCatalog)Catalog;
        var image = Enumerable.Repeat((byte)0xAA, Layout.AppSize).ToArray();
        var masked = catalog.Masked(image);

        foreach (var edit in Catalog.All.Values.SelectMany(r => r.Edits))
            Assert.All(masked[(edit.Addr - Layout.Base)..(edit.Addr - Layout.Base + edit.Size)], b => Assert.Equal(0, b));
        Assert.Equal(0xAA, masked[0]);
    }
}
