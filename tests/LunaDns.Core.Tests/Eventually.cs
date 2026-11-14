namespace LunaDns.Core.Tests;

/// <summary>Chờ điều kiện do luồng nền đặt, tối đa 5 giây.</summary>
internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Điều kiện không thành sau 5 giây.");
            await Task.Delay(10);
        }
    }
}
