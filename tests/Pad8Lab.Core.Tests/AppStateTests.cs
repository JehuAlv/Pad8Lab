using Pad8Lab.Core.Ops;

namespace Pad8Lab.Core.Tests;

public sealed class AppStateTests
{
    // accepting one model's disclaimer does not accept another model's
    [Fact]
    public void Acceptance_is_per_profile()
    {
        var store = TempStore();
        try
        {
            AppState.Accept(store, "model-a");

            Assert.True(AppState.Accepted(store, "model-a"));
            Assert.False(AppState.Accepted(store, "model-b"));
        }
        finally
        {
            Directory.Delete(store, recursive: true);
        }
    }

    // the old global file still counts, so upgrading users are not asked again
    [Fact]
    public void Legacy_global_acceptance_still_counts()
    {
        var store = TempStore();
        try
        {
            AppState.Accept(store);

            Assert.True(AppState.Accepted(store));
            Assert.True(AppState.Accepted(store, "model-a"));
        }
        finally
        {
            Directory.Delete(store, recursive: true);
        }
    }

    // ids with characters Windows rejects still produce a valid file name
    [Fact]
    public void Unsafe_ids_are_made_safe()
    {
        var store = TempStore();
        try
        {
            AppState.Accept(store, "a/b:c");

            Assert.True(AppState.Accepted(store, "a/b:c"));
            Assert.False(AppState.Accepted(store, "a_b_c"));
            Assert.Single(Directory.GetFiles(store, "disclaimer-accepted-a_b_c-*"));
        }
        finally
        {
            Directory.Delete(store, recursive: true);
        }
    }

    private static string TempStore()
    {
        var store = Path.Combine(Path.GetTempPath(), "pad8lab-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(store);
        return store;
    }
}
