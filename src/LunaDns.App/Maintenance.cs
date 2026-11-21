using LunaDns.App.Updates;
using LunaDns.App.ViewModels;

namespace LunaDns.App;

/// <summary>Logic của nút "Kiểm tra cập nhật" và tự kiểm khi mở app (spec 4.10, 2026-11-15) và "Gỡ khỏi máy" (KH7 K4), tách khỏi VM để test.</summary>
internal sealed class Maintenance(IUserPrompts prompts, ISystemShell shell)
{
    public async Task<UpdateCheckResult> CheckForUpdateAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, Version current, CancellationToken cancellationToken)
    {
        var result = await check(cancellationToken).ConfigureAwait(true);
        var shown = UpdateChecker.Normalize(current).ToString();
        switch (result.Status)
        {
            case UpdateStatus.NewerAvailable:
                var message = Strings.Format(nameof(Strings.UpdateAvailableFormat), result.Latest, shown);
                if (await prompts.ConfirmAsync(Strings.UpdateAvailableTitle, message).ConfigureAwait(true)) shell.OpenUrl(result.Url!);
                break;
            case UpdateStatus.UpToDate:
                await prompts.InformAsync(Strings.UpToDateTitle, Strings.Format(nameof(Strings.UpToDateFormat), shown)).ConfigureAwait(true);
                break;
            default:
                await prompts.InformAsync(Strings.CheckUpdateButton, Strings.UpdateCheckFailed).ConfigureAwait(true);
                break;
        }

        return result;
    }

    /// <summary>Tự kiểm khi mở app (spec 2026-11-15): không hộp thoại; null khi không có bản mới hay kiểm lỗi.</summary>
    public static async Task<UpdateCheckResult?> CheckSilentlyAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, CancellationToken cancellationToken)
    {
        try
        {
            var result = await check(cancellationToken).ConfigureAwait(true);
            return result.Status == UpdateStatus.NewerAvailable ? result : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>true khi đã mở trình gỡ: bên gọi phải thoát app (hoàn tác) để AppMutex của Setup cho gỡ tiếp.</summary>
    public async Task<bool> UninstallAsync(string uninstallerPath)
    {
        if (!await prompts.ConfirmAsync(Strings.UninstallConfirmTitle, Strings.UninstallConfirmMessage).ConfigureAwait(true)) return false;
        shell.StartProcess(uninstallerPath);
        return true;
    }
}
