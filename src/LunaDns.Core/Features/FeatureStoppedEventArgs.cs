namespace LunaDns.Core.Features;

/// <summary>Watchdog đã bỏ cuộc và tắt tính năng (spec 6).</summary>
public sealed class FeatureStoppedEventArgs(Feature feature, bool changesLeftBehind) : EventArgs
{
    public Feature Feature { get; } = feature;

    public bool ChangesLeftBehind { get; } = changesLeftBehind;
}
