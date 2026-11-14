using System.Net;
using System.Text.Json;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Diagnostics;

public sealed class DiagnosticReportTests
{
    private static readonly string[] Sections =
    [
        "== Kết quả kiểm tra ==", "== Journal (", "== Luật NRPT của LunaDNS ==", "== AutoConfigURL ==",
        "== Dòng hosts có # LunaDNS ==", "== 200 dòng log cuối ==",
    ];

    private static readonly Redactor Redactor = new("lunav", "DESKTOP-42");

    private static DiagnosticInput Input(
        IReadOnlyList<ProbeResult>? probe = null,
        IReadOnlyList<JournalEntry>? journal = null,
        IReadOnlyList<string>? logTail = null) =>
        new("0.5.0", "Windows 10 Pro", new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero), probe,
            journal ?? [], ["{abc} | LunaDNS 1 | .steampowered.com → 127.0.0.1"], "(trống)",
            ["127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS"], logTail ?? []);

    [Fact]
    public void Lists_every_section_in_order()
    {
        var report = DiagnosticReport.Build(Input(), Redactor);

        var positions = Sections.Select(s => report.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.StartsWith("LunaDNS — báo cáo chẩn đoán", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_labels_replace_windows_section_titles()
    {
        var report = DiagnosticReport.Build(
            Input() with { WindowsVersion = "macOS 26.0", Labels = new DiagnosticLabels("Hệ điều hành", "File /etc/resolver của LunaDNS", "PAC (networksetup)") },
            Redactor);

        Assert.Contains("Hệ điều hành: macOS 26.0", report, StringComparison.Ordinal);
        Assert.Contains("== File /etc/resolver của LunaDNS ==", report, StringComparison.Ordinal);
        Assert.Contains("== PAC (networksetup) ==", report, StringComparison.Ordinal);
        Assert.DoesNotContain("NRPT", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_probe_says_not_run() =>
        Assert.Contains("(chưa chạy)", DiagnosticReport.Build(Input(), Redactor), StringComparison.Ordinal);

    [Fact]
    public void Probe_line_shows_verdict_and_both_address_lists()
    {
        var result = new ProbeResult("store.steampowered.com", ProbeVerdict.DnsPoisoned, [IPAddress.Loopback], [IPAddress.Parse("23.1.2.3")], true);

        var report = DiagnosticReport.Build(Input(probe: [result]), Redactor);

        Assert.Contains("store.steampowered.com: DnsPoisoned | DNS hệ thống: 127.0.0.1 (IP chặn) | DoH: 23.1.2.3", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Journal_snapshot_sid_is_masked()
    {
        var snapshot = JsonSerializer.SerializeToElement(new AutoProxySnapshot("S-1-5-21-1-2-3-1001", false, null, false, "http://127.0.0.1:18080/proxy.pac"));
        var entry = new JournalEntry(Guid.NewGuid(), "sni-proxy", ChangeOps.AutoProxy, snapshot, DateTimeOffset.UnixEpoch);

        var report = DiagnosticReport.Build(Input(journal: [entry]), Redactor);

        Assert.Contains("== Journal (1 mục) ==", report, StringComparison.Ordinal);
        Assert.Contains("<sid>", report, StringComparison.Ordinal);
        Assert.DoesNotContain("S-1-5-21-1-2-3-1001", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "(trống)")]
    [InlineData("http://127.0.0.1:18080/proxy.pac", "http://127.0.0.1:18080/proxy.pac")]
    [InlineData("http://wpad.corp.local/proxy.pac", "(có giá trị khác của người dùng, không ghi vào báo cáo)")]
    public void Auto_config_is_shown_only_when_it_is_ours(string? value, string expected) =>
        Assert.Equal(expected, DiagnosticReport.DescribeAutoConfig(value));

    [Fact]
    public void Keeps_only_last_200_log_lines()
    {
        var lines = Enumerable.Range(0, 250).Select(i => $"log {i}").ToList();

        var report = DiagnosticReport.Build(Input(logTail: lines), Redactor);

        Assert.Contains("log 249", report, StringComparison.Ordinal);
        Assert.Contains("log 50" + Environment.NewLine, report, StringComparison.Ordinal);
        Assert.DoesNotContain("log 49" + Environment.NewLine, report, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_lines_are_redacted()
    {
        var report = DiagnosticReport.Build(Input(logTail: [@"mở C:\Users\lunav\a.txt từ 192.168.1.9"]), Redactor);

        Assert.Contains(@"mở C:\Users\<user>\a.txt từ 192.168.x.x", report, StringComparison.Ordinal);
    }
}
