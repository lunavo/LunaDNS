using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LunaDns.Core.Tests.Probing;

/// <summary>Máy chủ TLS trên 127.0.0.1 với chứng chỉ tự ký cho một host, để test TlsProbe không cần Internet.</summary>
internal sealed class LoopbackTlsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LoopbackTlsServer(string host)
    {
        Certificate = CreateCertificate(host);
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    public X509Certificate2 Certificate { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Callback kiểm tra chứng chỉ cho test: chỉ tin đúng chứng chỉ của máy chủ này.</summary>
    public bool Trusts(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
        certificate is not null && certificate.GetCertHashString() == Certificate.GetCertHashString();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        Certificate.Dispose();
    }

    private static X509Certificate2 CreateCertificate(string host)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel không dùng được khoá chỉ nằm trong bộ nhớ: xuất PFX rồi nạp lại để khoá có chỗ chứa.
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.UserKeySet);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            using var ssl = new SslStream(client.GetStream());
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, _stop.Token);
                _ = await ssl.ReadAsync(new byte[1], _stop.Token); // giữ kết nối tới khi client đóng
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException)
            {
                // Client từ chối chứng chỉ hoặc đã đóng kết nối: đúng kịch bản của test.
                return;
            }
        }
    }
}
