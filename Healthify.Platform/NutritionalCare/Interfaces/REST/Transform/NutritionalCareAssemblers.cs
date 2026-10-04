using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;

// ---------------------------------------------------------------------------------------------
// Resource to Command
// ---------------------------------------------------------------------------------------------

public static class RecordAssessmentCommandAssembler
{
    public static RecordAssessmentCommand ToCommand(int practitionerId, RecordAssessmentResource resource)
    {
        return new RecordAssessmentCommand(
            resource.PatientId, practitionerId, resource.Habits, resource.MedicalHistory,
            resource.PhysicalActivity, resource.Biochemistry, resource.AgeYears, resource.BiologicalSex,
            resource.SupersedesAssessmentId, resource.ActivityLevel,
            resource.EatingHabits is null
                ? null
                : new EatingHabitsDto(resource.EatingHabits.MealsPerDay, resource.EatingHabits.WaterLitersPerDay,
                    resource.EatingHabits.MealsOutPerWeek),
            resource.BiochemistryPanel is null
                ? null
                : new BiochemistryDto(resource.BiochemistryPanel.FastingGlucoseMgDl,
                    resource.BiochemistryPanel.TotalCholesterolMgDl, resource.BiochemistryPanel.TriglyceridesMgDl));
    }
}

public static class TakeClinicalMeasurementCommandAssembler
{
    public static TakeClinicalMeasurementCommand ToCommand(int assessmentId, int practitionerId,
        TakeClinicalMeasurementResource resource)
    {
        return new TakeClinicalMeasurementCommand(assessmentId, practitionerId, resource.WeightKg,
            resource.HeightCm, resource.Protocol, resource.BodyFatPercentage,
            resource.WaistCircumferenceCm, resource.ProtocolChecks);
    }
}

public static class RecordPatientBaselineCommandAssembler
{
    public static RecordPatientBaselineCommand ToCommand(int patientId, int practitionerId,
        RecordPatientBaselineResource resource)
    {
        return new RecordPatientBaselineCommand(patientId, practitionerId, resource.BirthDate,
            resource.BiologicalSex, resource.HeightCm, resource.Conditions ?? []);
    }
}

public static class UpdatePatientBaselineCommandAssembler
{
    public static UpdatePatientBaselineCommand ToCommand(int patientId, int practitionerId,
        UpdatePatientBaselineResource resource)
    {
        return new UpdatePatientBaselineCommand(patientId, practitionerId, resource.BirthDate,
            resource.BiologicalSex, resource.HeightCm, resource.Conditions ?? []);
    }
}

public static class CloseAssessmentCommandAssembler
{
    public static CloseAssessmentCommand ToCommand(int assessmentId, int practitionerId)
    {
        return new CloseAssessmentCommand(assessmentId, practitionerId);
    }
}

public static class IssueDiagnosisCommandAssembler
{
    public static IssueDiagnosisCommand ToCommand(int practitionerId, IssueDiagnosisResource resource)
    {
        return new IssueDiagnosisCommand(resource.PatientId, practitionerId, resource.AssessmentId,
            resource.Statement, resource.Rationale, resource.Code);
    }
}

public static class ProposeTargetsCommandAssembler
{
    public static ProposeTargetsCommand ToCommand(int practitionerId, ProposeTargetsResource resource)
    {
        return new ProposeTargetsCommand(
            resource.PatientId, practitionerId, resource.Equation, resource.ReferenceWeightKind,
            resource.ReferenceWeightKg, resource.ActivityFactor, resource.DeficitKind,
            resource.DeficitValue, resource.ProteinGramsPerKg, resource.FatPercentOfEnergy);
    }
}

public static class PrescribeTargetsCommandAssembler
{
    public static PrescribeTargetsCommand ToCommand(int planId, int practitionerId,
        PrescribeTargetsResource resource)
    {
        return new PrescribeTargetsCommand(planId, practitionerId, resource.Outcome, resource.EnergyKcal,
            resource.ProteinG, resource.CarbG, resource.FatG, resource.OverrideReason);
    }
}

public static class PublishNutritionPlanCommandAssembler
{
    public static PublishNutritionPlanCommand ToCommand(int planId, int practitionerId,
        PublishNutritionPlanResource resource)
    {
        return new PublishNutritionPlanCommand(planId, practitionerId, resource.Guidelines ?? [],
            resource.Restrictions ?? [], resource.CustomGuidelines, resource.PatientMessage);
    }
}

public static class AdjustNutritionPlanCommandAssembler
{
    public static AdjustNutritionPlanCommand ToCommand(int planId, int practitionerId,
        AdjustNutritionPlanResource resource)
    {
        return new AdjustNutritionPlanCommand(planId, practitionerId, resource.EnergyKcal,
            resource.ProteinG, resource.CarbG, resource.FatG, resource.Guidelines ?? [],
            resource.Restrictions ?? [], resource.ChangeReason, resource.CustomGuidelines, resource.PatientMessage);
    }
}

