namespace LunaDns.Platform.MacOS.Tests;

/// <summary>
/// Test đổi cấu hình thật của macOS (/etc/resolver, /etc/hosts, networksetup). Chỉ chạy khi đặt LUNADNS_ADMIN_TESTS=1
/// và chạy bằng sudo. Đặt biến mà không phải root thì test hỏng (không âm thầm bỏ qua).
/// </summary>
public sealed class MacAdminFactAttribute : FactAttribute
{
    public MacAdminFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_ADMIN_TESTS") != "1")
            Skip = "Đặt LUNADNS_ADMIN_TESTS=1 và chạy bằng sudo để chạy test này (xem docs/guide/09-macos.md).";
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "xunit dùng hậu tố Collection.")]
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealSystemCollection
{
    public const string Name = "RealSystem";
}
