using LunaDns.Core.Journal;
using Microsoft.Extensions.Logging;

namespace LunaDns.Core.Platform;

/// <summary>
/// Quét dọn lúc khởi động (B5 + F7): gỡ dấu vết mang dấu LunaDNS mà journal không còn giữ, ví dụ khi journal.json hỏng hay bị xoá.
/// Gọi ngay sau <see cref="ChangeJournal.RollbackAll"/>, khi đang giữ khoá một-bản-chạy và TRƯỚC khi tự bật lại tính năng (F1):
/// lúc đó chưa tính năng nào chạy. Không bao giờ ném; lỗi nằm trong kết quả và log.
/// </summary>
public static partial class OrphanSweeper
{
    public static OrphanSweepResult Sweep(ChangeJournal journal, INetworkConfigurator configurator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(logger);
        var coverage = JournalCoverage.From(journal.Entries);
        foreach (var op in coverage.Unknown) LogSkipped(logger, op);
        OrphanSweepResult result;
        try
        {
            result = configurator.SweepOrphans(coverage);
        }
        catch (Exception ex) // quét là bước phụ lúc khởi động: lỗi gì cũng không được chặn app hay dịch vụ nền
        {
            result = new OrphanSweepResult(0, 0, 0, [new OrphanSweepFailure("*", ex)]);
        }
        if (result.DnsRoutes + result.HostsLines > 0) configurator.FlushDnsCache();
        if (result.Removed > 0) LogRemoved(logger, result.DnsRoutes, result.AutoProxies, result.HostsLines);
        foreach (var failure in result.Failed) LogFailed(logger, failure.Op, failure.Error);
        return result;
    }

    /// <summary>
    /// Khung chung cho các bản cài đặt <see cref="INetworkConfigurator.SweepOrphans"/>: mỗi loại chạy riêng, loại lỗi không chặn
    /// loại khác, loại <see cref="JournalCoverage.Unknown"/> hoặc phần null (nền tảng không quét loại đó) thì bỏ qua.
    /// Mỗi phần nhận tập đang được journal giữ và trả số mục đã gỡ.
    /// </summary>
    public static OrphanSweepResult RunParts(JournalCoverage coverage,
        Func<IReadOnlySet<string>, int>? dns, Func<IReadOnlySet<string>, int>? proxy, Func<IReadOnlySet<string>, int>? hosts)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        List<OrphanSweepFailure> failed = [];
        var d = Run(ChangeOps.DnsRoute, dns, coverage.DnsMarkers);
        var p = Run(ChangeOps.AutoProxy, proxy, coverage.PacUrls);
        var h = Run(ChangeOps.HostsEntries, hosts, coverage.HostsLines);
        return new OrphanSweepResult(d, p, h, failed);

        int Run(string op, Func<IReadOnlySet<string>, int>? part, IReadOnlySet<string> covered)
        {
            if (part is null || !coverage.IsKnown(op)) return 0;
            try
            {
                return part(covered);
            }
            catch (Exception ex)
            {
                failed.Add(new OrphanSweepFailure(op, ex));
                return 0;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quét dọn: journal còn mục {Op} không đọc được, bỏ qua loại này.")]
    private static partial void LogSkipped(ILogger logger, string op);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quét dọn: đã gỡ thay đổi LunaDNS không còn trong journal: {Dns} định tuyến DNS, {Proxy} PAC, {Hosts} dòng hosts.")]
    private static partial void LogRemoved(ILogger logger, int dns, int proxy, int hosts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Quét dọn: không gỡ được thay đổi mồ côi loại {Op}.")]
    private static partial void LogFailed(ILogger logger, string op, Exception error);
}
