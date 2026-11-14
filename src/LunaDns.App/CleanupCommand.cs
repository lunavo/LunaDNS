using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --cleanup: hoàn tác mọi mục trong journal rồi thoát (spec 4.5). Giữ mutex trong suốt lúc chạy.
/// App đang chạy thì không làm gì và trả 0. Không đụng luật NRPT mồ côi (KH5 quyết định 10).
/// Mã thoát: 0 = xong hoặc không có gì để làm; 1 = còn mục lỗi, giữ trong journal.
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

    /// <summary>Hoàn tác journal, không lấy mutex: bên gọi phải đang giữ mutex (UninstallCommand). Mã như Run.</summary>
    internal static int RollbackJournal(AppPaths paths, Func<INetworkConfigurator> configurator, ILogger logger)
    {
        if (!File.Exists(paths.JournalFile))
        {
            LogNothingToDo(logger);
            return 0;
        }

        var journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator()));
        if (journal.Entries.Count == 0)
        {
            LogNothingToDo(logger);
            return 0;
        }
        var result = journal.RollbackAll();
        foreach (var failure in result.Failed) LogRevertFailed(logger, failure.Entry.Feature, failure.Entry.Op, failure.Error);
        LogDone(logger, result.Reverted, result.Failed.Count);
        return result.Success ? 0 : 1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: LunaDNS đang chạy, bỏ qua.")]
    private static partial void LogAppRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: journal trống, không có gì để hoàn tác.")]
    private static partial void LogNothingToDo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--cleanup: không hoàn tác được {Feature}/{Op}; giữ trong journal.")]
    private static partial void LogRevertFailed(ILogger logger, string feature, string op, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: đã hoàn tác {Reverted} mục, còn {Failed} mục lỗi.")]
    private static partial void LogDone(ILogger logger, int reverted, int failed);
}
