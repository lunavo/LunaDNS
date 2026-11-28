using LunaDns.Core.Features;
using LunaDns.Platform.MacOS.Daemon;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class WatchdogStopsTests
{
    [Fact]
    public void Nothing_recorded_means_no_latest()
    {
        Assert.Null(new WatchdogStops().Latest);
    }

    [Fact]
    public void Each_stop_bumps_the_sequence_and_keeps_only_the_latest()
    {
        var stops = new WatchdogStops();

        stops.Record(new FeatureStoppedEventArgs(Feature.DnsRouting, changesLeftBehind: false));
        Assert.Equal(new DaemonStopped(1, FeatureKeys.DnsRouting, false), stops.Latest);

        stops.Record(new FeatureStoppedEventArgs(Feature.SniProxy, changesLeftBehind: true));
        Assert.Equal(new DaemonStopped(2, FeatureKeys.SniProxy, true), stops.Latest);
    }

    [Fact]
    public async Task Concurrent_stops_each_get_their_own_sequence()
    {
        var stops = new WatchdogStops();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            stops.Record(new FeatureStoppedEventArgs(Feature.DnsRouting, changesLeftBehind: false)))));

        Assert.Equal(50, stops.Latest!.Seq);
    }
}
