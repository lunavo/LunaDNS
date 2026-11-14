using System.Text.Json;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Tests.Platform;

public class AutoProxySnapshotJsonTests
{
    [Fact]
    public void Journal_written_before_services_field_still_reads()
    {
        const string old = """
            {"UserSid":"S-1-5-21-1","HadPreviousValue":false,"PreviousValue":null,"PreviousWasExpandString":false,"NewValue":"http://127.0.0.1:18080/proxy.pac"}
            """;

        var snapshot = JsonSerializer.Deserialize<AutoProxySnapshot>(old)!;

        Assert.Equal("S-1-5-21-1", snapshot.UserSid);
        Assert.Null(snapshot.Services);
    }

    [Fact]
    public void Services_round_trip()
    {
        var snapshot = new AutoProxySnapshot("", false, null, false, "http://127.0.0.1:18080/proxy.pac",
            [new ServiceAutoProxy("Luna Wifi", "http://127.0.0.1:8099/x.pac", false), new ServiceAutoProxy("Luna Ethernet", null, false)]);

        var back = JsonSerializer.SerializeToElement(snapshot).Deserialize<AutoProxySnapshot>()!;

        Assert.Equal(snapshot.Services, back.Services);
    }
}
