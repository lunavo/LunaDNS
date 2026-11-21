namespace LunaDns.Core.Platform;

/// <summary>Một dòng hosts muốn thêm. Address phải là loopback, Host phải thuộc DomainCatalog (kiểm tra ở HostsEditor).</summary>
public sealed record HostsEntry(string Address, string Host);