public static class ResolveReviewItemCommandAssembler
{
    public static ResolveReviewItemCommand ToCommand(int reviewItemId, int practitionerId,
        ResolveReviewItemResource resource)
    {
        return new ResolveReviewItemCommand(reviewItemId, practitionerId, resource.ResolvedWithAdjustment,
            resource.ResolutionNote);
    }
}

// ---------------------------------------------------------------------------------------------
// Model to Resource
// ---------------------------------------------------------------------------------------------

public static class NutritionalAssessmentResourceAssembler
{
    public static NutritionalAssessmentResource ToResource(NutritionalAssessment assessment)
    {
        return new NutritionalAssessmentResource(
            assessment.Id.Value,
            assessment.PatientId,
            assessment.PractitionerId,
            assessment.Habits,
            assessment.MedicalHistory,
            assessment.PhysicalActivity,
            assessment.Biochemistry,
            assessment.AgeYears,
            assessment.BiologicalSex.Value,
            assessment.SupersedesAssessmentId,
            assessment.IsClosed,
            assessment.ClosedAt,
            assessment.Measurements
                .OrderBy(m => m.TakenAt)
                .Select(m => new ClinicalMeasurementResource(
                    m.Id, m.WeightKg, m.HeightCm, m.Protocol.Value, m.BodyFatPercentage,
                    m.WaistCircumferenceCm, m.TakenAt, m.BmiKgM2, m.BmiCategory, m.ProtocolChecks))
                .ToList(),
            assessment.ActivityLevel?.Value,
            assessment.EatingHabits is { } habits
                ? new EatingHabitsResource(habits.MealsPerDay, habits.WaterLitersPerDay, habits.MealsOutPerWeek)
                : null,
            assessment.BiochemistryPanel is { } panel
                ? new BiochemistryPanelResource(panel.FastingGlucoseMgDl, panel.TotalCholesterolMgDl,
                    panel.TriglyceridesMgDl)
                : null,
            assessment.ConditionsSnapshot,
            assessment.ConsultationId);
    }
}

public static class PatientBaselineResourceAssembler
{
    /// <param name="baseline">The baseline.</param>
    /// <param name="today">The practice's date (IClinicalDateProvider), the day the age is computed for.</param>
    public static PatientBaselineResource ToResource(PatientBaseline baseline, DateOnly today)
    {
        // The age is derived on every read, which is what makes it update itself on the birthday.
        return new PatientBaselineResource(
            baseline.PatientId,
            baseline.BirthDate,
            baseline.AgeAt(today),
            baseline.BiologicalSex.Value,
            baseline.Height.Value,
            baseline.Conditions.Select(c => c.Value).ToList(),
            baseline.UpdatedAt ?? baseline.CreatedAt,
            baseline.BirthDateEstimated);
    }
}

public static class NutritionalDiagnosisResourceAssembler
{
    public static NutritionalDiagnosisResource ToResource(NutritionalDiagnosis diagnosis)
    {
        return new NutritionalDiagnosisResource(
            diagnosis.Id.Value,
            diagnosis.PatientId,
            diagnosis.AssessmentId,
            diagnosis.Statement,
            diagnosis.Rationale.Value,
            diagnosis.IssuedAt,
            diagnosis.IsActive,
            diagnosis.Code?.Value,
            diagnosis.Source?.Value,
            diagnosis.BmiAtIssue);
    }
}

public static class NutritionPlanResourceAssembler
{
    public static NutritionPlanResource ToResource(NutritionPlan plan)
    {
        var basis = plan.CalculationBasis;

        return new NutritionPlanResource(
            plan.Id.Value,
            plan.PatientId,
            plan.PractitionerId,
            plan.DiagnosisId,
            plan.Version,
            new CalculationBasisResource(
                basis.Equation.Value,
                basis.ReferenceWeight.Kind,
                basis.ReferenceWeight.ValueKg,
                basis.ActivityFactor,
                basis.DeficitStrategy.Kind,
                basis.DeficitStrategy.Value,
                basis.ComputedBmr,
                basis.ComputedTdee),
            new TargetsResource(plan.TargetProposal.EnergyKcal, plan.TargetProposal.ProteinG,
                plan.TargetProposal.CarbG, plan.TargetProposal.FatG),
            plan.PrescribedTargets is null
                ? null
                : new TargetsResource(plan.PrescribedTargets.EnergyKcal, plan.PrescribedTargets.ProteinG,
                    plan.PrescribedTargets.CarbG, plan.PrescribedTargets.FatG),
            plan.PrescribedTargets?.Outcome.Value,
            plan.PrescribedTargets?.OverrideReason?.Value,
            plan.ChangeReason?.Value,
            plan.Guidelines.Select(g => g.ToString()).ToList(),
            plan.Restrictions,
            plan.IsActive,
            plan.PublishedAt,
            plan.SupersededAt,
            plan.Guidelines.Select(g => new GuidelineItemResource(g.Code, g.Custom)).ToList(),
            plan.LegacyRestrictions,
            plan.DroppedLegacyRestrictions,
            plan.PatientMessage,
            plan.ChangeReason?.Code,
            plan.ChangeReason?.Data is { } data ? new ChangeReasonDataResource(data.Date, data.SignalType) : null);
    }
}

