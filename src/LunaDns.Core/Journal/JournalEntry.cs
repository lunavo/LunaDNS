using System.Text.Json;

namespace LunaDns.Core.Journal;

public sealed record JournalEntry(Guid Id, string Feature, string Op, JsonElement Snapshot, DateTimeOffset AppliedAt);
