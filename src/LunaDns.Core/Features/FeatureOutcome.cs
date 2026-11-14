namespace LunaDns.Core.Features;

public enum FeatureOutcome
{
    Enabled,
    Disabled,
    Unchanged,
    /// <summary>Máy đã có PAC/proxy riêng: hỏi người dùng rồi gọi lại EnableAsync với allowProxyOverride (KH4 quyết định 4).</summary>
    NeedsProxyOverride,
    Failed,
}
