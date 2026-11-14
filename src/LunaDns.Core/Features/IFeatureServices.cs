namespace LunaDns.Core.Features;

/// <summary>Mở dịch vụ nền cho FeatureSet. Bản thật là <see cref="CoreFeatureServices"/>; test dùng bản giả.</summary>
public interface IFeatureServices
{
    /// <summary>Stub DoH trên 127.0.0.1:53 (spec 4.2). Ném DnsStubException nếu không mở được cổng.</summary>
    IRunningService StartDnsStub(IReadOnlyList<string> domains);

    /// <summary>
    /// SplitProxy (spec 4.3). <paramref name="port"/> = null: cổng trống đầu tiên trong 18080–18090; có giá trị: chỉ đúng
    /// cổng đó (watchdog mở lại mà giữ nguyên AutoConfigURL). Ném SplitProxyException.
    /// </summary>
    IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning);

    /// <summary>Listener 127.0.0.1:443 của Chế độ PUBG (spec 4.4). Ném SplitProxyException.</summary>
    IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning);
}
