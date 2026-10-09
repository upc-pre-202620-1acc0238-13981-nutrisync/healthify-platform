namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Subflow 3.7 - Resolve Review Item. The practitioner decides; nothing is automatic.</summary>
public record ResolveReviewItemCommand(
    int ReviewItemId,
    int PractitionerId,
    bool? ResolvedWithAdjustment,
    string? ResolutionNote);
