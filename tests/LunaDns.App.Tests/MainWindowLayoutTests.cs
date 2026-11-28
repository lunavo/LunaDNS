using System.Xml.Linq;

namespace LunaDns.App.Tests;

/// <summary>Spec D5: bốn khu theo thứ tự, mỗi lệnh nằm trong khu của nó. Đọc axaml như XML, không cần Avalonia.</summary>
public sealed class MainWindowLayoutTests
{
    private static readonly List<XElement> Elements =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Layout", "MainWindow.axaml")).Descendants().ToList();

    private static int IndexOf(string text)
    {
        var index = Elements.FindIndex(e => e.Attributes().Any(a => a.Value.Contains(text, StringComparison.Ordinal)));
        Assert.True(index >= 0, $"Không thấy {text} trong MainWindow.axaml");
        return index;
    }

    private static int Zone(string key) => IndexOf($"Strings.{key}}}");

    [Fact]
    public void Zones_are_in_order()
    {
        Assert.True(Zone("ZoneCheck") < Zone("ZoneStatus"));
        Assert.True(Zone("ZoneStatus") < Zone("ZoneUpdate"));
        Assert.True(Zone("ZoneUpdate") < Zone("ZoneCleanup"));
    }

    [Theory]
    [InlineData("{Binding CheckCommand}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding StopTuneCommand}", "ZoneCheck", "ZoneStatus")] // TuneCommand còn ở banner mạng chặn phía trên
    [InlineData("{Binding DiagnosticsCommand}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding HasAdvancedSettings}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding Features}", "ZoneStatus", "ZoneUpdate")]
    [InlineData("{Binding DomainGroups}", "ZoneStatus", "ZoneUpdate")]
    [InlineData("{Binding VersionText}", "ZoneUpdate", "ZoneCleanup")]
    [InlineData("{Binding CheckUpdateCommand}", "ZoneUpdate", "ZoneCleanup")]
    [InlineData("{Binding DomainList.ToggleCommand}", "ZoneUpdate", "ZoneCleanup")]
    [InlineData("{Binding DisableAllCommand}", "ZoneCleanup", null)]
    [InlineData("{Binding UninstallAppCommand}", "ZoneCleanup", null)]
    [InlineData("{Binding ClearNetworkProfilesCommand}", "ZoneCleanup", null)]
    [InlineData("{Binding NetworkLine}", "ZoneStatus", "ZoneUpdate")]
    [InlineData("{Binding ToggleRememberPerNetworkCommand}", "ZoneStatus", "ZoneUpdate")]
    [InlineData("Strings.UninstallHint}", "ZoneCleanup", null)]
    public void Control_sits_in_its_zone(string binding, string zone, string? nextZone)
    {
        var index = IndexOf(binding);

        Assert.True(index > Zone(zone), $"{binding} phải nằm sau {zone}");
        if (nextZone is not null) Assert.True(index < Zone(nextZone), $"{binding} phải nằm trước {nextZone}");
    }

    [Fact]
    public void Check_card_title_is_gone() => Assert.DoesNotContain(Elements,
        e => e.Attributes().Any(a => a.Value.Contains("CheckSectionTitle", StringComparison.Ordinal)));
}
