namespace LunaDns.Core.Platform;

/// <summary>Kết quả quét dọn dấu vết mồ côi: số mục đã gỡ theo loại, và loại nào lỗi (spec F7).</summary>
public sealed record OrphanSweepResult(int DnsRoutes, int AutoProxies, int HostsLines, IReadOnlyList<OrphanSweepFailure> Failed)
{
    public static OrphanSweepResult Empty { get; } = new(0, 0, 0, []);

    public int Removed => DnsRoutes + AutoProxies + HostsLines;

    public bool Success => Failed.Count == 0;
}

/// <param name="Op">Loại bị lỗi, theo tên trong <see cref="ChangeOps"/>.</param>
public sealed record OrphanSweepFailure(string Op, Exception Error);
