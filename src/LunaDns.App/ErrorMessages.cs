using LunaDns.Core.Features;

namespace LunaDns.App;

/// <summary>Đổi kết quả của FeatureSet thành câu cho người dùng. Giao diện không hiện message của exception trong Core.</summary>
internal static class ErrorMessages
{
    /// <summary>null khi không có gì cần báo. NeedsProxyOverride do hộp thoại xác nhận xử lý, không phải lỗi.</summary>
    public static string? For(FeatureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome != FeatureOutcome.Failed) return null;
        var message = Strings.Format(nameof(Strings.FeatureFailedFormat), Strings.FeatureTitle(result.Feature), Strings.Error(result.Error));
        return result.ChangesLeftBehind ? $"{message} {Strings.ChangesLeftBehind}" : message;
    }

    public static string Stopped(FeatureStoppedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var key = e.ChangesLeftBehind ? nameof(Strings.FeatureStoppedLeftBehindFormat) : nameof(Strings.FeatureStoppedFormat);
        return Strings.Format(key, Strings.FeatureTitle(e.Feature));
    }
}
