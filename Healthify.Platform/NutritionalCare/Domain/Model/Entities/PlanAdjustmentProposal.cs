using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Domain.Model.Entities;

namespace Healthify.Platform.NutritionalCare.Domain.Model.Entities;

/// <summary>
///     NC-10. The plan the AI proposes when a sustained deviation reaches the inbox (PR14.IA "Plan propuesto por IA ·
///     Ajustar la energía y reforzar las cenas · Energía diaria 1 796 → 1 650 kcal…"). Part of the
///     <c>ReviewItem</c> aggregate: it exists only inside one item and is changed only through it.
/// </summary>
/// <remarks>
///     A proposal is a text on a screen, not a plan. Business rule: No Signal Or Algorithm Modifies The Plan Without
///     An Explicit Action Of The Practitioner (NC-10). Attaching one writes this row and nothing else; only the
///     acceptance a practitioner sends creates a version. What the AI proposed is kept as it was, also after it is
///     accepted with edits: the version records what was assigned, this row what was proposed.
/// </remarks>
public class PlanAdjustmentProposal : IAuditableEntity
{
    public const int MaximumTitleLength = 120;
    public const int MaximumPatientMessageLength = 500;
    public const int MaximumRationaleLength = 600;

    /// <summary>NC-10: "RecheckAfterDays (1–30)".</summary>
    public const int MinimumRecheckAfterDays = 1;

    public const int MaximumRecheckAfterDays = 30;

    private List<string> _addedGuidelines = [];
    private List<string> _removedGuidelines = [];

    /// <summary>Required by EF Core.</summary>
    protected PlanAdjustmentProposal()
    {
    }

    internal PlanAdjustmentProposal(
        long aiGenerationId,
        string title,
        decimal proposedEnergyKcal,
        decimal proposedProteinG,
        decimal proposedCarbG,
        decimal proposedFatG,
        IEnumerable<string> addedGuidelines,
        IEnumerable<string> removedGuidelines,
        string patientMessage,
        int recheckAfterDays,
        string rationale,
        DateTimeOffset generatedAt,
        string? practitionerLanguage = null,
        string? patientLanguage = null)
    {
        if (aiGenerationId <= 0)
            throw new ArgumentException("A proposal is traceable to its AI generation.", nameof(aiGenerationId));
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaximumTitleLength)
            throw new ArgumentException($"The title has 1 to {MaximumTitleLength} characters.", nameof(title));
        // Business rule: Macros Coherent With The Energy (NC-10): "deben sumar la energía ±2 %".
        if (!PlanAdjustmentSafety.AreMacrosCoherent(proposedEnergyKcal, proposedProteinG, proposedCarbG,
                proposedFatG))
            throw new ArgumentException("The proposed macros must add up to the proposed energy within 2 %.",
                nameof(proposedEnergyKcal));
        if (recheckAfterDays is < MinimumRecheckAfterDays or > MaximumRecheckAfterDays)
            throw new ArgumentException(
                $"The recheck is {MinimumRecheckAfterDays} to {MaximumRecheckAfterDays} days after the decision.",
                nameof(recheckAfterDays));
        if (string.IsNullOrWhiteSpace(rationale) || rationale.Trim().Length > MaximumRationaleLength)
            throw new ArgumentException($"The rationale has 1 to {MaximumRationaleLength} characters.",
                nameof(rationale));

        var added = addedGuidelines.Select(Guideline.FromCode).Select(g => g.Code!).Distinct().ToList();
        var removed = removedGuidelines.Select(Guideline.FromCode).Select(g => g.Code!).Distinct().ToList();
        if (added.Intersect(removed).Any())
            throw new ArgumentException("A guideline cannot be added and removed at once.", nameof(addedGuidelines));

