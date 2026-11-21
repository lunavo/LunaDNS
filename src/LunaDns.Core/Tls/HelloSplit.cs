namespace LunaDns.Core.Tls;

/// <summary>
/// Cách cắt ClientHello (mục Nâng cao): <see cref="Mode"/> là cắt ở đâu, <see cref="Records"/> là có tách thành hai
/// TLS record không (D1). Tắt Records thì giữ một record và chỉ cắt TCP segment, như spec 4.3.
/// </summary>
public sealed record HelloSplit(SplitMode Mode, bool Records = false)
{
    public static HelloSplit Default { get; } = new(SplitMode.FirstByte);
}
