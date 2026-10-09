using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

/// <summary>
///     IN-7. What the AI recognized in a photo of a meal: the dish resolved to the catalog, the estimated portion, how
///     sure it was and up to three alternatives. Kept for a short time (24 hours by default) so the photo log the
///     patient confirms can reference it, then purged.
/// </summary>
/// <remarks>
///     Business rule: Photo Never Stored (IN-7). There is no property here that could hold the image, its name or its
///     metadata; the photo lived in memory for the request that produced this and nowhere else.
///     Business rule: Analysis Is A Proposal (IN-7). An analysis never becomes intake by itself: it creates no diary
///     entry. The patient confirms or adjusts it on PT7, and the entry keeps it as its proposal, beside what the
///     patient said (Proposal Kept Alongside Confirmation).
///     It carries no field about where the food came from: whether the catalog already had it or the AI created it
///     is not something this context knows or shows.
/// </remarks>
public partial class MealPhotoAnalysis
{
    public const int MaximumAlternatives = 3;

    private List<MealPhotoAlternative> _alternatives = [];

    /// <summary>Required by EF Core.</summary>
    protected MealPhotoAnalysis()
    {
    }

    /// <exception cref="ArgumentException">An invariant of the analysis does not hold.</exception>
    public MealPhotoAnalysis(int patientId, int referenceFoodId, decimal estimatedGrams, Confidence confidence,
        IReadOnlyList<MealPhotoAlternative> alternatives, long aiGenerationId, DateTimeOffset expiresAt)
    {
        if (patientId <= 0) throw new ArgumentException("An analysis belongs to a patient.", nameof(patientId));
        if (referenceFoodId <= 0)
            throw new ArgumentException("An analysis resolves to a catalog food.", nameof(referenceFoodId));
        if (estimatedGrams is < MealPhotoPortion.MinimumGrams or > MealPhotoPortion.MaximumGrams)
            throw new ArgumentException("The estimated portion must be between 5 and 2000 grams.",
                nameof(estimatedGrams));
        ArgumentNullException.ThrowIfNull(confidence);
        if (aiGenerationId <= 0)
            throw new ArgumentException("An analysis is traced to its AI generation.", nameof(aiGenerationId));
        if ((alternatives ?? []).Count > MaximumAlternatives)
            throw new ArgumentException($"An analysis has at most {MaximumAlternatives} alternatives.",
                nameof(alternatives));

        Id = Guid.NewGuid();
        PatientId = patientId;
        ReferenceFoodId = referenceFoodId;
        EstimatedGrams = decimal.Round(estimatedGrams, 0);
        // Business rule: Confidence Always Attached (Subflow 4.2), from the analysis on.
        Confidence = confidence.Value;
        _alternatives = (alternatives ?? []).ToList();
        AiGenerationId = aiGenerationId;
        ExpiresAt = expiresAt;
    }

    /// <summary>The <c>analysisId</c> a photo log references. Random, so it cannot be guessed.</summary>
    public Guid Id { get; private set; }

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>The catalog entry the recognized dish resolved to.</summary>
    public int ReferenceFoodId { get; private set; }

    /// <summary>The portion the AI estimated, in grams.</summary>
    public decimal EstimatedGrams { get; private set; }

    /// <summary>How sure the AI was, from 0 to 1.</summary>
    public decimal Confidence { get; private set; }

    public IReadOnlyList<MealPhotoAlternative> Alternatives => _alternatives;

    /// <summary>NOTE: technical field. The <c>ai_generations</c> row of the recognition, for traceability.</summary>
    public long AiGenerationId { get; private set; }

    /// <summary>After this instant the analysis can no longer be logged and the purge deletes it.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool BelongsTo(int patientId)
    {
        return PatientId == patientId;
    }

    public bool IsExpiredAt(DateTimeOffset now)
    {
        return now >= ExpiresAt;
    }

    /// <summary>The proposal a photo entry stores (Subflow 4.2), as the AI made it.</summary>
    public ProposedEstimate ToProposal(DateTimeOffset estimatedAt)
    {
        return new ProposedEstimate(ReferenceFoodId, EstimatedGrams, new Confidence(Confidence), estimatedAt);
    }
}
