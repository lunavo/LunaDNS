using System.Globalization;
using LunaDns.App.Features;
using LunaDns.Core.Dns;
using LunaDns.Core.Net;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.NetworkProfiles;

/// <summary>
/// Hồ sơ theo mạng (spec 2026-11-28). Chạy trên luồng UI. Mạng có hồ sơ: áp tổ hợp đã lưu. Mạng mới hoặc hồ sơ cũ (Stale):
/// Kiểm tra ngầm nhóm Steam, đạt thì lưu, bị chặn thì hiện banner. Đang bận thì bỏ lượt, chạy lại khi hết bận.
/// </summary>
internal sealed partial class NetworkProfileCoordinator(NetworkProfileStore store, INetworkProfileHost host, TimeProvider time, ILogger logger)
{
    private CancellationTokenSource? _run;
    private NetworkId? _handled;
    private bool _applied;

    public NetworkSnapshot? Network { get; private set; }

    public bool ShowBlocked { get; private set; }

    public event Action? Changed;

    public string StatusLine
    {
        get
        {
            if (Network is not { } net) return string.Empty;
            var profile = store.Find(net.Id.Value);
            if (profile is null) return Strings.Format(nameof(Strings.NetworkNotSavedFormat), net.Label);
            var date = profile.SavedAt.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);
            return Strings.Format(_applied ? nameof(Strings.NetworkAppliedFormat) : nameof(Strings.NetworkSavedFormat), net.Label, date);
        }
    }

    public static bool Passes(IReadOnlyList<ProbeResult> results, bool sniProxyOn)
    {
        ArgumentNullException.ThrowIfNull(results);
        var steam = results.Where(r => ProbeTargets.Steam.Contains(r.Host, StringComparer.OrdinalIgnoreCase)).ToList();
        return steam.Count > 0 && steam.All(r => r.Verdict == ProbeVerdict.Ok || (sniProxyOn && r.Verdict == ProbeVerdict.SniFiltered));
    }

    public Task OnNetworkChangedAsync(NetworkSnapshot? snapshot)
    {
        _run?.Cancel();
        Network = snapshot;
        _applied = false;
        ShowBlocked = false;
        Changed?.Invoke();
        return HandleAsync();
    }

    public Task OnBusyEndedAsync() => Network?.Id == _handled ? Task.CompletedTask : HandleAsync();

    private async Task HandleAsync()
    {
        if (Network is not { } net || !host.RememberPerNetwork || !(host.Status.DnsRouting || host.Status.SniProxy)) return;
        if (host.IsBusy) return; // OnBusyEndedAsync chạy lại
        _handled = net.Id;
        using var run = new CancellationTokenSource();
        _run = run;
        await host.RunBusyAsync(async () =>
        {
            var profile = store.Find(net.Id.Value);
            if (profile is { Stale: false }) await ApplyAsync(net, profile);
            else await ProbeAndSaveAsync(net, run.Token);
        });
        if (ReferenceEquals(_run, run)) _run = null;
        Changed?.Invoke();
    }

    private async Task ApplyAsync(NetworkSnapshot net, NetworkProfile profile)
    {
        var current = await host.GetTuningAsync();
        Guard(net, () => store.Touch(profile.Id, time.GetUtcNow()));
        if (current is { } c && c.Doh == profile.Doh && c.Candidate == profile.Candidate) return;
        var result = await host.ApplyTuningAsync(profile.Doh, profile.Candidate);
        if (!result.Success)
        {
            LogApplyFailed(logger, net.Id.Short);
            host.ShowError(result.Message ?? Strings.UnexpectedError);
            return;
        }
        _applied = Network?.Id == net.Id;
        LogApplied(logger, net.Id.Short);
    }

    private async Task ProbeAndSaveAsync(NetworkSnapshot net, CancellationToken token)
    {
        IReadOnlyList<ProbeResult> results;
        try
        {
            results = await host.ProbeSteamAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested || Network?.Id != net.Id) return;
        await RecordAsync(net, results);
    }

    public async Task OnCheckCompletedAsync(IReadOnlyList<ProbeResult> results)
    {
        if (Network is not { } net || !host.RememberPerNetwork) return;
        await RecordAsync(net, results);
        Changed?.Invoke();
    }

    private async Task RecordAsync(NetworkSnapshot net, IReadOnlyList<ProbeResult> results)
    {
        if (Passes(results, host.Status.SniProxy))
        {
            if (await host.GetTuningAsync() is { } tuning) Save(net, tuning.Doh, tuning.Candidate);
            ShowBlocked = false;
            LogSaved(logger, net.Id.Short);
            return;
        }
        if (store.Find(net.Id.Value) is not null) Guard(net, () => store.MarkStale(net.Id.Value));
        ShowBlocked = true;
        LogBlocked(logger, net.Id.Short);
    }

    public void OnTuneSucceeded(DohProvider doh, TuneCandidate candidate)
    {
        if (Network is not { } net || !host.RememberPerNetwork) return;
        Save(net, doh, candidate);
        ShowBlocked = false;
        Changed?.Invoke();
    }

    public void Forget()
    {
        if (Network is { } net) Guard(net, () => store.Remove(net.Id.Value));
        _applied = false;
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        Guard(Network, store.Clear);
        _applied = false;
        Changed?.Invoke();
    }

    public void DismissBlocked()
    {
        ShowBlocked = false;
        Changed?.Invoke();
    }

    private void Save(NetworkSnapshot net, DohProvider doh, TuneCandidate candidate)
    {
        var now = time.GetUtcNow();
        Guard(net, () => store.Upsert(new NetworkProfile(net.Id.Value, net.Id.Weak, net.Label, doh, candidate, now, now)));
    }

    /// <summary>Ghi hồ sơ lỗi (đĩa đầy, file bị khoá) không được làm hỏng luồng áp/Kiểm tra: chỉ ghi log, không lộ MAC/IP.</summary>
    private void Guard(NetworkSnapshot? net, Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStoreFailed(logger, net?.Id.Short ?? "-", ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hồ sơ mạng {Network}: không ghi được profiles.json ({Error})")]
    private static partial void LogStoreFailed(ILogger logger, string network, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hồ sơ mạng {Network}: đã áp tổ hợp đã lưu")]
    private static partial void LogApplied(ILogger logger, string network);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hồ sơ mạng {Network}: áp tổ hợp đã lưu lỗi")]
    private static partial void LogApplyFailed(ILogger logger, string network);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hồ sơ mạng {Network}: Kiểm tra đạt, đã lưu")]
    private static partial void LogSaved(ILogger logger, string network);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hồ sơ mạng {Network}: Kiểm tra bị chặn")]
    private static partial void LogBlocked(ILogger logger, string network);
}
