namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>Một luật NRPT như Windows trả về. Name là GUID dạng "{…}" do Windows đặt.</summary>
public sealed record NrptRule(string Name, IReadOnlyList<string> Namespaces, IReadOnlyList<string> NameServers, string? Comment, string? DisplayName);
