namespace LunaDns.Platform.MacOS.Tests;

/// <summary>
/// Điểm vào khi test tự chạy lại assembly này làm tiến trình con (dotnet exec). Test host không gọi Main, nên không ảnh hưởng
/// lúc chạy test bình thường.
/// </summary>
internal static class TestChildProgram
{
    public static int Main(string[] args) => args is ["process-runner-sigchld-blocked"] ? ProcessRunnerTests.RunChild() : 2;
}
