using System.Text.Json;

namespace LunaDns.Core.Journal;

/// <summary>Hoàn tác một loại thay đổi hệ thống. Revert phải idempotent.</summary>
public interface IChangeReverter
{
    string Op { get; }
    void Revert(JsonElement snapshot);
}
