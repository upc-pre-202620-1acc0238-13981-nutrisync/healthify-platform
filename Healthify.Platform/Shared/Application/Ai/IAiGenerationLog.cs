namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>Outcome of one generation, as <c>ai_generations.status</c> stores it.</summary>
public enum AiGenerationStatus
{
    /// <summary>Validated and returned.</summary>
    Succeeded,

    /// <summary>The provider answered, but the output broke the schema or a rule.</summary>
    Rejected,

    /// <summary>The provider did not answer.</summary>
    Failed,

    /// <summary>
    ///     Stopped after pseudonymization but before reaching the provider (quota, no prompt). The kill switch, the
    ///     feature flag and consent stop a generation before any data is processed and leave no row.
    /// </summary>
    Blocked
}

/// <summary>One row of the technical audit (<c>ai_generations</c>). Never holds the input itself, only its hash.</summary>
/// <remarks>IN-7: <c>InputImageHash</c> is the SHA-256 of the images sent (<see cref="AiImagePart.CombinedHash" />), never the bytes.</remarks>
public record AiGenerationRecord(
    string Feature,
    int SubjectPatientId,
    int? RequestedByUserId,
    string? PromptVersion,
    string? Model,
    string? InputHash,
    string? OutputJson,
    AiGenerationStatus Status,
    string? ErrorCode,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? InputImageHash = null);

/// <summary>
///     IA-0, guard 7. The audit of every generation that processed patient data, also the failed ones: "the AI
///     wrote this", and what it cost.
///     Writes in a unit of work of its own, so recording a generation never commits the caller's pending changes.
/// </summary>
public interface IAiGenerationLog
{
    /// <returns>The identifier of the row (<c>AiGenerationId</c> for traceability).</returns>
    Task<long> RecordAsync(AiGenerationRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Guard 4. Generations of <paramref name="feature" /> since <paramref name="since" /> that reached the
    ///     provider (Blocked rows do not count), for the patient as subject, or for the requester when given.
    /// </summary>
    Task<int> CountSinceAsync(AiFeature feature, int subjectPatientId, int? requestedByUserId, DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>§12-#14. Deletes every generation about the patient, or only those of one feature.</summary>
    /// <returns>Rows deleted.</returns>
    Task<int> PurgeForPatientAsync(int subjectPatientId, AiFeature? feature = null,
        CancellationToken cancellationToken = default);

    /// <summary>Retention: deletes the rows whose <c>expires_at</c> has passed.</summary>
    /// <returns>Rows deleted.</returns>
    Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
