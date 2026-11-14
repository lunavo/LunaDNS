namespace LunaDns.Core.Tests;

/// <summary>Test cần Internet thật: chỉ chạy khi đặt LUNADNS_NETWORK_TESTS=1, để lệnh kiểm tra mặc định không phụ thuộc mạng.</summary>
public sealed class NetworkFactAttribute : FactAttribute
{
    public NetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_NETWORK_TESTS") != "1")
            Skip = "Đặt LUNADNS_NETWORK_TESTS=1 để chạy test cần mạng.";
    }
}
