using System.Collections.ObjectModel;
using Avalonia.Threading;
using LunaDns.App.Features;
using LunaDns.App.Updates;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Màn hình chính (spec 4.8, KH6 3.1). Mọi lệnh gọi IFeatureController qua Task.Run vì Configurator (Windows) chặn luồng.
/// FeatureStopped (luồng nền của watchdog) được chuyển về luồng UI bằng Dispatcher.UIThread.Post.
/// Timer 2 giây hỏi lại trạng thái (macOS: hỏi dịch vụ nền, nên bắt được cả "lunadns on/off" từ Terminal).
/// Overall (huy hiệu header, icon khay) tính lại mỗi khi trạng thái, việc đang chạy hoặc thông báo đổi.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    /// <summary>Tự kiểm cập nhật sau khi khởi động xong, để không tranh mạng với lần kiểm kết nối đầu.</summary>
    private static readonly TimeSpan UpdateStartupDelay = TimeSpan.FromSeconds(10);

    private readonly AppServices _services;
    private readonly IUserPrompts _prompts;
    private readonly Maintenance _maintenance;
    private readonly Func<Task> _quit;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly Func<Task<ServiceInstallResult>>? _installService;
    private (bool Dns, bool Proxy, ControllerAvailability Availability) _shown;
    private bool _polling;
    private ProbeSummary? _summary;
    private string _probeStatus = Strings.NotCheckedYet;
    private string _probeHint = string.Empty;
    private string _message = string.Empty;
    private Tone _messageTone;
    private bool _canApplyRecommended;
    private bool _anyOn;
    private int _busyCount;
    private OverallState _overall;
    private DomainSelection _domains = new();
    private bool _applyingDomains;
    private bool _daemonNeedsUpdate;
    private bool _showProbeCustomHint;

    /// <param name="installService">macOS: cài dịch vụ nền (osascript). null trên Windows.</param>
    public MainViewModel(AppServices services, IUserPrompts prompts, ISystemShell shell, Func<Task> quit,
        Func<Task<ServiceInstallResult>>? installService = null)
    {
        _installService = installService;
        _services = services;
        _prompts = prompts;
        _maintenance = new Maintenance(prompts, shell);
        UpdateBanner = new UpdateBannerViewModel(shell, AppInfo.Version, OnError);
        _quit = quit;
        _logger = services.LoggerFactory.CreateLogger<MainViewModel>();
        // KH6 U0: PUBG ra khỏi v1, giao diện chỉ còn hai công tắc.
        Features =
        [
            new FeatureViewModel(Feature.DnsRouting, ToggleAsync, OnError),
            new FeatureViewModel(Feature.SniProxy, ToggleAsync, OnError),
        ];
        CheckCommand = new AsyncCommand(CheckAsync, OnError, () => !_isTuning);
        ApplyRecommendedCommand = new AsyncCommand(ApplyRecommendedAsync, OnError, () => !_isTuning);
        DisableAllCommand = new AsyncCommand(DisableAllAsync, OnError, () => _anyOn || HasAttention);
        DiagnosticsCommand = new AsyncCommand(DiagnosticsAsync, OnError);
        CheckUpdateCommand = new AsyncCommand(CheckUpdateAsync, OnError);
        UninstallAppCommand = new AsyncCommand(UninstallAppAsync, OnError);
        DismissMessageCommand = new AsyncCommand(DismissMessageAsync, OnError);
        QuitCommand = new AsyncCommand(quit, OnError);
        InstallServiceCommand = new AsyncCommand(InstallServiceAsync, OnError);
        MeasureDohCommand = new AsyncCommand(MeasureDohAsync, OnError, () => !_isSavingCustomDoh && !_isTuning);
        // Không dò khi việc khác đang chạy: Đo/Lưu Tự nhập/thẻ/Kiểm tra có thể ghi settings giữa lúc áp cấu hình dò được.
        TuneCommand = new AsyncCommand(TuneAsync, OnError, () => !_isTuning && !_isMeasuringDoh && !_isSavingCustomDoh && _busyCount == 0);
        StopTuneCommand = new AsyncCommand(StopTuneAsync, OnError, () => _isTuning && !_applyingTune);
        SaveCustomDohCommand = new AsyncCommand(SaveCustomDohAsync, OnError, () => !_isMeasuringDoh);
        _customDohUrl = _services.Settings.CustomDoh?.Endpoint ?? string.Empty;
        _customDohAddresses = string.Join(", ", _services.Settings.CustomDoh?.ManualAddresses ?? []);
        RefreshCustomDohStatus();
        _services.Features.FeatureStopped += OnFeatureStopped;
        _timer = new DispatcherTimer(StatusInterval, DispatcherPriority.Background, async (_, _) => await PollAsync());
        _overall = OverallState.From(_services.Features.Status, busy: false, attention: false);
        Refresh();
    }

    public IReadOnlyList<FeatureViewModel> Features { get; }

    public ObservableCollection<ProbeLineViewModel> ProbeLines { get; } = [];

    /// <summary>Khu "Tên miền" (spec KH8 5.1): ba nhóm dựng sẵn rồi Tuỳ chỉnh.</summary>
    public ObservableCollection<DomainGroupViewModel> DomainGroups { get; } = [];

    public string RoutedCountText => Strings.Format(nameof(Strings.DomainsRoutedCountFormat), FeatureDomains.Routed(_domains).Count);

    /// <summary>Huy hiệu "MỚI" và viền vàng cho tới lần đầu bấm Xem/Sửa hoặc bật/tắt một nhóm.</summary>
    public bool ShowDomainsIntro => !_services.Settings.DomainsIntroSeen;

    /// <summary>Dưới kết quả Kiểm tra: Tuỳ chỉnh chỉ được kiểm vài tên miền đầu (mockup 1.2).</summary>
    public bool ShowProbeCustomHint
    {
        get => _showProbeCustomHint;
        private set => SetProperty(ref _showProbeCustomHint, value);
    }

    public AsyncCommand CheckCommand { get; }

    public AsyncCommand ApplyRecommendedCommand { get; }

    public AsyncCommand DisableAllCommand { get; }

    public AsyncCommand DiagnosticsCommand { get; }

    public AsyncCommand CheckUpdateCommand { get; }

    public AsyncCommand UninstallAppCommand { get; }

    public AsyncCommand DismissMessageCommand { get; }

    public AsyncCommand QuitCommand { get; }

    public AsyncCommand InstallServiceCommand { get; }

    public UpdateBannerViewModel UpdateBanner { get; }

    /// <summary>Chỉ bản Windows do Setup cài có nút gỡ; bản portable và macOS gỡ theo guide.</summary>
    public bool CanUninstall { get; } = PlatformInfo.CanUninstall();

    /// <summary>macOS ẩn mục Nâng cao: dịch vụ nền chưa nhận settings từ giao diện.</summary>
    public bool HasAdvancedSettings { get; } = PlatformInfo.HasAdvancedSettings();

    /// <summary>macOS: banner "dịch vụ nền chưa cài / không trả lời / bản cũ không biết set-domains" (mockup 1.4) kèm nút cài.</summary>
    public bool ShowServiceSetup => _daemonNeedsUpdate
        || _services.Features.Availability is ControllerAvailability.DaemonNotInstalled or ControllerAvailability.DaemonNotResponding;

    public string ServiceSetupText => _services.Features.Availability switch
    {
        ControllerAvailability.DaemonNotResponding => Strings.ServiceNotResponding,
        ControllerAvailability.DaemonNotInstalled => Strings.ServiceNotInstalled,
        _ => Strings.DaemonNeedsUpdate,
    };

    public string InstallServiceText => _services.Features.Availability == ControllerAvailability.DaemonNotResponding
        ? Strings.ReinstallServiceButton
        : Strings.InstallServiceButton;

    public string ProbeStatus
    {
        get => _probeStatus;
        private set => SetProperty(ref _probeStatus, value);
    }

    /// <summary>Một câu dưới các dòng kết quả: "không thấy bị chặn" hoặc gợi ý khi có máy chủ không vào được.</summary>
    public string ProbeHint
    {
        get => _probeHint;
        private set
        {
            if (SetProperty(ref _probeHint, value)) OnPropertyChanged(nameof(HasProbeHint));
        }
    }

    public bool HasProbeHint => _probeHint.Length > 0;

    public bool CanApplyRecommended
    {
        get => _canApplyRecommended;
        private set => SetProperty(ref _canApplyRecommended, value);
    }

    public string Message => _message;

    public Tone MessageTone => _messageTone;

    public bool HasMessage => _message.Length > 0;

    public OverallState Overall
    {
        get => _overall;
        private set => SetProperty(ref _overall, value);
    }

    // Mục Nâng cao: ghi settings.json ngay; dịch vụ đang chạy giữ cấu hình cũ tới lần bật sau.
    public int SplitModeIndex
    {
        get => _services.Settings.SplitMode == SplitMode.MidSni ? 1 : 0;
        set => SaveSettings(s => s with { SplitMode = value == 1 ? SplitMode.MidSni : SplitMode.FirstByte });
    }

    public bool SplitRecords
    {
        get => _services.Settings.SplitRecords;
        set => SaveSettings(s => s with { SplitRecords = value });
    }

    public decimal? SplitDelayMs
    {
        get => _services.Settings.SplitDelayMs;
        set => SaveSettings(s => s with { SplitDelayMs = (int)Math.Clamp(value ?? 0, 0, AppSettings.MaxSplitDelayMs) });
    }

    /// <summary>Thứ tự mục trong combobox "Máy chủ DoH" (D2).</summary>
    private static readonly DohProvider[] DohOrder =
        [DohProvider.Cloudflare, DohProvider.Google, DohProvider.Quad9, DohProvider.AdGuard, DohProvider.Custom];

    private static readonly TimeSpan DohMeasureTimeout = TimeSpan.FromSeconds(30);

    private IReadOnlyDictionary<DohProvider, DohLatency> _dohLatencies = new Dictionary<DohProvider, DohLatency>();
    private IReadOnlyList<string> _dohItems = BuildDohItems(new Dictionary<DohProvider, DohLatency>());
    private bool _isMeasuringDoh;
    private bool _isSavingCustomDoh;
    private string _customDohUrl = string.Empty;
    private string _customDohAddresses = string.Empty;
    private string _customDohStatus = string.Empty;
    private static readonly TimeSpan TuneTimeout = TimeSpan.FromSeconds(60);

    private Action? _cancelTune; // huỷ lượt dò đang chạy; như _cancelDoh, không giữ CTS
    private bool _isTuning;
    private bool _applyingTune;
    private string _tuneStatus = string.Empty;

    public AsyncCommand TuneCommand { get; }

    public AsyncCommand StopTuneCommand { get; }

    public bool IsTuning
    {
        get => _isTuning;
        private set
        {
            if (SetProperty(ref _isTuning, value)) RaiseTuneCommands();
        }
    }

    public string TuneStatus
    {
        get => _tuneStatus;
        private set
        {
            if (SetProperty(ref _tuneStatus, value)) OnPropertyChanged(nameof(HasTuneStatus));
        }
    }

    public bool HasTuneStatus => TuneStatus.Length > 0;

    private Action? _cancelDoh; // huỷ lượt đo đang chạy; không giữ CTS để khỏi phải làm VM thành IDisposable

    public IReadOnlyList<string> DohItems
    {
        get => _dohItems;
        private set => SetProperty(ref _dohItems, value);
    }

    public int DohIndex
    {
        get => Array.IndexOf(DohOrder, _services.Settings.EffectiveDoh());
        set
        {
            // ComboBox đặt -1 khi ItemsSource đổi: bỏ qua, không phải lựa chọn của người dùng.
            if (value < 0 || value >= DohOrder.Length) return;
            SaveSettings(s => s.WithDoh(DohOrder[value]));
        }
    }

    public bool IsCustomDoh => _services.Settings.EffectiveDoh() == DohProvider.Custom;

    public string CustomDohUrl
    {
        get => _customDohUrl;
        set => SetProperty(ref _customDohUrl, value ?? string.Empty);
    }

    public string CustomDohAddresses
    {
        get => _customDohAddresses;
        set => SetProperty(ref _customDohAddresses, value ?? string.Empty);
    }

    public string CustomDohStatus
    {
        get => _customDohStatus;
        private set => SetProperty(ref _customDohStatus, value);
    }

    public string DohMeasureLabel => _isMeasuringDoh ? Strings.DohMeasuring : Strings.DohMeasureButton;

    public AsyncCommand MeasureDohCommand { get; }

    public AsyncCommand SaveCustomDohCommand { get; }

    private static IReadOnlyList<string> BuildDohItems(IReadOnlyDictionary<DohProvider, DohLatency> latencies) =>
        [.. DohOrder.Select(p => latencies.TryGetValue(p, out var l)
            ? l.Median is { } median
                ? Strings.Format(nameof(Strings.DohLatencyFormat), TuneText.DohName(p), (int)Math.Round(median.TotalMilliseconds))
                : Strings.Format(nameof(Strings.DohLatencyFailedFormat), TuneText.DohName(p))
            : TuneText.DohName(p))];

    /// <summary>Dòng trạng thái khối Tự nhập: IP đang dùng, hoặc "đang dùng Cloudflare".</summary>
    private void RefreshCustomDohStatus() =>
        CustomDohStatus = CustomDohParser.ToServer(_services.Settings.CustomDoh) is { } server
            ? Strings.Format(nameof(Strings.DohCustomUsingFormat), string.Join(", ", server.BootstrapAddresses))
            : Strings.DohCustomNotSaved;

    /// <summary>
    /// Vừa chọn lại Tự nhập thì đọc lại dòng trạng thái (có thể cũ sau khi "Đo" chuyển máy chính đi).
    /// Không đổi lúc đang "Lưu" hay khi vẫn ở Tự nhập, để không đè thông báo lỗi vừa hiện.
    /// </summary>
    internal static bool ShouldRefreshCustomDohStatus(AppSettings before, AppSettings after, bool savingCustomDoh) =>
        !savingCustomDoh && before.EffectiveDoh() != DohProvider.Custom && after.EffectiveDoh() == DohProvider.Custom;

    private async Task MeasureDohAsync()
    {
        _isMeasuringDoh = true;
        SaveCustomDohCommand.RaiseCanExecuteChanged();
        TuneCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(DohMeasureLabel));
        // CTS chỉ để Stop() huỷ; hết giờ do meter tự lo, giữ kết quả từng phần.
        using var cts = new CancellationTokenSource();
        _cancelDoh = cts.Cancel;
        try
        {
            var token = cts.Token;
            var measurement = await Task.Run(() => _services.MeasureDohAsync(DohMeasureTimeout, token));
            var results = measurement.Results;
            // Ghi settings chỉ ở luồng UI; bỏ qua nếu URL Tự nhập đã đổi trong lúc đo.
            if (measurement.Resolved is { } resolvedCustom)
                SaveSettings(s => s.CustomDoh?.Endpoint == resolvedCustom.Endpoint ? s with { CustomDoh = resolvedCustom } : s);
            _dohLatencies = results.ToDictionary(r => r.Provider);
            DohItems = BuildDohItems(_dohLatencies);
            if (results.FirstOrDefault(r => r.Median is not null) is { Median: { } median } best)
            {
                SaveSettings(s => s.WithDoh(best.Provider));
                ShowMessage(Strings.Format(nameof(Strings.DohMeasureChosenFormat), TuneText.DohName(best.Provider), (int)Math.Round(median.TotalMilliseconds)), Tone.Good);
            }
            else
            {
                OnPropertyChanged(nameof(DohIndex)); // ItemsSource vừa đổi: combobox cần đọc lại lựa chọn cũ
                ShowMessage(Strings.DohMeasureAllFailed, Tone.Error);
            }
            if (IsCustomDoh) RefreshCustomDohStatus();
        }
        catch (OperationCanceledException)
        {
            ShowMessage(Strings.DohMeasureAllFailed, Tone.Error);
        }
        finally
        {
            _cancelDoh = null;
            _isMeasuringDoh = false;
            SaveCustomDohCommand.RaiseCanExecuteChanged();
            TuneCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(DohMeasureLabel));
        }
    }

    private async Task SaveCustomDohAsync()
    {
        if (!CustomDohParser.TryParseEndpoint(CustomDohUrl, out _))
        {
            CustomDohStatus = Strings.DohCustomInvalidUrl;
            return;
        }
        if (!CustomDohParser.TryParseAddresses(CustomDohAddresses, out var manual))
        {
            CustomDohStatus = Strings.DohCustomInvalidAddresses;
            return;
        }
        var endpoint = CustomDohUrl.Trim();
        var old = _services.Settings.CustomDoh;
        var draft = CustomDohBootstrap.Draft(old, endpoint, [.. manual.Select(a => a.ToString())]);
        CustomDohStatus = Strings.DohCustomResolving;
        _isSavingCustomDoh = true;
        MeasureDohCommand.RaiseCanExecuteChanged();
        TuneCommand.RaiseCanExecuteChanged();
        using var timeout = new CancellationTokenSource(DohMeasureTimeout);
        try
        {
            var resolved = await Task.Run(() => _services.ResolveCustomDohAsync(draft, timeout.Token));
            SaveSettings(s => s with { CustomDoh = resolved });
            RefreshCustomDohStatus();
        }
        catch (Exception ex) when (ex is DohException or OperationCanceledException)
        {
            // URL mới không có IP thì không ghi: máy Tự nhập cũ vẫn dùng tiếp.
            if (CustomDohBootstrap.AfterFailedResolve(old, draft) is { } keep)
                SaveSettings(s => s with { CustomDoh = keep });
            CustomDohStatus = Strings.DohCustomResolveFailed;
        }
        finally
        {
            _isSavingCustomDoh = false;
            MeasureDohCommand.RaiseCanExecuteChanged();
            TuneCommand.RaiseCanExecuteChanged();
        }
    }

    private bool HasAttention => _messageTone == Tone.Error || Features.Any(f => f.HasError);

    /// <summary>Gọi sau khi cửa sổ hiện: báo kết quả hoàn tác lúc khởi động, bật timer, tự chạy "Kiểm tra" (spec 5).</summary>
    public void Start()
    {
        var startup = _services.StartupRollback;
        if (!startup.Success) ShowMessage(Strings.StartupRestoreFailed, Tone.Error);
        else if (startup.Reverted > 0) ShowMessage(Strings.Format(nameof(Strings.StartupRestoredFormat), startup.Reverted), Tone.Info);
        _timer.Start();
        _ = PollAsync();
        _ = LoadDomainsSafeAsync();
        CheckCommand.Execute(null);
        _ = CheckUpdateSilentlyAsync();
    }

    /// <summary>Dựng bốn dòng của khu Tên miền theo lựa chọn của nơi giữ thật (macOS: dịch vụ nền), hỏi không được thì theo settings.json.</summary>
    public async Task LoadDomainsAsync()
    {
        _domains = await Task.Run(_services.GetDomainsAsync) ?? _services.Settings.Domains;
        DomainGroups.Clear();
        foreach (var group in DomainCatalog.Selectable.Append(DomainGroup.Custom))
            DomainGroups.Add(new DomainGroupViewModel(group, () => _domains, ApplyDomainsAsync, ShowDomainError, OpenGroupAsync, OnError));
        if (_summary is not null) ShowGroupProbes(_summary); // "Kiểm tra" lúc mở app có thể xong trước
        OnPropertyChanged(nameof(RoutedCountText));
    }

    /// <summary>Nhóm dựng sẵn: cửa sổ chỉ đọc. Tuỳ chỉnh: hộp thoại sửa; chỉ nút Lưu mới áp.</summary>
    public async Task OpenGroupAsync(DomainGroupViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        MarkDomainsIntroSeen();
        if (!row.IsCustom)
        {
            await _prompts.ShowDomainListAsync(row.Title, DomainCatalog.For(row.Group));
            return;
        }
        using var vm = new CustomDomainsViewModel(_domains, ApplyDomainsAsync, _services.ProbeDomainsAsync, TimeProvider.System, a => Dispatcher.UIThread.Post(a));
        await _prompts.EditCustomDomainsAsync(vm);
        RefreshDomainRows();
    }

    public void ShowUnexpectedError()
    {
        ShowMessage(Strings.UnexpectedError, Tone.Error);
        Refresh();
    }

    /// <summary>Gọi khi thoát: dừng timer, bỏ đăng ký FeatureStopped.</summary>
    public void Stop()
    {
        _timer.Stop();
        _cancelDoh?.Invoke();
        _cancelTune?.Invoke();
        _services.Features.FeatureStopped -= OnFeatureStopped;
    }

    private void RaiseTuneCommands()
    {
        TuneCommand.RaiseCanExecuteChanged();
        StopTuneCommand.RaiseCanExecuteChanged();
        CheckCommand.RaiseCanExecuteChanged();
        ApplyRecommendedCommand.RaiseCanExecuteChanged();
        MeasureDohCommand.RaiseCanExecuteChanged();
    }

    private Task StopTuneAsync()
    {
        _cancelTune?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// "Tìm cấu hình hợp mạng" (D3). Dò ở luồng nền tối đa 60 giây; huỷ (Dừng, đóng cửa sổ, hết giờ) thì không đổi gì.
    /// Có kết quả thì áp ở luồng UI, lúc này không huỷ được nữa.
    /// </summary>
    private async Task TuneAsync()
    {
        using var cts = new CancellationTokenSource(TuneTimeout);
        _cancelTune = cts.Cancel;
        IsTuning = true;
        TuneStatus = Strings.TuneMeasuringDoh;
        try
        {
            // Progress<T> chuyển về luồng UI; bỏ báo muộn sau khi đã sang bước áp.
            var progress = new Progress<TuneProgress>(p =>
            {
                if (_isTuning && !_applyingTune) TuneStatus = TuneText.Progress(p);
            });
            var token = cts.Token;
            TuneRun run;
            try
            {
                run = await Task.Run(() => _services.TuneAsync(progress, token));
            }
            catch (OperationCanceledException)
            {
                TuneStatus = Strings.TuneCancelled;
                return;
            }
            // Dừng (hoặc hết giờ) đúng lúc dò vừa xong mà chưa sang bước áp: vẫn coi là huỷ, không đổi gì.
            if (token.IsCancellationRequested)
            {
                TuneStatus = Strings.TuneCancelled;
                return;
            }
            _applyingTune = true;
            RaiseTuneCommands();
            await ApplyTuneRunAsync(run);
        }
        finally
        {
            _cancelTune = null;
            _applyingTune = false;
            IsTuning = false;
        }
    }

    private Task ApplyTuneRunAsync(TuneRun run) => RunBusyAsync(Features, async () =>
    {
        if (run.ResolvedCustom is { } resolved)
            SaveSettings(s => s.CustomDoh?.Endpoint == resolved.Endpoint ? s with { CustomDoh = resolved } : s);
        if (run.Summary is not null) _summary = run.Summary; // RunBusyAsync gọi ShowSummary khi xong
        var result = run.Result;
        if (result.Outcome == TuneOutcome.DohFailed)
        {
            TuneStatus = Strings.TuneDohFailed;
            return;
        }
        if (result.Outcome == TuneOutcome.NothingWorked)
        {
            TuneStatus = Strings.Format(nameof(Strings.TuneNothingWorkedFormat), string.Join(", ", result.FailingHosts));
            return;
        }
        var doh = result.Doh!.Value;
        var candidate = result.Candidate!;
        var unchanged = run.Current is { } current && current.Doh == doh && current.Candidate == candidate;
        if (!unchanged)
        {
            var applied = await Task.Run(() => _services.ApplyTuningAsync(doh, candidate));
            NotifyTuningSettingsChanged();
            if (!applied.Success)
            {
                TuneStatus = Strings.Format(nameof(Strings.TuneFoundNotAppliedFormat), TuneText.Chosen(doh, candidate),
                    applied.Message ?? Strings.UnexpectedError);
                return;
            }
        }
        foreach (var card in Features) card.SetError(string.Empty);
        var pending = result.Remedies & ~_services.Features.Status.Remedies;
        var failures = new List<string?>();
        if (pending.HasFlag(ProbeRemedies.DnsRouting)) failures.Add(Failure(await EnableAsync(Feature.DnsRouting)));
        if (pending.HasFlag(ProbeRemedies.SniProxy)) failures.Add(Failure(await EnableAsync(Feature.SniProxy)));
        ShowFailures(failures);
        TuneStatus = unchanged ? Strings.TuneNoChange : TuneText.Chosen(doh, candidate);
    });

    private Task ToggleAsync(FeatureViewModel item) => RunBusyAsync([item], async () =>
    {
        item.SetError(string.Empty);
        var feature = item.Feature;
        var result = IsEnabled(_services.Features.Status, feature)
            ? await Task.Run(() => _services.Features.DisableAsync(feature))
            : await EnableAsync(feature);
        ShowFailures([Failure(result)]);
    });

    /// <summary>NeedsProxyOverride: hỏi người dùng rồi bật lại với allowProxyOverride. Bấm Huỷ thì coi như Unchanged.</summary>
    private async Task<FeatureResult> EnableAsync(Feature feature)
    {
        var result = await Task.Run(() => _services.Features.EnableAsync(feature));
        if (result.Outcome != FeatureOutcome.NeedsProxyOverride) return result;
        if (!await _prompts.ConfirmAsync(Strings.ProxyOverrideTitle, Strings.ProxyOverrideText))
            return result with { Outcome = FeatureOutcome.Unchanged };
        return await Task.Run(() => _services.Features.EnableAsync(feature, allowProxyOverride: true));
    }

    private Task CheckAsync() => RunBusyAsync([], async () =>
    {
        ProbeStatus = Strings.CheckingStatus;
        ProbeLines.Clear();
        ProbeHint = string.Empty;
        ShowProbeCustomHint = false;
        CanApplyRecommended = false;
        _summary = null;
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        try
        {
            _summary = await Task.Run(() => _services.ProbeAsync(timeout.Token));
            ShowGroupProbes(_summary);
        }
        catch (OperationCanceledException)
        {
            ProbeStatus = Strings.CheckFailed;
        }
    });

    private void ShowSummary()
    {
        if (_summary is null) return;
        var status = _services.Features.Status;
        ProbeStatus = Strings.Format(nameof(Strings.CheckedAtFormat), _summary.CheckedAt);
        ProbeLines.Clear();
        foreach (var group in _summary.Groups) ProbeLines.Add(ProbeLineViewModel.From(group, status));
        ProbeHint = _summary.AnyUnreachable ? Strings.UnreachableHint
            : _summary.Groups.All(g => g.Worst == ProbeVerdict.Ok) ? Strings.AllOk
            : string.Empty;
        CanApplyRecommended = _summary.PendingSteamRemedies(status) != ProbeRemedies.None;
        ShowProbeCustomHint = _summary.Groups.Any(g => g.Group == DomainGroup.Custom);
    }

    /// <summary>Kết luận nặng nhất của mỗi nhóm lên dòng của nó; dòng không được kiểm (nhóm tắt) xoá kết quả cũ.</summary>
    private void ShowGroupProbes(ProbeSummary summary)
    {
        foreach (var row in DomainGroups)
        {
            var group = summary.Groups.FirstOrDefault(g => g.Group == row.Group);
            if (group is null) row.ShowProbe(null, null);
            else if (group.Group == DomainGroup.Custom && group.Results.Count(r => r.Verdict == ProbeVerdict.Unreachable) is > 0 and var unhelped)
                row.ShowProbe(Strings.Format(nameof(Strings.DomainRowProbeUnhelpedFormat), unhelped), Tone.Error);
            else if (group.Worst == ProbeVerdict.Ok) row.ShowProbe(Strings.DomainRowProbeOk, Tone.Good);
            else row.ShowProbe(DomainProbeText.For(group.Worst), DomainProbeText.ToneFor(group.Worst));
        }
    }

    /// <summary>
    /// Mọi lần áp danh sách (công tắc nhóm và nút Lưu) đi qua đây: khoá hai thẻ trong lúc dịch vụ khởi động lại,
    /// mỗi lúc chỉ một lần áp; thành công thì nhận lựa chọn mới và xoá trạng thái tạm của mọi dòng.
    /// </summary>
    private async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
    {
        MarkDomainsIntroSeen();
        if (_applyingDomains) return new DomainApplyResult(false, null);
        _applyingDomains = true;
        var result = new DomainApplyResult(false, null);
        try
        {
            await RunBusyAsync(Features, async () => result = await Task.Run(() => _services.ApplyDomainsAsync(selection)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ranh giới của công tắc nhóm và nút Lưu: log rồi trả câu chung, hộp thoại/dòng tự hiện câu đó.
            LogCommandFailed(_logger, ex);
            result = new DomainApplyResult(false, Strings.UnexpectedError);
        }
        finally
        {
            _applyingDomains = false;
        }
        if (result.Success)
        {
            _domains = selection;
            SetDaemonNeedsUpdate(false);
            foreach (var row in DomainGroups) row.ClearTemporary();
            RefreshDomainRows();
        }
        else if (result.Message == Strings.DaemonNeedsUpdate)
        {
            SetDaemonNeedsUpdate(true);
        }
        return result;
    }

    /// <summary>Câu lỗi của công tắc nhóm lên thanh thông báo; dịch vụ nền cũ thì đã có banner mời cài lại.</summary>
    private void ShowDomainError(string message)
    {
        if (message != Strings.DaemonNeedsUpdate) ShowMessage(message, Tone.Error);
    }

    private void RefreshDomainRows()
    {
        foreach (var row in DomainGroups) row.Refresh();
        OnPropertyChanged(nameof(RoutedCountText));
    }

    private void MarkDomainsIntroSeen()
    {
        if (_services.Settings.DomainsIntroSeen) return;
        SaveSettings(s => s with { DomainsIntroSeen = true });
        OnPropertyChanged(nameof(ShowDomainsIntro));
    }

    private void SetDaemonNeedsUpdate(bool value)
    {
        if (_daemonNeedsUpdate == value) return;
        _daemonNeedsUpdate = value;
        OnPropertyChanged(nameof(ShowServiceSetup));
        OnPropertyChanged(nameof(ServiceSetupText));
        OnPropertyChanged(nameof(InstallServiceText));
    }

    private async Task LoadDomainsSafeAsync()
    {
        try
        {
            await LoadDomainsAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            LogPollFailed(_logger, ex);
        }
    }

    /// <summary>Chỉ bật biện pháp cho Steam. DNS trước, proxy sau.</summary>
    private Task ApplyRecommendedAsync() => RunBusyAsync(Features, async () =>
    {
        if (_summary is null) return;
        var pending = _summary.PendingSteamRemedies(_services.Features.Status);
        if (pending == ProbeRemedies.None)
        {
            ShowMessage(Strings.NothingToApply, Tone.Info);
            return;
        }
        foreach (var card in Features) card.SetError(string.Empty);
        var failures = new List<string?>();
        if (pending.HasFlag(ProbeRemedies.DnsRouting)) failures.Add(Failure(await EnableAsync(Feature.DnsRouting)));
        if (pending.HasFlag(ProbeRemedies.SniProxy)) failures.Add(Failure(await EnableAsync(Feature.SniProxy)));
        ShowFailures(failures);
    });

    /// <summary>"Tắt hết &amp; khôi phục" (spec 4.8): tắt mọi tính năng, app vẫn chạy.</summary>
    private Task DisableAllAsync() => RunBusyAsync(Features, async () =>
    {
        var result = await Task.Run(_services.Features.DisableAllAsync);
        if (!result.Success)
        {
            ShowMessage(Strings.RestoreFailed, Tone.Error);
            return;
        }
        foreach (var card in Features) card.SetError(string.Empty);
        ShowMessage(Strings.DisableAllDone, Tone.Good);
    });

    private async Task DiagnosticsAsync()
    {
        ShowMessage(Strings.DiagnosticsRunning, Tone.Info);
        try
        {
            var path = await Task.Run(() => ReportWriter.Write(_services.Paths, _services.LastProbe, PlatformInfo.CreateDiagnostics(_services)));
            ShowMessage(Strings.Format(nameof(Strings.ReportSavedFormat), path), Tone.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReportFailed(_logger, ex);
            ShowMessage(Strings.Format(nameof(Strings.ReportFailedFormat), ex.Message), Tone.Error);
        }
    }

    /// <summary>macOS: hộp thoại mật khẩu của hệ thống, rồi hỏi lại dịch vụ nền. Bấm Huỷ thì im lặng.</summary>
    private Task InstallServiceAsync() => RunBusyAsync(Features, async () =>
    {
        if (_installService is null) return;
        var result = await _installService();
        if (result.Success)
        {
            ShowMessage(Strings.ServiceInstalled, Tone.Good);
            SetDaemonNeedsUpdate(false);
        }
        else if (!result.Cancelled)
        {
            ShowMessage(Strings.Format(nameof(Strings.ServiceInstallFailedFormat), result.Output), Tone.Error);
        }
        await Task.Run(() => _services.Features.RefreshAsync());
        if (result.Success) await LoadDomainsAsync();
    });

    private async Task CheckUpdateAsync() =>
        UpdateBanner.Show(await _maintenance.CheckForUpdateAsync(FetchUpdateAsync, AppInfo.Version, CancellationToken.None).ConfigureAwait(true));

    /// <summary>Chạy một lần mỗi lần mở app; CheckSilentlyAsync không ném nên bỏ Task không sợ mất lỗi.</summary>
    private async Task CheckUpdateSilentlyAsync()
    {
        await Task.Delay(UpdateStartupDelay).ConfigureAwait(true);
        if (await Maintenance.CheckSilentlyAsync(FetchUpdateAsync, CancellationToken.None).ConfigureAwait(true) is { } update) UpdateBanner.Show(update);
    }

    private static Task<UpdateCheckResult> FetchUpdateAsync(CancellationToken cancellationToken) =>
        new UpdateChecker(UpdateHttp, AppInfo.Repository, AppInfo.Version).CheckAsync(cancellationToken);

    private async Task UninstallAppAsync()
    {
        var uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (await _maintenance.UninstallAsync(uninstaller).ConfigureAwait(true)) await _quit().ConfigureAwait(true);
    }

    private Task DismissMessageAsync()
    {
        ShowMessage(string.Empty, Tone.Neutral);
        return Task.CompletedTask;
    }

    /// <summary>Khoá thẻ liên quan và đặt Overall = Đang xử lý trong lúc chạy; xong thì làm mới thẻ và kết quả Kiểm tra.</summary>
    private async Task RunBusyAsync(IReadOnlyList<FeatureViewModel> cards, Func<Task> work)
    {
        _busyCount++;
        TuneCommand.RaiseCanExecuteChanged();
        foreach (var card in cards) card.IsBusy = true;
        UpdateOverall();
        try
        {
            await work();
        }
        finally
        {
            foreach (var card in cards) card.IsBusy = false;
            _busyCount--;
            TuneCommand.RaiseCanExecuteChanged();
            Refresh();
            ShowSummary();
        }
    }

    /// <summary>Câu lỗi cho thanh thông báo (null nếu không lỗi); đồng thời ghi lý do ngắn lên thẻ của tính năng.</summary>
    private string? Failure(FeatureResult result)
    {
        var message = ErrorMessages.For(result);
        if (message is not null) Card(result.Feature)?.SetError(Strings.Error(result.Error));
        return message;
    }

    /// <summary>Không có lỗi thì xoá thông báo cũ (như trước KH6); có thì gộp thành một thông báo mức Lỗi.</summary>
    private void ShowFailures(IEnumerable<string?> failures) =>
        ShowMessage(string.Join(" ", failures.OfType<string>()), Tone.Error);

    private FeatureViewModel? Card(Feature feature) => Features.FirstOrDefault(f => f.Feature == feature);

    /// <summary>Chuỗi rỗng luôn hạ mức về Neutral (ẩn thanh thông báo, hết Cần xử lý).</summary>
    private void ShowMessage(string text, Tone tone)
    {
        _message = text;
        _messageTone = text.Length == 0 ? Tone.Neutral : tone;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(MessageTone));
        OnPropertyChanged(nameof(HasMessage));
        UpdateOverall();
    }

    private void SaveSettings(Func<AppSettings, AppSettings> change)
    {
        try
        {
            var before = _services.Settings;
            _services.Settings = change(before);
            if (ShouldRefreshCustomDohStatus(before, _services.Settings, _isSavingCustomDoh)) RefreshCustomDohStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Setter của binding: Avalonia nuốt exception, nên ranh giới phải nằm ở đây.
            LogSettingsSaveFailed(_logger, ex);
            ShowMessage(Strings.UnexpectedError, Tone.Error);
        }
        NotifyTuningSettingsChanged();
    }

    private void NotifyTuningSettingsChanged()
    {
        OnPropertyChanged(nameof(SplitModeIndex));
        OnPropertyChanged(nameof(SplitRecords));
        OnPropertyChanged(nameof(SplitDelayMs));
        OnPropertyChanged(nameof(DohIndex));
        OnPropertyChanged(nameof(IsCustomDoh));
    }

    private void OnFeatureStopped(object? sender, FeatureStoppedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            Card(e.Feature)?.SetError(Strings.FeatureStoppedShort);
            ShowMessage(ErrorMessages.Stopped(e), Tone.Error);
            Refresh();
            ShowSummary();
        });

    private void OnError(Exception ex)
    {
        LogCommandFailed(_logger, ex);
        ShowUnexpectedError();
    }

    private void Refresh()
    {
        var status = _services.Features.Status;
        var availability = _services.Features.Availability;
        var available = availability is ControllerAvailability.Local or ControllerAvailability.DaemonRunning;
        foreach (var feature in Features)
        {
            feature.IsAvailable = available;
            feature.Update(status);
        }
        _anyOn = status.DnsRouting || status.SniProxy || status.PubgMode;
        if (_shown.Availability != availability || _shown == default)
        {
            OnPropertyChanged(nameof(ShowServiceSetup));
            OnPropertyChanged(nameof(ServiceSetupText));
            OnPropertyChanged(nameof(InstallServiceText));
        }
        _shown = (status.DnsRouting, status.SniProxy, availability);
        UpdateOverall(status);
    }

    /// <summary>
    /// Timer: hỏi lại trạng thái (bản Windows không tốn gì). Bật/tắt từ ngoài giao diện (macOS: "lunadns on/off") thì làm mới cả thẻ;
    /// không đổi gì thì chỉ đổi dòng trạng thái như trước. Thẻ đang bật/tắt thì chờ (ToggleSwitch đang chờ kết quả);
    /// "Kiểm tra" đang chạy thì không chờ, để banner dịch vụ nền hiện ngay lúc mở app.
    /// </summary>
    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            await Task.Run(() => _services.Features.RefreshAsync());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            LogPollFailed(_logger, ex);
        }
        finally
        {
            _polling = false;
        }
        var status = _services.Features.Status;
        if (!Features.Any(f => f.IsBusy) && (status.DnsRouting, status.SniProxy, _services.Features.Availability) != _shown)
        {
            Refresh();
            ShowSummary();
        }
        else
        {
            RefreshStatusText();
        }
    }

    private static bool IsEnabled(FeatureStatus status, Feature feature) => feature switch
    {
        Feature.DnsRouting => status.DnsRouting,
        Feature.SniProxy => status.SniProxy,
        _ => status.PubgMode,
    };

    private void UpdateOverall(FeatureStatus? status = null)
    {
        Overall = OverallState.From(status ?? _services.Features.Status, _busyCount > 0, HasAttention);
        DisableAllCommand.RaiseCanExecuteChanged();
    }

    private void RefreshStatusText()
    {
        var status = _services.Features.Status;
        foreach (var feature in Features) feature.UpdateStatusText(status);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lệnh trên giao diện lỗi.")]
    private static partial void LogCommandFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không ghi được báo cáo chẩn đoán.")]
    private static partial void LogReportFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không hỏi được trạng thái.")]
    private static partial void LogPollFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không lưu được settings.json.")]
    private static partial void LogSettingsSaveFailed(ILogger logger, Exception error);
}
