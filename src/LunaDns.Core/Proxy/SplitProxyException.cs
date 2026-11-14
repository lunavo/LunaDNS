namespace LunaDns.Core.Proxy;

/// <summary>Proxy không bật được. <see cref="Error"/> để giao diện (KH5) chọn thông báo.</summary>
public sealed class SplitProxyException : Exception
{
    public SplitProxyException()
    {
    }

    public SplitProxyException(string message) : base(message)
    {
    }

    public SplitProxyException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public SplitProxyException(ProxyStartError error, string message, Exception innerException) : base(message, innerException) =>
        Error = error;

    public ProxyStartError Error { get; } = ProxyStartError.Other;
}
