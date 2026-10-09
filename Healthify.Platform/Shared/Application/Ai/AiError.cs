namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0. The technical failures of an AI generation, shared by every context that runs one. Each context maps
///     them to HTTP in its own ActionResultAssembler (<c>ToAiFailureResult</c>) with the texts of
///     <c>AiMessages</c>. Business failures of a function (not enough data, patient only…) stay in the error enum
///     of the context that owns it.
/// </summary>
public enum AiError
{
    /// <summary>Guard 1: <c>Ai:Enabled</c> or <c>Ai:Features:&lt;Feature&gt;:Enabled</c> is off. 503.</summary>
    AiFeatureDisabled,

    /// <summary>Guard 2: the patient did not consent to AI processing, or turned the function off. 403.</summary>
    AiConsentRequired,

    /// <summary>Guard 4: the daily quota of the function is used up. 429.</summary>
    AiRateLimited,

    /// <summary>Guard 5: the provider failed, timed out (after the one retry) or is not configured. 503.</summary>
    AiProviderUnavailable,

    /// <summary>Guard 6: the output is not valid JSON, breaks the schema or a rule of the function. 502.</summary>
    AiOutputRejected,

    UnexpectedError
}
