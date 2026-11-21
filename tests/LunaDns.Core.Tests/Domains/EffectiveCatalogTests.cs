using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public sealed class EffectiveCatalogTests
{
    private static readonly DateTimeOffset Issued = new(2026, 11, 21, 14, 2, 0, TimeSpan.Zero);

    private static EffectiveCatalog With(DomainGroup group, params string[] added) =>
        EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [group] = added }, Issued);

    [Fact]
    public void Builtin_is_layer_one_only()
    {
        Assert.Equal(DomainCatalog.For(DomainGroup.Steam), EffectiveCatalog.Builtin.For(DomainGroup.Steam));
        Assert.Empty(EffectiveCatalog.Builtin.Added(DomainGroup.Steam));
        Assert.Null(EffectiveCatalog.Builtin.Issued);
    }

    [Fact]
    public void Layer_two_comes_after_layer_one_with_source()
    {
        var catalog = With(DomainGroup.Steam, "cdn.example.net");

        Assert.Equal([.. DomainCatalog.For(DomainGroup.Steam), "cdn.example.net"], catalog.For(DomainGroup.Steam));
        Assert.Equal(new CatalogEntry("cdn.example.net", CatalogSource.Update), catalog.Entries(DomainGroup.Steam)[^1]);
        Assert.Equal(CatalogSource.Builtin, catalog.Entries(DomainGroup.Steam)[0].Source);
        Assert.Equal(Issued, catalog.Issued);
    }

    [Fact]
    public void Group_not_selectable_gets_nothing() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.OtherGames), With(DomainGroup.OtherGames, "x.example.net").For(DomainGroup.OtherGames));

    [Fact]
    public void Pubg_never_gets_layer_two() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.Pubg), With(DomainGroup.Pubg, "x.example.net").For(DomainGroup.Pubg));

    [Fact]
    public void Invalid_duplicate_or_builtin_entries_are_filtered_again()
    {
        var catalog = With(DomainGroup.Steam, "cdn.example.net", "cdn.example.net", "steampowered.com", "com", "easyanticheat.net");
        Assert.Equal(["cdn.example.net"], catalog.Added(DomainGroup.Steam));
    }
}
