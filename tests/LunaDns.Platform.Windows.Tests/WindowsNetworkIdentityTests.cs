using LunaDns.Platform.Windows.Net;

namespace LunaDns.Platform.Windows.Tests;

public class WindowsNetworkIdentityTests
{
    [Fact]
    public void FormatMac_SixBytes() =>
        Assert.Equal("aa:bb:cc:01:02:03", WindowsNetworkIdentity.FormatMac([0xAA, 0xBB, 0xCC, 1, 2, 3]));

    [Fact]
    public void FormatMac_WrongLength_Null() => Assert.Null(WindowsNetworkIdentity.FormatMac([1, 2, 3]));

    [WindowsFact]
    public void Current_OnRealMachine_DoesNotThrow()
    {
        var snapshot = new WindowsNetworkIdentity().Current();
        if (snapshot is not null) Assert.Equal(64, snapshot.Id.Value.Length);
    }
}
