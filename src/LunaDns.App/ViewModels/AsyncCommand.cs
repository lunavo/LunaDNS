using System.Windows.Input;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Lệnh bất đồng bộ cho một nút bấm: không chạy chồng (CanExecute = false khi đang chạy).
/// Execute là ranh giới lỗi của nút: mọi exception được chuyển cho onError (log + thông báo), không bao giờ nuốt im.
/// </summary>
internal sealed class AsyncCommand(Func<Task> execute, Action<Exception> onError, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
