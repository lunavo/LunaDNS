using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Platform;

/// <summary>Đọc snapshot kiểu <typeparamref name="T"/> từ journal rồi gọi hàm hoàn tác. Snapshot sai dạng thì ném JsonException (mục được giữ lại).</summary>
internal sealed class SnapshotReverter<T>(string op, Action<T> revert, JsonTypeInfo<T> typeInfo) : IChangeReverter
    where T : class
{
    public string Op { get; } = op;

    public void Revert(JsonElement snapshot) =>
        revert(snapshot.Deserialize(typeInfo) ?? throw new JsonException($"Snapshot rỗng cho thao tác '{Op}'."));
}
