namespace LunaDns.Core.Journal;

public sealed record RollbackFailure(JournalEntry Entry, Exception Error);

public sealed record RollbackResult(int Reverted, IReadOnlyList<RollbackFailure> Failed)
{
    public bool Success => Failed.Count == 0;
}
