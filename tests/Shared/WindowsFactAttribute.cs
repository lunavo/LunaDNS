namespace LunaDns.Tests;

/// <summary>
/// Test cần Windows thật (registry, ACL, CIM, mutex có tên, đường dẫn kiểu C:\...). Trên macOS/Linux bản build
/// net8.0-windows vẫn chạy được nhưng các API này ném PlatformNotSupportedException, nên test tự skip.
/// CI Windows vẫn chạy đủ.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = WindowsOnly.Reason;
    }
}

/// <summary>Như WindowsFact, cho Theory.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = WindowsOnly.Reason;
    }
}

internal static class WindowsOnly
{
    public const string Reason = "Chỉ chạy trên Windows.";
}
