namespace LunaDns.Platform.Windows.Tests;

/// <summary>
/// Test đổi cấu hình thật của Windows (NRPT, file hosts, AutoConfigURL, cache DNS). Chỉ chạy khi đặt LUNADNS_ADMIN_TESTS=1,
/// trong PowerShell chạy bằng quyền Admin. Đặt biến mà không có quyền Admin thì test hỏng (không âm thầm bỏ qua).
/// </summary>
public sealed class AdminFactAttribute : FactAttribute
{
    public AdminFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_ADMIN_TESTS") != "1")
            Skip = "Đặt LUNADNS_ADMIN_TESTS=1 và chạy PowerShell bằng quyền Admin để chạy test này.";
    }
}

/// <summary>Như AdminFact, và cần Internet thật (LUNADNS_NETWORK_TESTS=1).</summary>
public sealed class AdminNetworkFactAttribute : FactAttribute
{
    public AdminNetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_ADMIN_TESTS") != "1"
            || Environment.GetEnvironmentVariable("LUNADNS_NETWORK_TESTS") != "1")
            Skip = "Đặt LUNADNS_ADMIN_TESTS=1 và LUNADNS_NETWORK_TESTS=1, chạy bằng quyền Admin để chạy test này.";
    }
}

/// <summary>Các lớp test chạm cấu hình thật chạy tuần tự, vì chúng so sánh toàn bộ luật NRPT trước và sau.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "Tên cố định theo kế hoạch; xunit dùng hậu tố Collection.")]
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealSystemCollection
{
    public const string Name = "RealSystem";
}
