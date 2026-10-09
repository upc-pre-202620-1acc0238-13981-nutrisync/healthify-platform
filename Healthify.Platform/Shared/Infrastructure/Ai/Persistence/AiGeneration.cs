using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Persistence;

/// <summary>
///     IA-0. One row of <c>ai_generations</c>: the technical audit of a generation ("the AI wrote this", and what it
///     cost). Not a domain aggregate: no context owns it and no rule lives in it. It never holds the input, only
///     its SHA-256.
/// </summary>
public class AiGeneration
{
    /// <summary>Required by EF Core.</summary>
    protected AiGeneration()
    {
    }

    public AiGeneration(AiGenerationRecord record)
    {
        Feature = record.Feature;
        SubjectPatientId = record.SubjectPatientId;
        RequestedByUserId = record.RequestedByUserId;
        PromptVersion = record.PromptVersion;
        Model = record.Model;
        InputHash = record.InputHash;
        InputImageHash = record.InputImageHash;
        OutputJson = record.OutputJson;
        Status = record.Status.ToString();
        ErrorCode = record.ErrorCode;
        InputTokens = record.InputTokens;
        OutputTokens = record.OutputTokens;
        LatencyMs = record.LatencyMs;
        CreatedAt = record.CreatedAt;
        ExpiresAt = record.ExpiresAt;
    }

    public long Id { get; private set; }
    public string Feature { get; private set; } = null!;

    /// <summary>Whose data it processed. A plain int: no reference to any context.</summary>
    public int SubjectPatientId { get; private set; }

    /// <summary>Who asked; null when a job generated it.</summary>
    public int? RequestedByUserId { get; private set; }

    /// <summary>Null when a guard stopped the generation before the prompt was chosen.</summary>
    public string? PromptVersion { get; private set; }

    public string? Model { get; private set; }

    /// <summary>SHA-256 of the pseudonymized input. Null when stopped before pseudonymization.</summary>
    public string? InputHash { get; private set; }

    /// <summary>IN-7. SHA-256 of the image(s) sent with the input, never the bytes. Null for a text-only generation.</summary>
    public string? InputImageHash { get; private set; }

    /// <summary>The validated output, only when it succeeded.</summary>
    public string? OutputJson { get; private set; }

    public string Status { get; private set; } = null!;
    public string? ErrorCode { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int LatencyMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Retention (§12-#14); the purge job deletes the row once it has passed.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }
}
