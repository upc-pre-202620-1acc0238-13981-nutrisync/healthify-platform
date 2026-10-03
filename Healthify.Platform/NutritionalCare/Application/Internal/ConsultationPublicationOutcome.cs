using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;

namespace Healthify.Platform.NutritionalCare.Application.Internal;

/// <summary>NC-7. What step 4 produced: the closed consultation, its published version and its active diagnosis.</summary>
/// <param name="Consultation">The consultation, now completed.</param>
/// <param name="Plan">The version in force.</param>
/// <param name="Diagnosis">The diagnosis of the consultation, now the active one.</param>
/// <param name="Replayed">
///     NC-2. True when the request repeated a publication already done with the same Idempotency-Key: nothing
///     was written or published again.
/// </param>
public record ConsultationPublicationOutcome(
    Consultation Consultation,
    NutritionPlan Plan,
    NutritionalDiagnosis Diagnosis,
    bool Replayed = false);
