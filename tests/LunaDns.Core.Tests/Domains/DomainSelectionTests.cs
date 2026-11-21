using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainSelectionTests
{
    [Fact]
    public void Default_routes_exactly_the_v1_steam_list() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.Steam), FeatureDomains.Routed(new DomainSelection()));

    [Fact]
    public void Routed_merges_groups_and_enabled_custom_entries()
    {
        var selection = new DomainSelection
        {
            EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom],
            Custom = [new("example.org"), new("off.example.net", Enabled: false)],
        };

        var routed = FeatureDomains.Routed(selection);

        Assert.Contains("reddit.com", routed);
        Assert.Contains("example.org", routed);
        Assert.DoesNotContain("off.example.net", routed);
        Assert.DoesNotContain("steampowered.com", routed);
    }

    [Fact]
    public void Routed_ignores_custom_entries_when_custom_group_is_off()
    {
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Steam], Custom = [new("example.org")] };
        Assert.DoesNotContain("example.org", FeatureDomains.Routed(selection));
    }

    [Fact]
    public void Routed_drops_subdomains_already_covered_and_never_includes_pubg()
    {
        var selection = new DomainSelection
        {
            EnabledGroups = [DomainGroup.Steam, DomainGroup.Pubg, DomainGroup.Custom],
            Custom = [new("store.steampowered.com"), new("example.org"), new("a.example.org")],
        };

        var routed = FeatureDomains.Routed(selection);

        Assert.DoesNotContain("store.steampowered.com", routed);
        Assert.DoesNotContain("a.example.org", routed);
        Assert.DoesNotContain("pubg.com", routed);
        Assert.Equal(routed.Count, routed.Distinct().Count());
    }

    [Fact]
    public void Sanitized_drops_invalid_duplicate_and_overflow_entries()
    {
        var custom = new List<CustomDomain> { new("Example.org"), new("example.org"), new("com"), new("dns.google") };
        custom.AddRange(Enumerable.Range(0, 250).Select(i => new CustomDomain($"d{i}.example.net")));
        var selection = new DomainSelection { Custom = custom };

        var clean = selection.Sanitized(out var dropped);

        Assert.Equal(DomainPolicy.MaxCustomDomains, clean.Custom.Count);
        Assert.Equal("example.org", clean.Custom[0].Domain);
        Assert.Equal(custom.Count - DomainPolicy.MaxCustomDomains, dropped);
    }

    [Fact]
    public void First_invalid_index_requires_normalised_valid_unique_entries()
    {
        Assert.Null(new DomainSelection { Custom = [new("example.org")] }.FirstInvalidIndex());
        Assert.Equal(1, new DomainSelection { Custom = [new("example.org"), new("Example.ORG")] }.FirstInvalidIndex());
        Assert.Equal(0, new DomainSelection { Custom = [new("com.vn")] }.FirstInvalidIndex());
        var tooMany = Enumerable.Range(0, 201).Select(i => new CustomDomain($"d{i}.example.org")).ToArray();
        Assert.Equal(DomainPolicy.MaxCustomDomains, new DomainSelection { Custom = tooMany }.FirstInvalidIndex());
    }

    [Fact]
    public void With_group_toggles_without_duplicates()
    {
        var s = new DomainSelection().WithGroup(DomainGroup.Reddit, true).WithGroup(DomainGroup.Reddit, true).WithGroup(DomainGroup.Steam, false);
        Assert.Equal([DomainGroup.Custom, DomainGroup.Reddit], s.EnabledGroups);
    }

    [Fact]
    public void Same_as_compares_content_not_references()
    {
        var a = new DomainSelection { Custom = [new("example.org")] };
        var b = new DomainSelection { Custom = [new("example.org")] };
        Assert.True(a.SameAs(b));
        Assert.False(a.SameAs(b with { Custom = [new("example.org", Enabled: false)] }));
    }

    [Fact]
    public void Shape_check_rejects_null_lists_unknown_and_duplicate_groups()
    {
        Assert.Equal(DomainSelectionDefect.None, new DomainSelection().CheckShape());
        Assert.Equal(DomainSelectionDefect.MissingGroups, new DomainSelection { EnabledGroups = null! }.CheckShape());
        Assert.Equal(DomainSelectionDefect.MissingCustom, new DomainSelection { Custom = null! }.CheckShape());
        Assert.Equal(DomainSelectionDefect.UnknownGroup, new DomainSelection { EnabledGroups = [(DomainGroup)99] }.CheckShape());
        Assert.Equal(DomainSelectionDefect.DuplicateGroup,
            new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Reddit] }.CheckShape());
    }

    [Fact]
    public void First_invalid_index_reports_null_entry_instead_of_throwing() =>
        Assert.Equal(1, new DomainSelection { Custom = [new("example.org"), null!] }.FirstInvalidIndex());

    [Fact]
    public void Sanitized_survives_nulls_and_unknown_groups()
    {
        var clean = new DomainSelection { EnabledGroups = null!, Custom = null! }.Sanitized(out var none);
        Assert.Equal(new DomainSelection(), clean);
        Assert.Equal(0, none);

        var fixedUp = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, (DomainGroup)99], Custom = [null!, new("example.org")] }
            .Sanitized(out var dropped);
        Assert.Equal([DomainGroup.Reddit], fixedUp.EnabledGroups);
        Assert.Equal([new CustomDomain("example.org")], fixedUp.Custom);
        Assert.Equal(2, dropped);
        Assert.NotEmpty(FeatureDomains.Routed(fixedUp));
    }
}
