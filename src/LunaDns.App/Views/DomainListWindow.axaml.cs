using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LunaDns.App.Views;

/// <summary>Cửa sổ chỉ đọc liệt kê tên miền của một nhóm dựng sẵn (mockup 3.1).</summary>
internal sealed partial class DomainListWindow : Window
{
    public DomainListWindow() => InitializeComponent();

    public DomainListWindow(string title, IReadOnlyList<string> domains) : this()
    {
        ArgumentNullException.ThrowIfNull(domains);
        Title = Strings.Format(nameof(Strings.GroupListTitleFormat), title);
        Header.Text = Strings.Format(nameof(Strings.GroupListHeaderFormat), title, domains.Count);
        Domains.ItemsSource = domains;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
