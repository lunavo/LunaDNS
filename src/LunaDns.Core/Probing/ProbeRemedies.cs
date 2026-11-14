namespace LunaDns.Core.Probing;

/// <summary>Biện pháp đề xuất cho nút "Áp dụng đề xuất" (spec 4.8).</summary>
[Flags]
public enum ProbeRemedies
{
    None = 0,
    DnsRouting = 1,
    SniProxy = 2,
}
