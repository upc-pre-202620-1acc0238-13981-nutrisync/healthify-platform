using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     Why an AI function of this context did not answer: a business error of the context
///     (<see cref="MonitoringError" />: not enough data, no care link…) or a technical error of the shared AI module
///     (<see cref="Shared.Application.Ai.AiError" />), which the controller maps with
///     <c>MonitoringActionResultAssembler.ToAiFailureResult</c>. Exactly one of the two is set.
/// </summary>
/// <remarks>
///     The command services of the AI functions return <c>Result&lt;T, MonitoringAiFailure&gt;</c>
///     instead of <c>Result&lt;T, MonitoringError&gt;</c>, so the shared <c>AiError</c> keeps its own texts and
///     statuses (as CLAUDE.md asks) without copying its values into the enum of this context.
/// </remarks>
public sealed record MonitoringAiFailure
{
    private MonitoringAiFailure(MonitoringError? error, AiError? aiError)
    {
        Error = error;
        AiError = aiError;
    }

    public MonitoringError? Error { get; }
    public AiError? AiError { get; }

    public static MonitoringAiFailure Of(MonitoringError error)
    {
        return new MonitoringAiFailure(error, null);
    }

    public static MonitoringAiFailure Of(AiError error)
    {
        return new MonitoringAiFailure(null, error);
    }

    public override string ToString()
    {
        return Error?.ToString() ?? AiError?.ToString() ?? "None";
    }
}