public static class AcceptPlanProposalCommandAssembler
{
    /// <summary>NC-10. As is, or with the edits of PR14.IA-A (missing numbers read as 0 and fail the bounds).</summary>
    public static AcceptPlanProposalCommand ToCommand(int reviewItemId, int practitionerId,
        AcceptPlanProposalResource resource)
    {
        var edits = resource.AsIs
            ? null
            : resource.EnergyKcal is null && resource.ProteinG is null && resource.CarbG is null &&
              resource.FatG is null && resource.Guidelines is null
                ? null
                : new AdjustedPlanDto(resource.EnergyKcal ?? 0m, resource.ProteinG ?? 0m, resource.CarbG ?? 0m,
                    resource.FatG ?? 0m, resource.Guidelines ?? [], resource.PatientMessage);
        return new AcceptPlanProposalCommand(reviewItemId, practitionerId, resource.AsIs, edits);
    }
}

public static class PlanAdjustmentProposalResourceAssembler
{
    /// <summary>NC-10. The proposal of the lookup, or null when the item has none.</summary>
    public static PlanAdjustmentProposalResource? ToResource(PlanProposalLookup lookup)
    {
        var proposal = lookup.ReviewItem.Proposal;
        if (proposal is null) return null;
        return new PlanAdjustmentProposalResource(proposal.Id, lookup.ReviewItem.Id.Value, proposal.Title,
            lookup.CurrentEnergyKcal, proposal.ProposedEnergyKcal, proposal.ProposedProteinG, proposal.ProposedCarbG,
            proposal.ProposedFatG, proposal.AddedGuidelines, proposal.RemovedGuidelines, proposal.PatientMessage,
            proposal.RecheckAfterDays, proposal.Rationale, proposal.GeneratedAt, proposal.Status.Value,
            proposal.AssignedPlanVersion, proposal.PractitionerLanguage, proposal.PatientLanguage);
    }

    /// <summary>NC-10/NC-11. The item with the patient's name when <paramref name="entry" /> is the same item.</summary>
    public static PlanProposalAcceptanceResource ToResource(PlanProposalAcceptanceOutcome outcome,
        ReviewInboxEntry? entry = null)
    {
        var item = entry is not null && entry.ReviewItem.Id.Value == outcome.ReviewItem.Id.Value
            ? ReviewItemResourceAssembler.ToResource(entry)
            : ReviewItemResourceAssembler.ToResource(outcome.ReviewItem);
        return new PlanProposalAcceptanceResource(item, NutritionPlanResourceAssembler.ToResource(outcome.PlanVersion));
    }
}

public static class ReviewItemQueryAssembler
{
    /// <summary>NC-11. The <c>?state=</c> filter: Open when absent (the inbox as it always was), false when invalid.</summary>
    public static bool TryToQuery(int practitionerId, string? state, out GetReviewItemsByPractitionerIdQuery query)
    {
        query = new GetReviewItemsByPractitionerIdQuery(practitionerId, new ReviewItemState(ReviewItemState.Open));
        if (string.IsNullOrWhiteSpace(state)) return true;
        try
        {
            query = query with { State = new ReviewItemState(state.Trim()) };
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public static class ReviewItemResourceAssembler
{
    public static ReviewItemResource ToResource(ReviewItem reviewItem)
    {
        return ToResource(reviewItem, null);
    }

    /// <summary>NC-11. One line of the inbox, with the patient's name.</summary>
    public static ReviewItemResource ToResource(ReviewInboxEntry entry)
    {
        return ToResource(entry.ReviewItem, entry.PatientFullName);
    }

    private static ReviewItemResource ToResource(ReviewItem reviewItem, string? patientFullName)
    {
        var evidence = reviewItem.EvidenceData;
        return new ReviewItemResource(
            reviewItem.Id.Value,
            reviewItem.PatientId,
            reviewItem.PractitionerId,
            reviewItem.SignalType.Value,
            reviewItem.Evidence,
            reviewItem.State.Value,
            reviewItem.ResolvedWithAdjustment,
            reviewItem.ResolutionNote,
            reviewItem.ResolvedAt,
            reviewItem.CreatedAt,
            patientFullName,
            reviewItem.HasPlanProposal,
            evidence is null
                ? null
                : new ReviewItemEvidenceResource(evidence.AveragePercentFromTarget, evidence.DeviatedDays,
                    evidence.LoggedDaysConsidered, evidence.Direction, evidence.AdjustedOn,
                    evidence.AdjustedPlanVersion, evidence.AverageEnergyKcalFromTarget, evidence.ConsistencyKgPerWeek,
                    evidence.ConsistencyState, evidence.AlertSinceOn, evidence.WeeksInAlert,
                    evidence.ShownToPatientOn),
            reviewItem.ResolutionNoteCode,
            reviewItem.ResolutionNoteData is { } note ? new ResolutionNoteDataResource(note.PlanVersion) : null);
    }
}
