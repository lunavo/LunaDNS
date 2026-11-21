using LunaDns.App.ViewModels;
using LunaDns.Core.Dns;
using LunaDns.Core.Settings;

namespace LunaDns.App.Tests;

public sealed class CustomDohStatusRefreshTests
{
    private static readonly AppSettings Cloudflare = new AppSettings().WithDoh(DohProvider.Cloudflare);
    private static readonly AppSettings Custom = new AppSettings().WithDoh(DohProvider.Custom);

    [Fact]
    public void Switching_to_custom_refreshes_the_status()
    {
        // "Đo" vừa chuyển máy chính sang Cloudflare, người dùng chọn lại Tự nhập: dòng trạng thái phải đọc lại.
        Assert.True(MainViewModel.ShouldRefreshCustomDohStatus(Cloudflare, Custom, savingCustomDoh: false));
    }

    [Fact]
    public void Staying_on_custom_keeps_the_status()
    {
        // "Lưu" ghi settings khi vẫn ở Tự nhập: không đè thông báo lỗi vừa hiện.
        Assert.False(MainViewModel.ShouldRefreshCustomDohStatus(Custom, Custom, savingCustomDoh: false));
    }

    [Fact]
    public void Switching_to_custom_while_saving_keeps_the_status()
    {
        Assert.False(MainViewModel.ShouldRefreshCustomDohStatus(Cloudflare, Custom, savingCustomDoh: true));
    }

    [Fact]
    public void Leaving_custom_does_not_refresh()
    {
        Assert.False(MainViewModel.ShouldRefreshCustomDohStatus(Custom, Cloudflare, savingCustomDoh: false));
    }
}