        AiGenerationId = aiGenerationId;
        Title = title.Trim();
        ProposedEnergyKcal = proposedEnergyKcal;
        ProposedProteinG = proposedProteinG;
        ProposedCarbG = proposedCarbG;
        ProposedFatG = proposedFatG;
        _addedGuidelines = added;
        _removedGuidelines = removed;
        PatientMessage = new PatientFacingMessage(patientMessage).Value;
        RecheckAfterDays = recheckAfterDays;
        Rationale = rationale.Trim();
        GeneratedAt = generatedAt;
        Status = new PlanProposalStatus(PlanProposalStatus.Proposed);
        PractitionerLanguage = LanguageOrNull(practitionerLanguage, nameof(practitionerLanguage));
        PatientLanguage = LanguageOrNull(patientLanguage, nameof(patientLanguage));
    }

    /// <summary>"ProposalId".</summary>
    public int Id { get; private set; }

    /// <summary>The item it belongs to. Same aggregate: one proposal per item at most.</summary>
    public ReviewItemId ReviewItemId { get; private set; } = null!;

    /// <summary>The <c>ai_generations</c> row that produced it. Not exposed to the client.</summary>
    public long AiGenerationId { get; private set; }

    public string Title { get; private set; } = null!;
    public decimal ProposedEnergyKcal { get; private set; }
    public decimal ProposedProteinG { get; private set; }
    public decimal ProposedCarbG { get; private set; }
    public decimal ProposedFatG { get; private set; }

    /// <summary>Guideline catalog codes the proposal adds.</summary>
    public IReadOnlyList<string> AddedGuidelines => _addedGuidelines;

    /// <summary>Guideline catalog codes of the version in force the proposal removes.</summary>
    public IReadOnlyList<string> RemovedGuidelines => _removedGuidelines;

    /// <summary>"Mensaje para Ana", as the AI drafted it. Reaches the patient only if a practitioner assigns it.</summary>
    public string PatientMessage { get; private set; } = null!;

    /// <summary>"Seguimiento: Revisar de nuevo en 7 días".</summary>
    public int RecheckAfterDays { get; private set; }

    /// <summary>"Por qué". Professional information.</summary>
    public string Rationale { get; private set; } = null!;

    public DateTimeOffset GeneratedAt { get; private set; }

    public PlanProposalStatus Status { get; private set; } = null!;

    /// <summary>The version a practitioner assigned from it, or null while it is not accepted.</summary>
    public int? AssignedPlanVersion { get; private set; }

    /// <summary>
    ///     X-2. <c>es</c> or <c>en</c>: the language of <see cref="Title" /> and <see cref="Rationale" />, the
    ///     practitioner's. Null for proposals generated before X-2 (one language, the patient's).
    /// </summary>
    public string? PractitionerLanguage { get; private set; }

    /// <summary>X-2. <c>es</c> or <c>en</c>: the language of <see cref="PatientMessage" />, the patient's. Null before X-2.</summary>
    public string? PatientLanguage { get; private set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsProposed => Status.IsProposed;

    /// <summary>IA-8. A practitioner assigned a version from it: it is part of the clinical record.</summary>
    public bool IsAccepted => AssignedPlanVersion is not null;

    internal void Accept(int planVersion, bool asIs)
    {
        if (!IsProposed) throw new InvalidOperationException("This plan proposal has already been decided.");
        AssignedPlanVersion = planVersion;
        Status = new PlanProposalStatus(asIs ? PlanProposalStatus.AcceptedAsIs : PlanProposalStatus.AcceptedWithEdits);
    }

    internal void Dismiss()
    {
        if (!IsProposed) throw new InvalidOperationException("This plan proposal has already been decided.");
        Status = new PlanProposalStatus(PlanProposalStatus.Dismissed);
    }

    private static string? LanguageOrNull(string? language, string parameterName)
    {
        if (language is null) return null;
        var normalized = language.Trim().ToLowerInvariant();
        return normalized is "es" or "en"
            ? normalized
            : throw new ArgumentException($"'{language}' is not a language of the app. Allowed: es, en.",
                parameterName);
    }
}
