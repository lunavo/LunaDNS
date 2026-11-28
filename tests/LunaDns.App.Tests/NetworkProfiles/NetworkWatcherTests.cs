using LunaDns.App.NetworkProfiles;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.NetworkProfiles;

public class NetworkWatcherTests
{
    private static NetworkSnapshot Net(string mac) => new(NetworkId.From(mac, "192.168.1.1", "Wireless80211", "Wi-Fi")!, "Wi-Fi · 192.168.1.1");

    private sealed class FakeIdentity : INetworkIdentity
    {
        public NetworkSnapshot? Value { get; set; }
        public int Calls { get; private set; }
        public Exception? Throw { get; set; }

        public NetworkSnapshot? Current()
        {
            Calls++;
            if (Throw is { } ex) throw ex;
            return Value;
        }
    }

    [Fact]
    public void IdentityThrows_OnTimer_NoEvent_RecoversNextPoll()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var seen = new List<NetworkSnapshot?>();
        watcher.Changed += seen.Add;
        watcher.Start();
        identity.Value = Net("aa:bb:cc:09:09:09");
        identity.Throw = new InvalidOperationException();
        watcher.Poke();
        time.Advance(NetworkWatcher.Settle);
        Assert.Empty(seen);
        identity.Throw = null;
        watcher.Poke();
        time.Advance(NetworkWatcher.Settle);
        Assert.Equal([identity.Value], seen);
    }

    [Fact]
    public void Start_ReadsCurrent_NoEvent()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        using var watcher = new NetworkWatcher(identity, new FakeTimeProvider());
        var events = 0;
        watcher.Changed += _ => events++;
        watcher.Start();
        Assert.Equal(identity.Value, watcher.Current);
        Assert.Equal(0, events);
    }

    [Fact]
    public void BurstOfPokes_OneEventAfterSettle()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var seen = new List<NetworkSnapshot?>();
        watcher.Changed += seen.Add;
        watcher.Start();
        identity.Value = Net("aa:bb:cc:09:09:09");
        watcher.Poke();
        time.Advance(TimeSpan.FromSeconds(2));
        watcher.Poke();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(seen);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([identity.Value], seen);
    }

    [Fact]
    public void SameNetwork_NoEvent()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var events = 0;
        watcher.Changed += _ => events++;
        watcher.Start();
        watcher.Poke();
        time.Advance(NetworkWatcher.Settle);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Poll_CatchesChangeWithoutEvent()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var seen = new List<NetworkSnapshot?>();
        watcher.Changed += seen.Add;
        watcher.Start();
        identity.Value = null;
        time.Advance(NetworkWatcher.PollInterval);
        time.Advance(NetworkWatcher.Settle);
        Assert.Equal([null], seen);
    }
}
