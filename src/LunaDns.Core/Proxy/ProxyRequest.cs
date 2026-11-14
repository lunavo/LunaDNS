namespace LunaDns.Core.Proxy;

/// <summary>Dòng yêu cầu HTTP gửi tới proxy. <see cref="ConnectHost"/> khác null khi và chỉ khi Method là CONNECT.</summary>
public sealed record ProxyRequest(string Method, string Target, string? ConnectHost, int ConnectPort);
