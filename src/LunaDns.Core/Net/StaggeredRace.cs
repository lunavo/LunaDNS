using System.Runtime.ExceptionServices;

namespace LunaDns.Core.Net;

/// <summary>Kết quả của <see cref="StaggeredRace"/>. <see cref="Winner"/> = -1 khi mọi lượt đều lỗi.</summary>
internal readonly record struct RaceResult<T>(bool Succeeded, T? Value, int Winner, IReadOnlyList<Exception> Failures);

/// <summary>
/// Chạy đua so le (spec doh-failover, quyết định 1): mở lượt 0; lượt mới nhất chạy quá <c>stagger</c> mà chưa xong thì mở
/// lượt kế, lượt cũ vẫn chạy; một lượt lỗi dự kiến thì mở lượt kế ngay. Lượt thành công đầu tiên thắng, các lượt khác bị huỷ,
/// kết quả đến muộn của chúng đưa cho <c>discard</c>. Bên gọi huỷ: ném OperationCanceledException. Lỗi không dự kiến:
/// huỷ các lượt khác rồi ném lại.
/// </summary>
internal static class StaggeredRace
{
    public static async Task<RaceResult<T>> RunAsync<T>(
        int count,
        TimeSpan stagger,
        TimeProvider time,
        Func<int, CancellationToken, Task<T>> attempt,
        Func<Exception, bool> isExpectedFailure,
        CancellationToken cancellationToken,
        Action<int, Exception>? onFailure = null,
        Action<T>? discard = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(isExpectedFailure);
        cancellationToken.ThrowIfCancellationRequested();

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new Dictionary<Task<T>, int>();
        var failures = new List<Exception>();
        var next = 0;
        Task? timer = null;

        void Launch()
        {
            var index = next++;
            pending.Add(Start(attempt, index, race.Token), index);
            timer = next < count ? Task.Delay(stagger, time, race.Token) : null;
        }

        try
        {
            Launch();
            while (pending.Count > 0)
            {
                var waitFor = new List<Task>(pending.Keys);
                if (timer is not null) waitFor.Add(timer);
                var done = await Task.WhenAny(waitFor).ConfigureAwait(false);
                if (done == timer)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Launch();
                    continue;
                }

                var task = (Task<T>)done;
                var index = pending[task];
                pending.Remove(task);
                if (task.IsCompletedSuccessfully)
                    return new RaceResult<T>(true, task.Result, index, failures);

                var error = task.Exception?.InnerException ?? new TaskCanceledException(task);
                cancellationToken.ThrowIfCancellationRequested();
                if (!isExpectedFailure(error)) ExceptionDispatchInfo.Capture(error).Throw();
                failures.Add(error);
                onFailure?.Invoke(index, error);
                if (next < count) Launch();
            }
            return new RaceResult<T>(false, default, -1, failures);
        }
        finally
        {
            await race.CancelAsync().ConfigureAwait(false);
            foreach (var loser in pending.Keys) Abandon(loser, discard);
        }
    }

    /// <summary>Gói trong async để lỗi ném đồng bộ từ <paramref name="attempt"/> thành task lỗi như mọi lượt khác.</summary>
    private static async Task<T> Start<T>(Func<int, CancellationToken, Task<T>> attempt, int index, CancellationToken token) =>
        await attempt(index, token).ConfigureAwait(false);

    private static void Abandon<T>(Task<T> loser, Action<T>? discard) =>
        _ = loser.ContinueWith(static (t, state) =>
        {
            if (t.IsCompletedSuccessfully) ((Action<T>?)state)?.Invoke(t.Result);
            else _ = t.Exception; // đánh dấu đã quan sát
        }, discard, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
