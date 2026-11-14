using LunaDns.Core.Features;
using LunaDns.Core.Hosts;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;

namespace LunaDns.App.Tests;

/// <summary>Thư mục dữ liệu tạm, tên mutex riêng và configurator giả cho một test.</summary>
internal sealed class TestApp : IDisposable
{
    private readonly TempDirectory _dir = new();

    public TestApp()
    {
        Paths = new AppPaths(_dir.PathOf("data"), _dir.PathOf("logs"), _dir.PathOf("desktop"));
        Paths.EnsureCreated();
    }

    public AppPaths Paths { get; }

    public string MutexName { get; } = $@"Global\LunaDNS.Test.{Guid.NewGuid():N}";

    public FakeNetworkConfigurator Configurator { get; } = new();

    /// <summary>Giống app bị kill khi Chế độ PUBG đang bật: journal còn một mục hosts.</summary>
    public void SeedJournal()
    {
        var journal = new ChangeJournal(new JsonFileJournalStore(Paths.JournalFile), SystemChanges.Reverters(Configurator));
        new SystemChanges(journal, Configurator).AddHostsEntries(FeatureKeys.PubgMode,
            [new HostsEntry("127.0.0.1", "prod-live-front.playbattlegrounds.com")]);
    }

    public int JournalCount() => new JsonFileJournalStore(Paths.JournalFile).Load().Count;

    public void Dispose() => _dir.Dispose();
}
