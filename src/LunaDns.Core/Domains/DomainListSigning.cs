using System.Security.Cryptography;
using System.Text.Json;

namespace LunaDns.Core.Domains;

/// <summary>Ký domains.json (công cụ DomainListSigner và test). Không chạm khoá bí mật nào ngoài khoá được truyền vào.</summary>
public static class DomainListSigning
{
    public static string PublicKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static byte[] Sign(DomainListSource source, long serial, DateTimeOffset issued, ECDsa key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new DomainListPayload(DomainListVerifier.SupportedFormat, serial, issued, source.Groups.ToDictionary(kv => kv.Key, kv => (List<string>?)kv.Value, StringComparer.Ordinal)),
            DomainListJsonContext.Default.DomainListPayload);
        var signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.SerializeToUtf8Bytes(
            new DomainListEnvelope(keyId, Convert.ToBase64String(payload), Convert.ToBase64String(signature)),
            DomainListJsonContext.Default.DomainListEnvelope);
    }
}
