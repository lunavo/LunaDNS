namespace LunaDns.Core.Dns;

/// <summary>Question của gói DNS. <see cref="Name"/> đã qua DomainMatcher.Normalize.</summary>
public sealed record DnsQuestion(string Name, ushort Type, ushort Class);
