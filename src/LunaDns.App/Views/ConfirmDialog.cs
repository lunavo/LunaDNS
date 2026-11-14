using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Views;

/// <summary>Hộp thoại xác nhận/thông báo dựng bằng code (không cần thêm XAML). Đóng bằng X thì trả false.</summary>
internal sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText, string? cancelText)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var confirm = new Button { Content = confirmText, IsDefault = true };
        confirm.Classes.Add("accent");
        confirm.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(confirm);
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
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
}
