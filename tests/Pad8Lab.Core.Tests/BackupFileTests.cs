using Pad8Lab.Core.Ops;
using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Tests;

public sealed class BackupFileTests
{
    // backups are named after the profile so two models share one store
    [Fact]
    public void Prefix_is_the_profile_id() =>
        Assert.Equal("8bitdo-u2c-wireless", BackupFile.Prefix(U2cWireless.Profile));

    // the count only sees this model's files, plus the legacy controller-* names
    [Fact]
    public void Count_filters_by_profile_and_keeps_legacy_names()
    {
        var store = TempStore();
        try
        {
            Save(store, "8bitdo-u2c-wireless-20260101-120000.bin");
            Save(store, "controller-20250101-120000.bin");
            Save(store, "other-model-20260101-120000.bin");

            Assert.Equal(2, BackupFile.Count(U2cWireless.Profile, store));
        }
        finally
        {
            Directory.Delete(store, recursive: true);
        }
    }

    // a missing store has no backups rather than throwing
    [Fact]
    public void Count_is_zero_when_the_store_does_not_exist() =>
        Assert.Equal(0, BackupFile.Count(U2cWireless.Profile,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    // the date is read from the new prefix and from the legacy one
    [Fact]
    public void Taken_reads_the_date_from_either_prefix()
    {
        Assert.Equal("1 Jan 2026 at 12:00", BackupFile.Taken("8bitdo-u2c-wireless-20260101-120000.bin"));
        Assert.Equal("1 Jan 2026 at 12:00", BackupFile.Taken("controller-20260101-120000.bin"));
    }

    private static string TempStore()
    {
        var store = Path.Combine(Path.GetTempPath(), "pad8lab-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(store);
        return store;
    }

    private static void Save(string store, string name) =>
        File.WriteAllBytes(Path.Combine(store, name), Array.Empty<byte>());
}
