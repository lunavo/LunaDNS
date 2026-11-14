using Avalonia.Controls;

namespace LunaDns.App.Views;

internal sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Nút X chỉ thu xuống khay (spec 4.8). Thoát thật là menu khay hoặc đăng xuất (CloseReason khác WindowClosing).</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
