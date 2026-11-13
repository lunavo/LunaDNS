using System.Text.Json;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests.Journal;

/// <summary>Ghi lại giá trị đã hoàn tác. FailTimes > 0 thì ném lỗi đúng số lần đó rồi mới chạy được.</summary>
internal sealed class FakeReverter(string op) : IChangeReverter
{
    public string Op { get; } = op;
    public List<string> Reverted { get; } = [];
    public int FailTimes { get; set; }

    public void Revert(JsonElement snapshot)
    {
        if (FailTimes > 0)
        {
            FailTimes--;
            throw new InvalidOperationException("giả lập lỗi");
        }
        Reverted.Add(snapshot.GetProperty("value").GetString()!);
    }
}
