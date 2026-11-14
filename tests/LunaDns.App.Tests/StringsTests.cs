using System.Globalization;
using System.Reflection;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.Tests;

public sealed class StringsTests
{
    private static void AssertResolved(string value) =>
        Assert.False(string.IsNullOrWhiteSpace(value) || value.StartsWith('['), $"Thiếu chuỗi: {value}");

    [Fact]
    public void Every_static_string_resolves()
    {
        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        Assert.Equal(67, properties.Count);
        foreach (var property in properties) AssertResolved((string)property.GetValue(null)!);
    }

    [Fact]
    public void Every_feature_error_except_none_has_text()
    {
        foreach (var error in Enum.GetValues<FeatureError>().Where(e => e != FeatureError.None))
            AssertResolved(Strings.Error(error));
    }

    [Fact]
    public void Every_verdict_has_text()
    {
        foreach (var verdict in Enum.GetValues<ProbeVerdict>()) AssertResolved(Strings.Verdict(verdict));
    }

    [Fact]
    public void Every_feature_and_shown_group_has_text()
    {
        foreach (var feature in Enum.GetValues<Feature>())
        {
            AssertResolved(Strings.FeatureTitle(feature));
            AssertResolved(Strings.FeatureDescription(feature));
        }
        AssertResolved(Strings.GroupName(DomainGroup.Steam));
    }

    /// <summary>KH5 quyết định 7: không có satellite assembly, Windows tiếng Anh vẫn ra tiếng Việt.</summary>
    [Fact]
    public void English_ui_culture_still_gets_vietnamese()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Equal("Kiểm tra", Strings.CheckButton);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
