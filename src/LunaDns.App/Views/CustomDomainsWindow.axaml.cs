using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Views;

/// <summary>
/// Hộp thoại "Tên miền tuỳ chỉnh" (spec KH8 5.2). Logic nằm ở CustomDomainsViewModel; ở đây chỉ nối chuột/bàn phím,
/// chọn file .txt và hỏi lại khi đóng lúc còn thay đổi chưa lưu. Đang bận (kiểm tra/áp) thì không cho đóng.
/// ShowDialog trả true khi Lưu thành công.
/// </summary>
internal sealed partial class CustomDomainsWindow : Window
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly FilePickerFileType TextFiles = new("Text (*.txt)")
    {
        Patterns = ["*.txt"],
        MimeTypes = ["text/plain"],
        AppleUniformTypeIdentifiers = ["public.plain-text"],
    };

    private bool _closeAllowed;
    private bool _asking;

    public CustomDomainsWindow() => InitializeComponent();

    private CustomDomainsViewModel? Vm => DataContext as CustomDomainsViewModel;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_closeAllowed && Vm is { } vm)
        {
            if (vm.IsBusy)
            {
                e.Cancel = true;
            }
            else if (vm.IsDirty)
            {
                e.Cancel = true;
                _ = ConfirmDiscardAsync();
            }
        }
        base.OnClosing(e);
    }

    private async Task ConfirmDiscardAsync()
    {
        if (_asking) return;
        _asking = true;
        try
        {
            var discard = await new ConfirmDialog(Strings.CustomDiscardQuestion, Strings.CustomDiscardBody,
                Strings.CustomDiscardConfirm, Strings.CustomDiscardKeepEditing, destructive: true).ShowDialog<bool>(this);
            if (!discard) return;
            _closeAllowed = true;
            Close(false);
        }
        finally
        {
            _asking = false;
        }
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => Vm?.Add();

    private void OnUndo(object? sender, RoutedEventArgs e) => Vm?.Undo();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CustomDomainRowViewModel row }) Vm?.Delete(row);
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        // SaveAsync tự chặn gọi chồng và tự đổi exception thành ErrorMessage, nên async void ở đây không ném ra ngoài.
        if (Vm is not { } vm || !await vm.SaveAsync()) return;
        _closeAllowed = true;
        Close(true);
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: CustomDomainRowViewModel row, Parent: Control panel }) return;
        row.IsEditing = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (panel.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is not { } box) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: CustomDomainRowViewModel row } || Vm is not { } vm) return;
        if (e.Key == Key.Enter)
        {
            vm.CommitEdit(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.CancelEdit();
            e.Handled = true;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var selected = List.SelectedItems;
        foreach (var row in vm.Rows) row.IsSelected = selected?.Contains(row) == true;
    }

    private async void OnProbeSelected(object? sender, RoutedEventArgs e) => await ProbeAsync(selectedOnly: true);

    private async void OnProbeAll(object? sender, RoutedEventArgs e) => await ProbeAsync(selectedOnly: false);

    /// <summary>ProbeAsync tự đổi lỗi mạng/hết giờ thành "Không kiểm tra được" trên từng dòng.</summary>
    private async Task ProbeAsync(bool selectedOnly)
    {
        if (Vm is not { } vm) return;
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        await vm.ProbeAsync(selectedOnly, timeout.Token);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.CustomImport,
                AllowMultiple = false,
                FileTypeFilter = [TextFiles],
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            vm.Import(await reader.ReadToEndAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.ShowFileError(ex.Message);
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Strings.CustomExport,
                DefaultExtension = "txt",
                SuggestedFileName = "lunadns-tuy-chinh.txt",
                FileTypeChoices = [TextFiles],
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(vm.Export());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.ShowFileError(ex.Message);
        }
    }
}
