using System.Collections.Concurrent;

namespace ParentalGuard.UI.Tests;

/// <summary>
/// Mô phỏng UI thread WinUI (DispatcherQueueSynchronizationContext) cho test ViewModel — 1 thread
/// duy nhất bơm mọi continuation được <see cref="Post"/> về. Dùng để bắt lỗi ViewModel dùng
/// <c>ConfigureAwait(false)</c> rồi set property x:Bind / raise event điều hướng từ thread pool
/// (bug 2026-09-30: nút "Tiếp tục" Onboarding xám vĩnh viễn).
/// </summary>
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public int ThreadId { get; private set; }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    /// <summary>Chạy <paramref name="action"/> trên 1 thread riêng có context này, bơm tới khi task xong.</summary>
    public static void Run(Func<SingleThreadSynchronizationContext, Task> action)
    {
        var context = new SingleThreadSynchronizationContext();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            context.ThreadId = Environment.CurrentManagedThreadId;
            SetSynchronizationContext(context);
            Task task = action(context);
            task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
            foreach ((SendOrPostCallback callback, object? state) in context._queue.GetConsumingEnumerable())
            {
                callback(state);
            }

            failure = task.Exception?.GetBaseException();
        });
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("SingleThreadSynchronizationContext.Run did not finish.");
        }

        if (failure is not null)
        {
            throw failure;
        }
    }
}
