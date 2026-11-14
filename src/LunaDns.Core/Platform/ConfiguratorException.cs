namespace LunaDns.Core.Platform;

/// <summary>Lỗi khi đổi hoặc hoàn tác cấu hình hệ thống (spec 6). Message viết cho người dùng, tiếng Việt.</summary>
public sealed class ConfiguratorException : Exception
{
    public ConfiguratorException()
    {
    }

    public ConfiguratorException(string message) : base(message)
    {
    }

    public ConfiguratorException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ConfiguratorException(ConfiguratorError error, string message) : base(message) =>
        Error = error;

    public ConfiguratorException(ConfiguratorError error, string message, Exception innerException) : base(message, innerException) =>
        Error = error;

    public ConfiguratorError Error { get; } = ConfiguratorError.Other;
}
