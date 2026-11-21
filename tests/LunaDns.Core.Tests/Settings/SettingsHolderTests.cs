using LunaDns.Core.Settings;

namespace LunaDns.Core.Tests.Settings;

public sealed class SettingsHolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-holder-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Loads_once_and_save_updates_file_and_current()
    {
        var store = new JsonFileSettingsStore(Path.Combine(_dir, "settings.json"));
        var holder = new SettingsHolder(store);
        Assert.Equal(0, holder.Current.SplitDelayMs);

        holder.Save(holder.Current with { SplitDelayMs = 10 });

        Assert.Equal(10, holder.Current.SplitDelayMs);
        Assert.Equal(10, store.Load().SplitDelayMs);
    }

    [Fact]
    public void Failed_save_keeps_current()
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(path); // settings.json là thư mục: ghi sẽ lỗi
        var holder = new SettingsHolder(new JsonFileSettingsStore(path));

        Assert.ThrowsAny<IOException>(() => holder.Save(holder.Current with { SplitDelayMs = 10 }));
        Assert.Equal(0, holder.Current.SplitDelayMs);
    }
}
