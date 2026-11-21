using Avalonia.Controls;
using Avalonia.Interactivity;
using LunaDns.Core.Domains;

namespace LunaDns.App.Views;

/// <summary>Cửa sổ chỉ đọc liệt kê tên miền của một nhóm dựng sẵn (mockup 3.1).</summary>
internal sealed partial class DomainListWindow : Window
{
    public DomainListWindow() => InitializeComponent();

    public DomainListWindow(string title, IReadOnlyList<CatalogEntry> entries, DateTimeOffset? issued) : this()
    {
        ArgumentNullException.ThrowIfNull(entries);
        Title = Strings.Format(nameof(Strings.GroupListTitleFormat), title);
        Header.Text = Strings.Format(nameof(Strings.GroupListHeaderFormat), title, entries.Count);
        var update = Strings.Format(nameof(Strings.DomainSourceUpdateFormat), issued ?? default);
        Domains.ItemsSource = entries.Select(e => new DomainListRow(e.Domain, e.Source == CatalogSource.Update ? update : Strings.DomainSourceBuiltin)).ToList();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

/// <summary>Một dòng của danh sách: tên miền và nguồn của nó.</summary>
internal sealed record DomainListRow(string Domain, string Source);
