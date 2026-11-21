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

    /// <summary>Đọc file nguồn data/domain-list.json; null nếu thiếu "groups". Ném JsonException nếu JSON hỏng.</summary>
    public static DomainListSource? ReadSource(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize(json, DomainListJsonContext.Default.DomainListSource);

    /// <summary>
    /// Kiểm data/domain-list.json trước khi ký (CI chạy qua lệnh verify-source). Nghiêm hơn Verify: nhóm lạ, pubg, custom
    /// và mọi dòng mà Verify sẽ bỏ đều là lỗi, để PO không phát hành dòng không bao giờ có hiệu lực.
    /// </summary>
    public static IReadOnlyList<string> CheckSource(ReadOnlySpan<byte> json)
    {
        DomainListSource? source;
        try
        {
            source = ReadSource(json);
        }
        catch (JsonException ex)
        {
            return [$"JSON hỏng: {ex.Message}"];
        }
        if (source?.Groups is null) return ["JSON thiếu \"groups\"."];
        var errors = new List<string>();
        var total = 0;
        foreach (var (key, list) in source.Groups)
        {
            if (!DomainListVerifier.TryParseGroup(key, out var group))
            {
                errors.Add($"{key}: nhóm không nhận lớp 2.");
                continue;
            }
            if (list is null)
            {
                errors.Add($"{key}: danh sách null.");
                continue;
            }
            if (list.Count > DomainListVerifier.MaxPerGroup) errors.Add($"{key}: quá {DomainListVerifier.MaxPerGroup} tên miền.");
            total += list.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var domain in list)
            {
                if (domain is null) errors.Add($"{key}: có phần tử null.");
                else if (DomainPolicy.Normalize(domain) != domain) errors.Add($"{key}: {domain} chưa chuẩn hoá.");
                else if (DomainPolicy.Validate(domain) is var r and not DomainRejection.None) errors.Add($"{key}: {domain} bị từ chối ({r}).");
                else if (DomainMatcher.Matches(domain, DomainCatalog.For(group))) errors.Add($"{key}: {domain} đã có ở lớp 1.");
                else if (!seen.Add(domain)) errors.Add($"{key}: {domain} lặp lại.");
            }
        }
        if (total > DomainListVerifier.MaxTotal) errors.Add($"Quá {DomainListVerifier.MaxTotal} tên miền.");
        return errors;
    }
}
