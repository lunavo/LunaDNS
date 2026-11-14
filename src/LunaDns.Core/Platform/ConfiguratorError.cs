namespace LunaDns.Core.Platform;

public enum ConfiguratorError
{
    Other,
    AccessDenied,
    DnsRouteConflict,
    ExistingProxyConfig,
    UserHiveNotLoaded,
    UnexpectedRegistryValue,
    HostsUnsupportedEncoding,
    NrptFailed,
}
