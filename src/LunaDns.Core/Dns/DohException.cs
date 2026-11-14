namespace LunaDns.Core.Dns;

public sealed class DohException : Exception
{
    public DohException()
    {
    }

    public DohException(string message) : base(message)
    {
    }

    public DohException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
