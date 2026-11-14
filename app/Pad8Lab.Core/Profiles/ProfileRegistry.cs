namespace Pad8Lab.Core.Profiles;

/// <summary>The profiles this build knows. The GUI only shows devices these profiles match.</summary>
public sealed class ProfileRegistry
{
    // register a new model here; TemplateProfile stays out until it is a real, validated profile
    public static ProfileRegistry Default { get; } = new(new ControllerProfile[]
    {
        U2cWireless.Profile,
    });

    public ProfileRegistry(IEnumerable<ControllerProfile> profiles)
    {
        Profiles = profiles.ToList();
        if (Profiles.Count == 0)
            throw new ArgumentException("At least one profile is needed.", nameof(profiles));

        var duplicate = Profiles.GroupBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Two profiles use the id '{duplicate.Key}'.", nameof(profiles));
    }

    public IReadOnlyList<ControllerProfile> Profiles { get; }
}
