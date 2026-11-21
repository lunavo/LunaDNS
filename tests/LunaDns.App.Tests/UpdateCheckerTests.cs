using System.Net;
using System.Text;
using LunaDns.App.Updates;

namespace LunaDns.App.Tests;

public sealed class UpdateCheckerTests
{
    private const string Repo = "owner/lunadns";

    private static UpdateChecker Checker(string current, HttpStatusCode status, string body, List<HttpRequestMessage>? seen = null) =>
        new(new HttpClient(new StubHandler(status, body, seen)), Repo, Version.Parse(current));

    private static string Release(string tag, bool draft = false, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/{{Repo}}/releases/tag/{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}}}""";

    [Theory]
    [InlineData("v0.9.1", 0, 9, 1)]
    [InlineData("v10.0.0", 10, 0, 0)]
    public void ParseTag_accepts_v_major_minor_patch(string tag, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData("0.9.1")]
    [InlineData("v0.9")]
    [InlineData("v0.9.1-beta")]
    [InlineData("nightly")]
    public void ParseTag_rejects_other_shapes(string tag)
    {
        Assert.Null(UpdateChecker.ParseTag(tag));
    }

    [Fact]
    public async Task Newer_prerelease_counts_and_drafts_are_ignored()
    {
        var body = $"[{Release("v0.9.3", draft: true)},{Release("v0.9.2", prerelease: true)},{Release("v0.9.0")}]";

        var result = await Checker("0.9.0", HttpStatusCode.OK, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.NewerAvailable, result.Status);
        Assert.Equal(new Version(0, 9, 2), result.Latest);
        Assert.Equal($"https://github.com/{Repo}/releases/tag/v0.9.2", result.Url);
    }

    [Fact]
    public async Task Same_version_is_up_to_date()
    {
        var result = await Checker("0.9.0", HttpStatusCode.OK, $"[{Release("v0.9.0")}]").CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Four_part_assembly_version_compares_as_three_parts()
    {
        var result = await Checker("0.9.0.0", HttpStatusCode.OK, $"[{Release("v0.9.0")}]").CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Url_outside_repo_releases_is_rejected()
    {
        var body = """[{"tag_name":"v1.0.0","html_url":"https://evil.example/x","draft":false,"prerelease":false}]""";

        var result = await Checker("0.9.0", HttpStatusCode.OK, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "[]")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Errors_fail_softly(HttpStatusCode status, string body)
    {
        var result = await Checker("0.9.0", status, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"tag_name":"v9.9.9","html_url":"https://github.com/owner/lunadns/releases/tag/v9.9.9","draft":true,"prerelease":false}]""")]
    [InlineData("""[{"tag_name":"nightly","html_url":"https://github.com/owner/lunadns/releases/tag/nightly","draft":false,"prerelease":false}]""")]
    public async Task No_valid_release_means_up_to_date(string body)
    {
        var result = await Checker("0.9.0", HttpStatusCode.OK, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Latest);
        Assert.Null(result.Url);
    }

    [Fact]
    public void Normalize_fills_missing_build_and_drops_revision()
    {
        Assert.Equal(new Version(0, 9, 0), UpdateChecker.Normalize(new Version(0, 9)));
        Assert.Equal(new Version(0, 9, 1), UpdateChecker.Normalize(new Version(0, 9, 1, 7)));
    }

    [Fact]
    public async Task Sends_user_agent_to_releases_endpoint()
    {
        var seen = new List<HttpRequestMessage>();

        await Checker("0.9.0", HttpStatusCode.OK, "[]", seen).CheckAsync(CancellationToken.None);

        var request = Assert.Single(seen);
        Assert.Equal($"https://api.github.com/repos/{Repo}/releases?per_page=20", request.RequestUri!.ToString());
        Assert.Contains("LunaDNS", request.Headers.UserAgent.ToString());
    }

    private sealed class StubHandler(HttpStatusCode status, string body, List<HttpRequestMessage>? seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen?.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
