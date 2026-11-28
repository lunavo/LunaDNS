using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LunaDns.App.ViewModels;
using LunaDns.App.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App;

/// <summary>
/// Vòng đời giao diện: cửa sổ chính, khay (dựng bằng code), "Thoát &amp; khôi phục", và đăng xuất/tắt máy.
/// ShutdownMode.OnExplicitShutdown: đóng cửa sổ không thoát app (spec 4.8).
/// </summary>
/// <remarks>IDisposable chỉ để thoả CA1001 (giữ TrayIcon). Avalonia không gọi Dispose; QuitAsync và OnShutdownRequested tự gọi CloseTray.</remarks>
internal sealed partial class App : Application, IDisposable
{
    private static readonly TimeSpan SessionEndBudget = TimeSpan.FromSeconds(4);

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private WindowIcon? _trayOff;
    private WindowIcon? _trayOn;
    private WindowIcon? _trayError;
    private ILogger _logger = NullLogger.Instance;
    private int _shuttingDown;

    public AppServices? Services { get; init; }

    public IAppInstance? Instance { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            _desktop = desktop;
            _logger = Services.LoggerFactory.CreateLogger<App>();
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;

            var prompts = new DialogPrompts(() => ShowWindow());
            _viewModel = new MainViewModel(Services, prompts, new SystemShell(), QuitAsync, ServiceInstaller(), PlatformInfo.CreateNetworkIdentity());
            _window = new MainWindow { DataContext = _viewModel };
            _window.PropertyChanged += OnWindowPropertyChanged;
            desktop.MainWindow = _window;
            desktop.ShutdownRequested += OnShutdownRequested;
            CreateTray();
            Instance?.ListenForShow(() => Dispatcher.UIThread.Post(() => ShowWindow()));
            ListenForDockReopen();

            _window.Show();
            _viewModel.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>macOS: nút "Cài dịch vụ nền" chạy lunadns đi kèm app qua osascript. Không thấy file thì báo lệnh gõ tay.</summary>
    private static Func<Task<Features.ServiceInstallResult>>? ServiceInstaller()
    {
#if MACOS
        return async () => MacOS.DaemonSetup.FindCli(AppContext.BaseDirectory) is { } cli
            ? await new MacOS.DaemonSetup().InstallAsync(cli).ConfigureAwait(true)
            : new Features.ServiceInstallResult(false, false, Strings.ServiceCliMissing);
#else
        return null;
#endif
    }

    /// <summary>macOS: nút X chỉ ẩn cửa sổ, nên bấm icon Dock (applicationShouldHandleReopen) phải hiện lại cửa sổ chính.</summary>
    private void ListenForDockReopen()
    {
        if (this.TryGetFeature<IActivatableLifetime>() is not { } activatable) return;
        activatable.Activated += (_, e) =>
        {
            if (e.Kind == ActivationKind.Reopen && Volatile.Read(ref _shuttingDown) == 0) ShowWindow();
        };
    }

    private void CreateTray()
    {
        var open = new NativeMenuItem(Strings.TrayOpen);
        open.Click += (_, _) => ShowWindow();
        // KH6 D6: Tắt hết ngay từ khay; Command tự xám khi không có gì để tắt.
        var disableAll = new NativeMenuItem(Strings.DisableAllButton) { Command = _viewModel!.DisableAllCommand };
        var quit = new NativeMenuItem(Strings.TrayQuit);
        quit.Click += async (_, _) => await QuitAsync();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(disableAll);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _trayOff = LoadIcon("tray-off");
        _trayOn = LoadIcon("tray-on");
        _trayError = LoadIcon("tray-error");
        _tray = new TrayIcon { Menu = menu, IsVisible = true };
#if MACOS
        // Icon template (đen + alpha): macOS tự tô theo thanh menu sáng/tối, như icon hệ thống.
        MacOSProperties.SetIsTemplateIcon(_tray, true);
#endif
        _tray.Clicked += (_, _) => ShowWindow();
        UpdateTray();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        TrayIcon.SetIcons(this, [_tray]);
    }

    /// <summary>Windows: .ico màu cho khay. macOS: PNG template 36 px (18 pt @2x, build/make-icns.sh) cho thanh menu.</summary>
    private static WindowIcon LoadIcon(string name)
    {
#if MACOS
        using var stream = AssetLoader.Open(new Uri($"avares://LunaDNS/Assets/{name}-mac.png"));
#else
        using var stream = AssetLoader.Open(new Uri($"avares://LunaDNS/Assets/{name}.ico"));
#endif
        return new WindowIcon(stream);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Overall)) UpdateTray();
    }

    /// <summary>Icon khác nhau cả về hình (có nền / không nền / có huy hiệu đỏ), không chỉ màu (KH6 3.4). Đang xử lý dùng lại icon Bật/Tắt.</summary>
    private void UpdateTray()
    {
        if (_tray is null || _viewModel is null) return;
        var overall = _viewModel.Overall;
        _tray.Icon = overall.Kind switch
        {
            OverallKind.Attention => _trayError,
            OverallKind.On => _trayOn,
            OverallKind.Busy when overall.Active.Count > 0 => _trayOn,
            _ => _trayOff,
        };
        _tray.ToolTipText = overall.Tooltip;
    }

    /// <summary>B11: cửa sổ ẩn (nút X thu xuống khay/thanh menu) hoặc thu nhỏ thì hỏi trạng thái thưa hơn.</summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Window window || (e.Property != Visual.IsVisibleProperty && e.Property != Window.WindowStateProperty)) return;
        _viewModel?.SetWindowShown(window.IsVisible && window.WindowState != WindowState.Minimized);
    }

    private MainWindow ShowWindow()
    {
        var window = _window!;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        return window;
    }

    /// <summary>"Thoát &amp; khôi phục": hoàn tác toàn bộ, báo nếu còn mục lỗi, rồi thoát. Thoát luôn hoàn tác Journal (spec 4.8).</summary>
    private async Task QuitAsync()
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) == 1) return;
        var result = await Task.Run(() => Services!.ShutdownAsync(Features.ShutdownReason.UserQuit));
        if (result == Features.ShutdownOutcome.Unknown) await new DialogPrompts(() => ShowWindow()).InformAsync(Strings.AppTitle, Strings.RestoreUnknown);
        else if (result > 0) await new DialogPrompts(() => ShowWindow()).InformAsync(Strings.AppTitle, Strings.RestoreFailed);
        CloseTray();
        _desktop!.Shutdown();
    }

    /// <summary>
    /// Đăng xuất/tắt máy (WM_QUERYENDSESSION): hoàn tác trong tối đa 4 giây (KH5 quyết định 14). Chưa chắc Avalonia 11.3 phát sự kiện
    /// này khi session kết thúc; T8 bước 6 đo bằng các dòng log ở đây. Không kịp thì lần mở sau hoặc --cleanup sẽ hoàn tác.
    /// macOS phát cả cho Cmd+Q và Quit trên Dock: đọc lý do của Apple Event quit để biết là người dùng thoát (xoá wanted) hay hết
    /// phiên (giữ wanted, F1). Phải đọc ngay tại đây, trên luồng chính, trong lúc hệ thống còn đang xử lý yêu cầu thoát.
    /// </summary>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) == 1) return;
        var reason = SessionEnding() ? Features.ShutdownReason.SessionEnd : Features.ShutdownReason.UserQuit;
        LogSessionEnding(_logger, reason);
        var watch = Stopwatch.StartNew();
        try
        {
            var shutdown = Task.Run(() => Services!.ShutdownAsync(reason));
            if (!shutdown.Wait(SessionEndBudget)) LogSessionEndTimedOut(_logger);
            else if (shutdown.Result == Features.ShutdownOutcome.Unknown) LogSessionEndUnknown(_logger);
            else if (shutdown.Result > 0) LogSessionEndLeftovers(_logger, shutdown.Result);
            else LogSessionEndDone(_logger, watch.ElapsedMilliseconds);
        }
        catch (AggregateException ex)
        {
            LogSessionEndFailed(_logger, ex);
        }
        CloseTray();
    }

    /// <summary>Windows: ShutdownRequested chỉ đến từ WM_QUERYENDSESSION. macOS: hỏi lý do của Apple Event quit.</summary>
    private static bool SessionEnding()
    {
#if MACOS
        return Platform.MacOS.AppleEvents.QuitReason.IsSessionEnding();
#else
        return true;
#endif
    }

    public void Dispose() => CloseTray();

    private void CloseTray()
    {
        _viewModel?.Stop();
        _viewModel?.Dispose();
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_tray is null) return;
        _tray.IsVisible = false;
        _tray.Dispose();
        _tray = null;
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ranh giới cuối của luồng UI: log, báo chung chung, giữ app chạy để còn "Thoát & khôi phục".
        LogUnhandled(_logger, e.Exception);
        _viewModel?.ShowUnexpectedError();
        e.Handled = true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nhận ShutdownRequested ({Reason}), bắt đầu hoàn tác.")]
    private static partial void LogSessionEnding(ILogger logger, Features.ShutdownReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hoàn tác khi đăng xuất xong sau {ElapsedMs} ms.")]
    private static partial void LogSessionEndDone(ILogger logger, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hoàn tác khi đăng xuất quá 4 giây; phần còn lại để lần mở sau hoặc --cleanup.")]
    private static partial void LogSessionEndTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Đăng xuất: còn {Count} thay đổi chưa hoàn tác được.")]
    private static partial void LogSessionEndLeftovers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Đăng xuất: dịch vụ nền không trả lời, không biết đã hoàn tác chưa.")]
    private static partial void LogSessionEndUnknown(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Đăng xuất: hoàn tác lỗi.")]
    private static partial void LogSessionEndFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lỗi không bắt trên luồng UI.")]
    private static partial void LogUnhandled(ILogger logger, Exception error);
}
