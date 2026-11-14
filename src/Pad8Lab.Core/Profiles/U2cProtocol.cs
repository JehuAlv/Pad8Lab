using Pad8Lab.Core.Hid;
using Pad8Lab.Core.Ops;
using Pad8Lab.Core.Protocol;

namespace Pad8Lab.Core.Profiles;

/// <summary>The update channel and bootloader protocol the Ultimate 2C family uses.</summary>
public sealed class U2cProtocol : IUpdateProtocol
{
    public int AppReportId => 0x81;
    public int BootReportId => 0;
    public int Chunk => BootProtocol.Chunk;
    public string BootMode => DeviceMode.Boot;

    public bool IsBoot(string mode) => mode == BootMode;

    public byte[] Read(IChannel channel, string mode, int addr, int count, Action<int, int>? progress = null)
    {
        if (IsBoot(mode))
            return BootProtocol.Read(channel, addr, count, progress);

        var output = new List<byte>(count);
        for (var a = addr; a < addr + count; a += Chunk)
        {
            var chunk = AppProtocol.Read(channel, a, Math.Min(Chunk, addr + count - a));
            if (chunk is null)
                throw new OpException($"The controller didn't answer at 0x{a:x}.");
            output.AddRange(chunk);
            if (progress is not null && output.Count % 0x4000 < Chunk)
                progress(output.Count, count);
        }
        return output.ToArray();
    }

    public void Erase(IChannel channel, int addr, int count) =>
        BootProtocol.Erase(channel, addr, count);

    public void WriteChunk(IChannel channel, int addr, int param, byte[] data) =>
        BootProtocol.WriteChunk(channel, addr, param, data);

    public bool OpenSession(IChannel channel) => AppProtocol.OpenSession(channel) is not null;

    public void EnterUpdateMode(IChannel channel) => AppProtocol.EnterUpdateMode(channel);

    public void Restart(IChannel channel) => channel.Xfer(BootProtocol.Request(7), 0.5);

    public int? Identify(IChannel channel) => BootProtocol.Identify(channel);

    public string? Kind(IChannel channel, int? identity) => BootProtocol.KindFrom(channel, identity);

    /// <summary>On an open update channel: a paired dongle has the controller's USB ID, but its 0x10000 is blank.</summary>
    public void CheckNotDongle(IChannel channel)
    {
        var head = AppProtocol.Read(channel, 0x10000, 16);
        if (head is null)
            throw new OpException("The controller isn't answering. Unplug it, plug it back in and try again.");
        if (head.All(b => b == 0xFF))
            throw new OpException("This looks like the 2.4 GHz dongle, which this app doesn't support.");
    }
}
