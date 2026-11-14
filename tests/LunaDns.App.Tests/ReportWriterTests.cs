using LunaDns.Core.Logging;
using LunaDns.Platform.Windows.Diagnostics;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Proxy;

namespace LunaDns.App.Tests;

public sealed class ReportWriterTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTimeOffset Time = new(2026, 11, 13, 8, 9, 10, TimeSpan.FromHours(7));
    private readonly TestApp _app = new();
    private readonly WindowsDiagnostics _diagnostics;

    public ReportWriterTests()
    {
        var hosts = Path.Combine(_app.Paths.DataDirectory, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
        // Khoá test không tồn tại: ReadCurrent trả null, báo cáo ghi "(trống)". Không đụng Internet Settings thật.
        _diagnostics = new WindowsDiagnostics(new EmptyNrptStore(),
            new AutoProxyRegistry($@"Software\LunaDNS.Tests\{Guid.NewGuid():N}"), new HostsFileStore(hosts));
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Writes_named_utf8_bom_file_to_report_directory()
    {
        var path = ReportWriter.Write(_app.Paths, probe: null, _diagnostics, Time);

        Assert.Equal(Path.Combine(_app.Paths.ReportDirectory, "LunaDNS-report-20261113-080910.txt"), path);
        Assert.Equal(Bom, File.ReadAllBytes(path)[..3]);
        var text = File.ReadAllText(path);
        Assert.Contains("== Journal (0 mục) ==", text, StringComparison.Ordinal);
        Assert.Contains("(chưa chạy)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_tail_is_redacted()
    {
        File.WriteAllText(Path.Combine(_app.Paths.LogDirectory, RollingFileLoggerProvider.FileName),
            $"chạy bởi {Environment.UserName} trên {Environment.MachineName}{Environment.NewLine}");

        var text = File.ReadAllText(ReportWriter.Write(_app.Paths, probe: null, _diagnostics, Time));

        Assert.Contains("chạy bởi <user> trên <host>", text, StringComparison.Ordinal);
    }
}
