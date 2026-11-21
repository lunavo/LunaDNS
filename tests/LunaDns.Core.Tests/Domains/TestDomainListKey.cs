using System.Security.Cryptography;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

/// <summary>Cặp khoá P-256 sinh một lần cho cả lượt test; không liên quan tới khoá thật của PO.</summary>
internal static class TestDomainListKey
{
    public const string KeyId = "test";

    public static ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static IReadOnlyDictionary<string, string> Keys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal) { [KeyId] = DomainListSigning.PublicKey(Key) };

    public static byte[] Signed(long serial, Dictionary<string, List<string>> groups, DateTimeOffset? issued = null) =>
        DomainListSigning.Sign(new DomainListSource(groups), serial, issued ?? new DateTimeOffset(2026, 11, 21, 14, 2, 0, TimeSpan.Zero), Key, KeyId);

    /// <summary>Envelope tự dựng, để test các trường hỏng mà Sign không bao giờ sinh ra.</summary>
    public static byte[] Envelope(string payloadJson, string keyId = KeyId, byte[]? signature = null)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(payloadJson);
        var sig = signature ?? Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return System.Text.Encoding.UTF8.GetBytes(
            $$"""{"keyId":"{{keyId}}","payload":"{{Convert.ToBase64String(payload)}}","sig":"{{Convert.ToBase64String(sig)}}"}""");
    }
}
