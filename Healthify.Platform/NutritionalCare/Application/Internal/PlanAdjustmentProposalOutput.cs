namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>IA-8. What the model answers (schema of <c>plan-adjustment-proposal@n.md</c>).</summary>
public sealed record PlanAdjustmentProposalOutput(
    string Title,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<string> AddedGuidelines,
    IReadOnlyList<string> RemovedGuidelines,
    string PatientMessage,
    int RecheckAfterDays,
    string Rationale,
    string? PractitionerLanguage = null,
    string? PatientLanguage = null);
