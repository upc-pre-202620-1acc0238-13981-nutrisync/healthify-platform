using System.Collections.Concurrent;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Fakes;

/// <summary>
///     IA-0. A <see cref="ILanguageModelClient" /> that never leaves the process: it answers from a queue of canned
///     outputs or failures and records every request. For tests; Program.cs never registers it, so no test and no
///     environment without a configured provider calls Google.
/// </summary>
public sealed class FakeLanguageModelClient : ILanguageModelClient
{
    public const string FakeModel = "fake-model";

    private readonly ConcurrentQueue<Func<LanguageModelRequest, LanguageModelResponse>> _answers = new();
    private readonly ConcurrentQueue<LanguageModelRequest> _requests = new();

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<LanguageModelRequest> Requests => _requests.ToList();

    public Task<LanguageModelResponse> GenerateStructuredAsync(LanguageModelRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue(request);

        if (!_answers.TryDequeue(out var answer))
            throw new LanguageModelException(LanguageModelFailure.NotConfigured,
                "The fake language model has no answer queued.");

        return Task.FromResult(answer(request));
    }

    /// <summary>Queues an answer with this output text.</summary>
    public FakeLanguageModelClient Answers(string outputJson, int inputTokens = 100, int outputTokens = 50)
    {
        _answers.Enqueue(_ => new LanguageModelResponse(outputJson, FakeModel, inputTokens, outputTokens));
        return this;
    }

    /// <summary>Queues a provider failure.</summary>
    public FakeLanguageModelClient Fails(LanguageModelFailure failure)
    {
        _answers.Enqueue(_ => throw new LanguageModelException(failure, $"Fake failure: {failure}."));
        return this;
    }
}
