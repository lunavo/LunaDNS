namespace LunaDns.Core.Dns;

/// <summary>Stub không bật được. <see cref="Error"/> để giao diện (KH5) chọn thông báo.</summary>
public sealed class DnsStubException : Exception
{
    public DnsStubException()
    {
    }

    public DnsStubException(string message) : base(message)
    {
    }

    public DnsStubException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DnsStubException(DnsStubError error, string message, Exception innerException) : base(message, innerException) =>
        Error = error;

    public DnsStubError Error { get; } = DnsStubError.Other;
}
