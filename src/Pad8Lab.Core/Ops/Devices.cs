using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Profiles;

namespace Pad8Lab.Core.Ops;

/// <summary>One controller-like device, the mode it is in and the profile that recognised it.</summary>
public sealed record Found(DeviceInfo Info, string Mode, ControllerProfile Profile);

/// <summary>Finds the devices of every registered profile.</summary>
public static class Devices
{
    public static IReadOnlyList<Found> Find(ProfileRegistry registry)
    {
        var output = new List<Found>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in HidTransport.ListDevices())
        {
            foreach (var profile in registry.Profiles)
            {
                foreach (var entry in profile.Modes)
                {
                    if (!entry.Matches(device))
                        continue;

                    var mode = entry.Resolve(device);
                    if (entry.Once && output.Any(f => f.Profile == profile && f.Mode == mode))
                        break;

                    // one interface can expose several matching entries; keep the first for each profile and mode
                    if (!seen.Add($"{device.Path}|{profile.Id}|{mode}"))
                        break;

                    output.Add(new Found(device, mode, profile));
                    break;
                }
            }
        }
        return output;
    }
}
