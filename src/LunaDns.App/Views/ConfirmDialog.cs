using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Views;

/// <summary>
/// Hộp thoại xác nhận/thông báo dựng bằng code (không cần thêm XAML). Đóng bằng X thì trả false.
/// <paramref name="destructive"/>: tiêu đề in đậm trong thân, nút huỷ đứng trước và là mặc định, nút xác nhận màu Brush.Error
/// (vd. "Bỏ các thay đổi chưa lưu?", mockup 2.5).
/// </summary>
internal sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText, string? cancelText, bool destructive = false)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var confirm = new Button { Content = confirmText, IsDefault = !destructive };
        if (destructive)
        {
            confirm.Bind(BackgroundProperty, confirm.GetResourceObservable("Brush.Error"));
            confirm.Bind(ForegroundProperty, confirm.GetResourceObservable("Brush.Window"));
            confirm.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            confirm.Classes.Add("accent");
        }
        confirm.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(confirm);
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true, IsDefault = destructive };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Insert(destructive ? 0 : 1, cancel);
        }

        var root = new StackPanel { Margin = new Thickness(20), Spacing = destructive ? 12 : 16 };
        if (destructive) root.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        if (destructive) body.Classes.Add("secondary");
        root.Children.Add(body);
        root.Children.Add(buttons);
        Content = root;
    }
}

/// <summary>Hiện cửa sổ chính trước (có thể đang ở khay) để làm owner cho hộp thoại modal.</summary>
internal sealed class DialogPrompts(Func<Window> showOwner) : IUserPrompts
{
    public Task<bool> ConfirmAsync(string title, string message) =>
        new ConfirmDialog(title, message, Strings.ConfirmYes, Strings.ConfirmNo).ShowDialog<bool>(showOwner());

    public Task InformAsync(string title, string message) =>
        new ConfirmDialog(title, message, Strings.ConfirmYes, cancelText: null).ShowDialog<bool>(showOwner());

    public Task ShowDomainListAsync(string title, IReadOnlyList<string> domains) =>
        new DomainListWindow(title, domains).ShowDialog(showOwner());

    public Task<bool> EditCustomDomainsAsync(CustomDomainsViewModel vm) =>
        new CustomDomainsWindow { DataContext = vm }.ShowDialog<bool>(showOwner());
}
