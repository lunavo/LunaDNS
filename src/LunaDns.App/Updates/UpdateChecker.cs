using System.Text.Json;

namespace LunaDns.App.Updates;

internal enum UpdateStatus
{
    UpToDate,
    NewerAvailable,
    Failed,
}

internal sealed record UpdateCheckResult(UpdateStatus Status, Version? Latest, string? Url)
{
    public static readonly UpdateCheckResult Failed = new(UpdateStatus.Failed, null, null);
}

/// <summary>
/// Spec 4.10, KH7 K10: lấy tag vX.Y.Z lớn nhất không phải draft (kể cả prerelease) trong 20 Release mới nhất.
/// Chỉ trả URL nằm dưới https://github.com/&lt;repo&gt;/releases/. Lỗi mạng hay dữ liệu lạ đều thành Failed, không ném.
/// Không có release hợp lệ nào thì UpToDate, Latest/Url null.
/// </summary>
internal sealed class UpdateChecker(HttpClient http, string repository, Version current)
{
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        GitHubRelease[] releases;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=20");
            request.Headers.UserAgent.ParseAdd($"LunaDNS/{Normalize(current)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.Failed;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            releases = await JsonSerializer.DeserializeAsync(stream, UpdateJsonContext.Default.GitHubReleaseArray, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return UpdateCheckResult.Failed;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout của HttpClient; huỷ do người gọi thì để OperationCanceledException đi tiếp.
            return UpdateCheckResult.Failed;
        }

        var candidates = releases
            .Where(r => !r.Draft)
            .Select(r => (Version: ParseTag(r.TagName), Release: r))
            .Where(x => x.Version is not null)
            .ToList();
        // Repo chưa có release hợp lệ: bản đang chạy coi là mới nhất. MaxBy trên bộ giá trị (struct) ném khi rỗng, nên kiểm trước.
        if (candidates.Count == 0) return new UpdateCheckResult(UpdateStatus.UpToDate, null, null);
        var latest = candidates.MaxBy(x => x.Version);
        if (!latest.Release.HtmlUrl.StartsWith($"https://github.com/{repository}/releases/", StringComparison.Ordinal)) return UpdateCheckResult.Failed;

        return latest.Version > Normalize(current)
            ? new UpdateCheckResult(UpdateStatus.NewerAvailable, latest.Version, latest.Release.HtmlUrl)
            : new UpdateCheckResult(UpdateStatus.UpToDate, latest.Version, latest.Release.HtmlUrl);
    }

    /// <summary>Chỉ nhận đúng "vX.Y.Z".</summary>
    public static Version? ParseTag(string tag) =>
        tag.Length > 1 && tag[0] == 'v' && tag.Count(c => c == '.') == 2 && Version.TryParse(tag[1..], out var version) ? version : null;

    /// <summary>Phiên bản 3 phần như tag: thiếu Build thì 0, bỏ Revision.</summary>
    internal static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
