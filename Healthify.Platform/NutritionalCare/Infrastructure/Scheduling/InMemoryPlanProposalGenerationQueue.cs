using System.Collections.Concurrent;
using System.Threading.Channels;
using Healthify.Platform.NutritionalCare.Application.Internal;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;

/// <summary>NC-10. <see cref="IPlanProposalGenerationQueue" /> over an unbounded channel. Singleton.</summary>
public sealed class InMemoryPlanProposalGenerationQueue : IPlanProposalGenerationQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        { SingleReader = true });

    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public bool TryEnqueue(int reviewItemId)
    {
        if (reviewItemId <= 0 || !_pending.TryAdd(reviewItemId, 0)) return false;
        if (_channel.Writer.TryWrite(reviewItemId)) return true;
        _pending.TryRemove(reviewItemId, out _);
        return false;
    }

    public bool IsPending(int reviewItemId)
    {
        return _pending.ContainsKey(reviewItemId);
    }

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    public void Complete(int reviewItemId)
    {
        _pending.TryRemove(reviewItemId, out _);
    }
}
