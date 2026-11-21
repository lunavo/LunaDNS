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

        Assert.Equal(183, properties.Count);
        foreach (var property in properties) AssertResolved((string)property.GetValue(null)!);
    }

    [Fact]
    public void Every_feature_error_except_none_has_text()
    {
        foreach (var error in Enum.GetValues<FeatureError>().Where(e => e != FeatureError.None))
            AssertResolved(Strings.Error(error));
    }

    /// <summary>Câu riêng macOS ("X_Mac") phải có câu chung tương ứng, và chỉ được dùng khi chạy trên macOS.</summary>
    [Fact]
    public void Mac_overrides_have_base_key_and_apply_only_on_mac()
    {
        var keys = new System.Resources.ResourceManager("LunaDns.App.Strings", typeof(Strings).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToList();
        var overrides = keys.Where(k => k.EndsWith(Strings.MacSuffix, StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(overrides);
        foreach (var key in overrides)
        {
            var baseKey = key[..^Strings.MacSuffix.Length];
            Assert.Contains(baseKey, keys);
            Assert.NotEqual(Strings.Lookup(baseKey, macOS: false), Strings.Lookup(baseKey, macOS: true));
        }
        Assert.Contains("15353", Strings.Lookup("Error_DnsPortBusy", macOS: true), StringComparison.Ordinal);
        Assert.Contains(" 53 ", Strings.Lookup("Error_DnsPortBusy", macOS: false), StringComparison.Ordinal);
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
        foreach (var group in DomainCatalog.Selectable.Append(DomainGroup.Custom)) AssertResolved(Strings.GroupName(group));
    }

    /// <summary>Khẩu hiệu cũ chỉ nhắc Steam; bản KH8 nói chung cho mọi nhóm tên miền.</summary>
    [Fact]
    public void Header_subtitle_no_longer_only_about_steam()
    {
        Assert.DoesNotContain("Sửa lỗi vào Steam", Strings.HeaderSubtitle, StringComparison.Ordinal);
        Assert.Contains("Reddit", Strings.HeaderSubtitle, StringComparison.Ordinal);
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
