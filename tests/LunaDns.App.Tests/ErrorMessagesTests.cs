using LunaDns.Core.Features;

namespace LunaDns.App.Tests;

public sealed class ErrorMessagesTests
{
    [Theory]
    [InlineData(FeatureOutcome.Enabled)]
    [InlineData(FeatureOutcome.Disabled)]
    [InlineData(FeatureOutcome.Unchanged)]
    [InlineData(FeatureOutcome.NeedsProxyOverride)]
    public void Non_failures_have_no_message(FeatureOutcome outcome) =>
        Assert.Null(ErrorMessages.For(new FeatureResult(Feature.SniProxy, outcome)));

    [Fact]
    public void Failure_names_feature_and_error()
    {
        var message = ErrorMessages.For(new FeatureResult(Feature.PubgMode, FeatureOutcome.Failed, FeatureError.PubgPortBusy));

        Assert.NotNull(message);
        Assert.Contains(Strings.FeatureTitle(Feature.PubgMode), message, StringComparison.Ordinal);
        Assert.Contains(Strings.Error(FeatureError.PubgPortBusy), message, StringComparison.Ordinal);
        Assert.DoesNotContain(Strings.ChangesLeftBehind, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_left_behind_are_mentioned()
    {
        var message = ErrorMessages.For(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true));

        Assert.EndsWith(Strings.ChangesLeftBehind, message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_domains_has_a_vietnamese_message() =>
        Assert.Contains("Chưa có tên miền", ErrorMessages.For(FeatureError.NoDomains), StringComparison.Ordinal);

    [Fact]
    public void Feature_error_text_is_the_shared_error_string()
    {
        foreach (var error in Enum.GetValues<FeatureError>().Where(e => e != FeatureError.None))
            Assert.Equal(Strings.Error(error), ErrorMessages.For(error));
    }

    [Fact]
    public void Other_error_uses_the_detail_when_there_is_one()
    {
        var result = new FeatureResult(Feature.SniProxy, FeatureOutcome.Failed, FeatureError.Other, Detail: "networksetup -setautoproxyurl lỗi: ** Error");

        Assert.Equal("networksetup -setautoproxyurl lỗi: ** Error", ErrorMessages.Reason(result));
        Assert.Contains("networksetup -setautoproxyurl lỗi", ErrorMessages.For(result), StringComparison.Ordinal);
        Assert.Equal(Strings.Error(FeatureError.Other), ErrorMessages.Reason(result with { Detail = null }));
        Assert.Equal(Strings.Error(FeatureError.Other), ErrorMessages.Reason(result with { Detail = "  " }));
    }

    [Fact]
    public void Specific_errors_keep_the_app_text_even_with_a_detail()
    {
        var result = new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsRouteConflict, Detail: "câu của dịch vụ nền");

        Assert.Equal(Strings.Error(FeatureError.DnsRouteConflict), ErrorMessages.Reason(result));
    }
}
