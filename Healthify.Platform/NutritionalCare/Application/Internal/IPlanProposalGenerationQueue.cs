namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>
///     NC-10, option A of the MD: the policy that opens a sustained deviation queues the generation of its proposal
///     instead of waiting for the model, so the signal handler never blocks on a provider. A worker drains it.
/// </summary>
/// <remarks>
///     In memory and per process: a restart loses what was waiting, and those items simply show PR14 without AI
///     (graceful). <see cref="IsPending" /> is what turns <c>GET /plan-proposal</c> into 202 while it is generated.
/// </remarks>
public interface IPlanProposalGenerationQueue
{
    /// <summary>Queues the item; false when it is already waiting or being generated.</summary>
    bool TryEnqueue(int reviewItemId);

    /// <summary>Whether the item is waiting or being generated.</summary>
    bool IsPending(int reviewItemId);

    /// <summary>The next item to generate, waiting until there is one.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>The generation of the item ended, with or without a proposal.</summary>
    void Complete(int reviewItemId);
}
