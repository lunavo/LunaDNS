using System.Text.Json.Serialization;

namespace LunaDns.App.Updates;

/// <summary>Các trường cần của GET /repos/{repo}/releases.</summary>
internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft);

/// <summary>Serializer sinh sẵn như settings.json (KH5 quyết định 16), để không thêm reflection mới.</summary>
[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
