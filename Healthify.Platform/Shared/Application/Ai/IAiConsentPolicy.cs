namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0, guard 2: "does this patient allow function X?". Shared only asks; it does not know where consent lives
///     and does not implement this interface. The context that owns consent (CareRelationship, CR-2 and IA-1)
///     implements it: dependency inversion, so the technical module references no context.
/// </summary>
public interface IAiConsentPolicy
{
    /// <summary>
    ///     Whether <paramref name="feature" /> may process the data of <paramref name="patientId" /> now. Asked on
    ///     every generation. Answers false, never throws, when the lookup fails: no answer means no processing.
    /// </summary>
    Task<bool> IsAllowedAsync(int patientId, AiFeature feature, CancellationToken cancellationToken = default);
}
