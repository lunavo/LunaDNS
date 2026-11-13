using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests.Journal;

internal sealed class InMemoryJournalStore : IJournalStore
{
    public List<JournalEntry> Saved { get; private set; } = [];
    public int SaveCount { get; private set; }

    public IReadOnlyList<JournalEntry> Load() => [.. Saved];

    public void Save(IReadOnlyList<JournalEntry> entries)
    {
        Saved = [.. entries];
        SaveCount++;
    }
}
