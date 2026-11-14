namespace LunaDns.App;

/// <summary>
/// Khoá "chỉ một bản giao diện" (spec 4.5). Lần mở thứ hai báo bản đang chạy hiện cửa sổ rồi thoát.
/// Windows: mutex Global\LunaDNS + sự kiện (SingleInstance). macOS: flock + Unix socket trong thư mục dữ liệu của user (MacAppInstance).
/// </summary>
internal interface IAppInstance : IDisposable
{
    /// <summary>onShow chạy trên luồng nền; bên gọi tự chuyển về luồng UI.</summary>
    void ListenForShow(Action onShow);
}
