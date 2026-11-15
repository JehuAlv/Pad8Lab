using Pad8Lab.Core;

// read-only runs of the C# flashing core against a connected controller
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: HardwareCheck <devices|backup|check|restore-check> [profileId]");
    return 2;
}

// the optional profile id lets a run name the model instead of discovering it
var profile = args.Length > 1 ? args[1] : null;
var service = new FlashService();
void Log(string line) => Console.WriteLine(line);
void Progress(string label, int done, int total)
{
    var percent = Math.Min(100, done * 100 / Math.Max(total, 1));
    Console.Write($"\r{label} {percent,3} %");
}

try
{
    switch (args[0])
    {
        case "devices":
            foreach (var line in service.DescribeDevices())
                Console.WriteLine(line);
            break;

        case "backup":
            service.Backup(Log, Progress, profile);
            break;

        case "check":
            service.Install(service.SuggestedTweaks(profile), write: false, disclaimerAccepted: false,
                Log, Progress, profileId: profile);
            break;

        case "restore-check":
            service.Restore("backup", write: false, forceUnknown: false, disclaimerAccepted: false,
                Log, Progress, profileId: profile);
            break;

        default:
            Console.Error.WriteLine($"unknown command {args[0]}");
            return 2;
    }
}
catch (Exception e)
{
    Console.WriteLine();
    Console.Error.WriteLine(e.Message);
    return 1;
}

Console.WriteLine();
return 0;
