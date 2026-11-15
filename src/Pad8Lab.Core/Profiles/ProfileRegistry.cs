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

        // an interface may only be claimed by one model, or discovery would return the same device twice
        var claims = new Dictionary<(ushort Vendor, ushort Product, ushort? UsagePage, bool BluetoothOnly), string>();
        foreach (var profile in Profiles)
        {
            foreach (var mode in profile.Modes)
            {
                var key = (mode.Vendor, mode.Product, mode.UsagePage, mode.BluetoothOnly);
                if (claims.TryGetValue(key, out var owner) && owner != profile.Id)
                    throw new ArgumentException(
                        $"Profiles '{owner}' and '{profile.Id}' both claim " +
                        $"{mode.Vendor:x4}:{mode.Product:x4} usage {mode.UsagePage?.ToString("x4") ?? "any"}.",
                        nameof(profiles));
                claims[key] = profile.Id;
            }
        }
    }

    public IReadOnlyList<ControllerProfile> Profiles { get; }
}
