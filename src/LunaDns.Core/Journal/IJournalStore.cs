namespace LunaDns.Core.Journal;

public interface IJournalStore
{
    IReadOnlyList<JournalEntry> Load();
    void Save(IReadOnlyList<JournalEntry> entries);
}
