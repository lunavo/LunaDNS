using System.Security.Cryptography;
using System.Text.Json;

namespace LunaDns.Core.Domains;

/// <summary>
/// Xác minh domains.json (spec D4, "Định dạng và thứ tự kiểm"). Chữ ký kiểm trên byte trước khi parse payload.
/// Tên miền không qua DomainPolicy chỉ bị bỏ dòng, để app mới (danh sách cấm dài hơn) vẫn đọc được file cũ.
/// Nhóm lạ, pubg, custom bị bỏ qua; othergames được giữ (EffectiveCatalog chỉ áp nhóm Selectable).
/// </summary>
public static class DomainListVerifier
{
    public const int MaxBytes = 65536;
    public const int MaxPerGroup = 200;
    public const int MaxTotal = 1000;
    public const int SupportedFormat = 1;
    public const int SignatureBytes = 64;

    private static readonly Dictionary<string, DomainGroup> Groups = new(StringComparer.Ordinal)
    {
        ["steam"] = DomainGroup.Steam,
        ["othergames"] = DomainGroup.OtherGames,
        ["reddit"] = DomainGroup.Reddit,
    };

    public static string GroupKey(DomainGroup group) =>
        Groups.First(kv => kv.Value == group).Key;

    public static bool TryParseGroup(string key, out DomainGroup group) => Groups.TryGetValue(key, out group);

    public static DomainListVerification Verify(ReadOnlySpan<byte> file, IReadOnlyDictionary<string, string> keys, long highestSerial, bool requireNewer = true)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (file.Length > MaxBytes) return new(DomainListOutcome.TooLarge);

        DomainListEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(file, DomainListJsonContext.Default.DomainListEnvelope);
        }
        catch (JsonException)
        {
            return new(DomainListOutcome.Malformed);
        }
        if (envelope?.KeyId is null || envelope.Payload is null || envelope.Sig is null) return new(DomainListOutcome.Malformed);
        if (!keys.TryGetValue(envelope.KeyId, out var publicKey)) return new(DomainListOutcome.UnknownKey);

        byte[] payload, signature;
        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Sig);
        }
        catch (FormatException)
        {
            return new(DomainListOutcome.Malformed);
        }
        if (signature.Length != SignatureBytes) return new(DomainListOutcome.Malformed);
        if (!SignatureValid(publicKey, payload, signature)) return new(DomainListOutcome.BadSignature);

        DomainListPayload? body;
        try
        {
            body = JsonSerializer.Deserialize(payload, DomainListJsonContext.Default.DomainListPayload);
        }
        catch (JsonException)
        {
            return new(DomainListOutcome.Malformed);
        }
        if (body is null) return new(DomainListOutcome.Malformed);
        if (body.Format != SupportedFormat) return new(DomainListOutcome.UnknownFormat);
        if (body.Serial is not > 0 || body.Issued is null || body.Groups is null || body.Groups.Values.Any(g => g is null))
            return new(DomainListOutcome.Malformed);
        if (requireNewer && body.Serial == highestSerial) return new(DomainListOutcome.NotNewer);
        if (requireNewer && body.Serial < highestSerial) return new(DomainListOutcome.Downgrade);
        if (body.Groups.Values.Any(g => g!.Count > MaxPerGroup) || body.Groups.Values.Sum(g => g!.Count) > MaxTotal)
            return new(DomainListOutcome.Malformed);

        var groups = new Dictionary<DomainGroup, IReadOnlyList<string>>();
        var dropped = 0;
        foreach (var (key, list) in body.Groups)
        {
            if (!Groups.TryGetValue(key, out var group)) continue;
            var builtin = DomainCatalog.For(group);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<string>();
            foreach (var domain in list!)
            {
                var ok = domain is not null
                    && DomainPolicy.Normalize(domain) == domain
                    && DomainPolicy.Validate(domain) == DomainRejection.None
                    && !DomainMatcher.Matches(domain, builtin)
                    && seen.Add(domain);
                if (ok) kept.Add(domain!);
                else dropped++;
            }
            groups[group] = kept;
        }
        return new(DomainListOutcome.Accepted, new DomainListUpdate(body.Serial.Value, body.Issued.Value, groups, file.ToArray()), dropped);
    }

    private static bool SignatureValid(string publicKey, byte[] payload, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
