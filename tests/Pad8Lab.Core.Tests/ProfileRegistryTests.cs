using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Tests;

public sealed class ProfileRegistryTests
{
    // the app offers exactly one model today
    [Fact]
    public void Default_registers_only_the_u2c()
    {
        var profiles = ProfileRegistry.Default.Profiles;
        Assert.Single(profiles);
        Assert.Same(U2cWireless.Profile, profiles[0]);
        Assert.Equal(U2cWireless.Id, profiles[0].Id);
    }

    // the template is an example, never a selectable model
    [Fact]
    public void Template_stays_out_of_the_registry()
    {
        var template = TemplateProfile.Create();
        Assert.DoesNotContain(ProfileRegistry.Default.Profiles, p => p.Id == template.Id);
        Assert.DoesNotContain(ProfileRegistry.Default.Profiles, p => p.Name == template.Name);
    }

    // the template must still describe a valid map so it can be copied
    [Fact]
    public void Template_describes_a_valid_map() => TemplateProfile.Create().Layout.Validate();

    // the catalog and protocol of the template refuse to run on purpose
    [Fact]
    public void Template_cannot_write_or_read()
    {
        var template = TemplateProfile.Create();
        Assert.Throws<NotSupportedException>(() => template.Primary.Catalog.Build(Array.Empty<byte>(), Array.Empty<string>()));
        Assert.Throws<NotSupportedException>(() => template.Protocol.Read(null!, DeviceMode.Boot, 0, 1));
    }

    // a registry cannot exist without models
    [Fact]
    public void Empty_registry_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new ProfileRegistry(Array.Empty<ControllerProfile>()));

    // two models cannot share an id, because the id names the profile
    [Fact]
    public void Duplicate_ids_are_rejected() =>
        Assert.Throws<ArgumentException>(() => new ProfileRegistry(new[] { U2cWireless.Profile, U2cWireless.Profile }));
}
