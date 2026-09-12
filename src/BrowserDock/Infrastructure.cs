using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace BrowserDock;

internal sealed class Deadline : IDisposable
{
    private readonly CancellationTokenSource source;
    public CancellationToken Token => source.Token;
    public Deadline(TimeSpan duration, CancellationToken token)
    {
        source = CancellationTokenSource.CreateLinkedTokenSource(token, CancellationToken.None);
        source.CancelAfter(duration);
    }
    public void Dispose() => source.Dispose();
}
internal sealed class Admission
{
    private readonly object sync = new();
    private bool open;
    private long epoch;
    private int count;
    private TaskCompletionSource<bool> drained = NewSource();
    private static TaskCompletionSource<bool> NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long Epoch { get { lock (sync) return epoch; } }
    public long Open() { lock (sync) { epoch++; open = true; return epoch; } }
    public Task Close()
    {
        lock (sync) { epoch++; open = false; return count == 0 ? Task.CompletedTask : drained.Task; }
    }
    public IDisposable Enter(long expected)
    {
        lock (sync)
        {
            if (!open || epoch != expected) throw new StaleAttachmentException();
            if (count++ == 0) drained = NewSource();
            return new Exit(this);
        }
    }
    private sealed class Exit(Admission owner) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.sync) if (--owner.count == 0) owner.drained.TrySetResult(true);
        }
    }
}
internal sealed class Diagnostics : IAsyncDisposable
{
    private sealed record Entry(int Id, string Message, BrowserState? State, long? Generation, long? Epoch, Guid? OperationId);
    private readonly Channel<Entry> channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });
    private readonly Task worker;
    private long dropped;
    internal int ActiveWorkersForTest => worker.IsCompleted ? 0 : 1;
    public Diagnostics(ILogger? logger)
    {
        worker = Task.Run(async () =>
        {
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    try
                    {
                        var overflow = Interlocked.Exchange(ref dropped, 0);
                        if (overflow > 0) logger?.LogWarning(new EventId(1099), "Dropped {Count} diagnostic events", overflow);
                        logger?.LogInformation(new EventId(item.Id), "{Message}; State={State}; Generation={Generation}; Epoch={Epoch}; OperationId={OperationId}", item.Message, item.State, item.Generation, item.Epoch, item.OperationId);
                    }
                    catch { /* A user logging sink cannot fault lifecycle or the transport. */ }
                }
            }
        });
    }
    public void Write(int id, string message, BrowserState? state = null, long? generation = null, long? epoch = null, Guid? operationId = null)
    { if (!channel.Writer.TryWrite(new(id, message, state, generation, epoch, operationId))) Interlocked.Increment(ref dropped); }
    public async ValueTask DisposeAsync() { channel.Writer.TryComplete(); await worker.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
}
