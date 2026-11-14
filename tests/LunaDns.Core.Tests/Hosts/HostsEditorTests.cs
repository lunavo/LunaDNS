using System.Text;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Tests.Hosts;

public class HostsEditorTests
{
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private static readonly HostsEntry[] TwoEntries = [new("127.0.0.1", "pubg.com"), new("127.0.0.1", "lobby.pubg.com")];

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] RoundTrip(byte[] original, IReadOnlyList<HostsEntry> entries)
    {
        var snapshot = HostsEditor.Plan(original, entries);
        var applied = HostsEditor.Apply(original, snapshot);
        return HostsEditor.Revert(applied, snapshot);
    }

    [Theory]
    [InlineData("127.0.0.1 localhost\r\n")]
    [InlineData("127.0.0.1 localhost")]
    [InlineData("127.0.0.1 localhost\n# lf\n")]
    [InlineData("")]
    public void Round_trip_is_byte_identical(string text)
    {
        var original = Ascii(text);

        Assert.Equal(original, RoundTrip(original, TwoEntries));
    }

    [Fact]
    public void Round_trip_keeps_utf8_bom_and_ansi_vietnamese_bytes()
    {
        byte[] original =
        [
            0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Ghi chú tiếng Việt\r\n"),
            .. Ascii("# ANSI: "), 0xE0, 0xEA, 0xF4, 0x0D, 0x0A,
        ];

        Assert.Equal(original, RoundTrip(original, Pubg));
    }

    [Fact]
    public void Apply_appends_marked_lines_with_file_line_ending()
    {
        var original = Ascii("a\nb\n");

        var applied = HostsEditor.Apply(original, HostsEditor.Plan(original, TwoEntries));

        Assert.Equal("a\nb\n127.0.0.1 pubg.com # LunaDNS\n127.0.0.1 lobby.pubg.com # LunaDNS\n", Encoding.ASCII.GetString(applied));
    }

    [Fact]
    public void Apply_terminates_last_line_before_appending()
    {
        var original = Ascii("127.0.0.1 localhost");
        var snapshot = HostsEditor.Plan(original, Pubg);

        Assert.True(snapshot.AddedNewline);
        Assert.Equal(
            "127.0.0.1 localhost\r\n127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS\r\n",
            Encoding.ASCII.GetString(HostsEditor.Apply(original, snapshot)));
    }

    [Fact]
    public void Apply_twice_does_not_duplicate_lines()
    {
        var original = Ascii("127.0.0.1 localhost\r\n");
        var snapshot = HostsEditor.Plan(original, Pubg);
        var once = HostsEditor.Apply(original, snapshot);

        Assert.Equal(once, HostsEditor.Apply(once, snapshot));
    }

    [Fact]
    public void Revert_twice_is_noop()
    {
        var original = Ascii("127.0.0.1 localhost");
        var snapshot = HostsEditor.Plan(original, Pubg);
        var once = HostsEditor.Revert(HostsEditor.Apply(original, snapshot), snapshot);

        Assert.Equal(original, once);
        Assert.Equal(once, HostsEditor.Revert(once, snapshot));
    }

    [Fact]
    public void Revert_keeps_lines_added_after_ours()
    {
        var original = Ascii("127.0.0.1 localhost\r\n");
        var snapshot = HostsEditor.Plan(original, Pubg);
        byte[] edited = [.. HostsEditor.Apply(original, snapshot), .. Ascii("10.0.0.5 nas.lan\r\n")];

        Assert.Equal("127.0.0.1 localhost\r\n10.0.0.5 nas.lan\r\n", Encoding.ASCII.GetString(HostsEditor.Revert(edited, snapshot)));
    }

    [Fact]
    public void Revert_does_not_touch_lookalike_lines()
    {
        var snapshot = HostsEditor.Plan([], Pubg);
        var line = snapshot.Lines[0];
        var content = Ascii($"#{line}\r\n{line} extra\r\nx {line}\r\n");

        Assert.Equal(content, HostsEditor.Revert(content, snapshot));
    }

    [Fact]
    public void Plan_removes_duplicate_entries()
    {
        HostsEntry[] twice = [Pubg[0], Pubg[0] with { Host = "PROD-LIVE-FRONT.playbattlegrounds.com." }];

        Assert.Single(HostsEditor.Plan([], twice).Lines);
    }

    [Fact]
    public void Plan_rejects_utf16_file()
    {
        byte[] utf16 = [0xFF, 0xFE, (byte)'a', 0];

        var error = Assert.Throws<ConfiguratorException>(() => HostsEditor.Plan(utf16, Pubg));

        Assert.Equal(ConfiguratorError.HostsUnsupportedEncoding, error.Error);
    }

    [Theory]
    [InlineData("evilpubg.com")]
    [InlineData("example.com")]
    [InlineData("pubg.com\r\n1.2.3.4 example.com")]
    [InlineData("pubg.com example.com")]
    public void Plan_rejects_host_outside_game_domains_or_with_separators(string host)
    {
        HostsEntry[] entries = [new("127.0.0.1", host)];

        Assert.Throws<ArgumentException>(() => HostsEditor.Plan([], entries));
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("not-an-ip")]
    public void Plan_rejects_non_loopback_address(string address)
    {
        HostsEntry[] entries = [new(address, "pubg.com")];

        Assert.Throws<ArgumentException>(() => HostsEditor.Plan([], entries));
    }

    [Fact]
    public void Plan_rejects_empty_entry_list()
    {
        Assert.Throws<ArgumentException>(() => HostsEditor.Plan([], []));
    }
}
