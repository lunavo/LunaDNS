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
        var message = Strings.Format(nameof(Strings.FeatureFailedFormat), Strings.FeatureTitle(result.Feature), For(result.Error));
        return result.ChangesLeftBehind ? $"{message} {Strings.ChangesLeftBehind}" : message;
    }

    /// <summary>Câu cho một mã lỗi (vd. NoDomains khi áp danh sách tên miền), dùng chung với <see cref="For(FeatureResult)"/>.</summary>
    public static string For(FeatureError error) => Strings.Error(error);

    public static string Stopped(FeatureStoppedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var key = e.ChangesLeftBehind ? nameof(Strings.FeatureStoppedLeftBehindFormat) : nameof(Strings.FeatureStoppedFormat);
        return Strings.Format(key, Strings.FeatureTitle(e.Feature));
    }
}
