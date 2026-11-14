using Pad8Lab.Core.Hid;

namespace Pad8Lab.Core.Protocol;

/// <summary>How one controller family reads, erases and writes flash, and how it proves what it is.</summary>
public interface IUpdateProtocol
{
    int AppReportId { get; }
    int BootReportId { get; }
    int Chunk { get; }
    string BootMode { get; }

    bool IsBoot(string mode);
    byte[] Read(IChannel channel, string mode, int addr, int count, Action<int, int>? progress = null);
    void Erase(IChannel channel, int addr, int count);
    void WriteChunk(IChannel channel, int addr, int param, byte[] data);
    bool OpenSession(IChannel channel);
    void EnterUpdateMode(IChannel channel);
    void Restart(IChannel channel);
    int? Identify(IChannel channel);
    string? Kind(IChannel channel, int? identity);
    void CheckNotDongle(IChannel channel);
}
