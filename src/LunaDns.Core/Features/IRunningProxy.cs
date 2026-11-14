namespace LunaDns.Core.Features;

public interface IRunningProxy : IRunningService
{
    Uri PacUrl { get; }

    int Port { get; }

    int ActiveConnections { get; }
}
