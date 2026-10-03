using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

/// <summary>IA-8 - Plan Adjustment Proposal (see <see cref="IPlanAdjustmentProposer" />).</summary>
/// <remarks>
///     What leaves for the model is <see cref="PlanAdjustmentInput" /> as numbers and codes, plus the bounds the
///     answer must respect (so a good model rarely wastes a generation) and the guideline catalog. Custom guideline
///     texts ("Otra indicación") are clinical free text and stay here; only codes go.
/// </remarks>
public class PlanAdjustmentProposer(IAiGenerationPipeline pipeline, IPatientMessageLexicon lexicon)
    : IPlanAdjustmentProposer
{
    public Task<Result<AiGenerationOutcome<PlanAdjustmentProposalOutput>, AiError>> ProposePlanAdjustment(
        PlanAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var current = input.CurrentPlan;

        var payload = new
        {
            evidence = new
            {
                averagePercentFromTarget = input.Evidence.AveragePercentFromTarget,
                deviatedDays = input.Evidence.DeviatedDays,
                loggedDaysConsidered = input.Evidence.LoggedDaysConsidered,
                direction = input.Evidence.Direction
            },
            mealSlotsOnDeviatedDays = new
            {
                days = input.MealSlotsOnDeviatedDays.Days,
                breakfast = input.MealSlotsOnDeviatedDays.Breakfast,
                lunch = input.MealSlotsOnDeviatedDays.Lunch,
                dinner = input.MealSlotsOnDeviatedDays.Dinner,
                other = input.MealSlotsOnDeviatedDays.Other
            },
            weightTrend = input.WeightTrend is null
                ? null
                : new
                {
                    slopeKgPerWeek = input.WeightTrend.SlopeKgPerWeek,
                    changeKg = input.WeightTrend.ChangeKg,
                    pointCount = input.WeightTrend.PointCount
                },
            currentPlan = new
            {
                version = current.Version,
                energyKcal = current.EnergyKcal,
                proteinG = current.ProteinG,
                carbG = current.CarbG,
                fatG = current.FatG,
                guidelines = current.GuidelineCodes,
                restrictions = current.RestrictionCodes
            },
            diagnosisCode = input.DiagnosisCode,
            safetyBounds = new
            {
                minEnergyKcal = PlanAdjustmentSafety.MinimumEnergyKcal(current.EnergyKcal, input.CalorieFloorKcal),
                maxEnergyKcal = PlanAdjustmentSafety.MaximumEnergyKcal(current.EnergyKcal),
                macroTolerancePercent = PlanAdjustmentSafety.MacroTolerance * 100m
            },
            guidelineCatalog = Guideline.Codes,
            // X-2: one call, two readers. The title and the rationale for the practitioner, the message for the
            // patient, each in the language of whoever reads it (plan-adjustment-proposal@2).
            languages = new
            {
                practitioner = input.PractitionerReadingLanguage,
                patient = input.PatientLanguage
            }
        };

        return pipeline.GenerateAsync(
            new AiGenerationRequest(AiFeature.PlanAdjustmentProposal, input.PatientId, input.PractitionerId,
                input.PractitionerReadingLanguage, payload),
            new PlanAdjustmentProposalOutputValidator(input, lexicon), cancellationToken);
    }
}
