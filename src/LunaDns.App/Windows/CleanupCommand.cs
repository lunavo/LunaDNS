using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --cleanup: hoàn tác mọi mục trong journal, quét luật NRPT và dòng hosts mang dấu LunaDNS mà journal không còn
/// giữ (B5 + F7, thay KH5 quyết định 10), rồi thoát (spec 4.5). Giữ mutex trong suốt lúc chạy. App đang chạy thì không làm gì và trả 0.
/// Mã thoát: 0 = xong hoặc không có gì để làm; 1 = còn mục lỗi (giữ trong journal) hoặc quét dọn có lỗi.
/// </summary>
internal static partial class CleanupCommand
{
    public static int Run(AppPaths paths, Func<INetworkConfigurator> configurator, ILogger logger, string mutexName = SingleInstance.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(logger);
        using var instance = SingleInstance.TryAcquire(mutexName);
        if (instance is null)
        {
            LogAppRunning(logger);
            return 0;
        }
        return RollbackJournal(paths, configurator, logger);
    }

    /// <summary>
    /// Hoàn tác journal rồi quét dấu vết LunaDNS mồ côi (B5 + F7), không lấy mutex: bên gọi phải đang giữ mutex (UninstallCommand).
    /// Quét chạy cả khi không có journal (journal bị xoá). Mã như Run.
    /// </summary>
    internal static int RollbackJournal(AppPaths paths, Func<INetworkConfigurator> configurator, ILogger logger)
    {
        var cfg = configurator();
        var journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile, logger), SystemChanges.Reverters(cfg));
        var result = journal.RollbackAll();
        foreach (var failure in result.Failed) LogRevertFailed(logger, failure.Entry.Feature, failure.Entry.Op, failure.Error);
        var sweep = OrphanSweeper.Sweep(journal, cfg, logger);
        if (result.Success && result.Reverted == 0 && sweep.Success && sweep.Removed == 0)
        {
            LogNothingToDo(logger);
            return 0;
        }
        LogDone(logger, result.Reverted, result.Failed.Count, sweep.Removed, sweep.Failed.Count);
        return result.Success && sweep.Success ? 0 : 1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: LunaDNS đang chạy, bỏ qua.")]
    private static partial void LogAppRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: không có gì để hoàn tác hay quét dọn.")]
    private static partial void LogNothingToDo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--cleanup: không hoàn tác được {Feature}/{Op}; giữ trong journal.")]
    private static partial void LogRevertFailed(ILogger logger, string feature, string op, Exception error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "--cleanup: đã hoàn tác {Reverted} mục, còn {Failed} mục lỗi; quét dọn gỡ {Swept} mục mồ côi, {SweepFailed} loại lỗi.")]
    private static partial void LogDone(ILogger logger, int reverted, int failed, int swept, int sweepFailed);
}
