namespace LunaDns.Core.Features;

public sealed record DisableAllResult(IReadOnlyList<FeatureResult> Features, int RemainingEntries)
{
    public bool Success => RemainingEntries == 0;
}
