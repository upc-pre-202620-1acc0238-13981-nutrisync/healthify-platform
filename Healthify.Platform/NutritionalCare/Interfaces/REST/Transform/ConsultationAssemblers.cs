using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;

// ---------------------------------------------------------------------------------------------
// NC-2. Resource to Command / Query
// ---------------------------------------------------------------------------------------------

public static class ConsultationCommandAssembler
{
    public static StartConsultationCommand ToCommand(int patientId, int practitionerId,
        StartConsultationResource? resource)
    {
        return new StartConsultationCommand(patientId, practitionerId, resource?.ScheduledFollowUpId);
    }

    public static RecordConsultationMeasurementCommand ToCommand(int consultationId, int practitionerId,
        RecordConsultationMeasurementResource resource)
    {
        return new RecordConsultationMeasurementCommand(consultationId, practitionerId, resource.WeightKg,
            resource.WaistCm, resource.BodyFatPercentage, resource.ProtocolChecks ?? [],
            resource.ActivityLevel ?? string.Empty,
            resource.Habits is { } habits
                ? new EatingHabitsDto(habits.MealsPerDay, habits.WaterLitersPerDay, habits.MealsOutPerWeek)
                : null,
            resource.Biochemistry is { } lab
                ? new BiochemistryDto(lab.FastingGlucoseMgDl, lab.TotalCholesterolMgDl, lab.TriglyceridesMgDl)
                : null);
    }

    public static IssueConsultationDiagnosisCommand ToCommand(int consultationId, int practitionerId,
        IssueConsultationDiagnosisResource resource)
    {
        return new IssueConsultationDiagnosisCommand(consultationId, practitionerId, resource.Code ?? string.Empty,
            resource.Source ?? string.Empty, resource.AiGenerationId, resource.Rationale);
    }

    public static ProposeConsultationTargetsCommand ToCommand(int consultationId, int practitionerId,
        ProposeConsultationTargetsResource? resource)
    {
        var p = resource?.Parameters;
        return new ProposeConsultationTargetsCommand(consultationId, practitionerId,
            p is null
                ? null
                : new TargetParametersDto(p.Equation, p.ReferenceWeightKind, p.ReferenceWeightKg, p.ActivityFactor,
                    p.DeficitKind, p.DeficitValue, p.ProteinGramsPerKg, p.FatPercentOfEnergy));
    }

    public static PrescribeConsultationTargetsCommand ToCommand(int consultationId, int practitionerId,
        PrescribeConsultationTargetsResource resource)
    {
        return new PrescribeConsultationTargetsCommand(consultationId, practitionerId,
            resource.Outcome ?? string.Empty, resource.EnergyKcal, resource.ProteinG, resource.CarbG, resource.FatG,
            resource.OverrideReason);
    }

    public static SaveConsultationPublicationDraftCommand ToDraftCommand(int consultationId, int practitionerId,
        ConsultationPublicationResource resource)
    {
        return new SaveConsultationPublicationDraftCommand(consultationId, practitionerId,
            resource.Restrictions ?? [], resource.Guidelines ?? [], resource.CustomGuidelines ?? [],
            resource.PatientMessage);
    }

    public static PublishFromConsultationCommand ToPublishCommand(int consultationId, int practitionerId,
        ConsultationPublicationResource resource, string? idempotencyKey)
    {
        return new PublishFromConsultationCommand(consultationId, practitionerId, resource.Restrictions ?? [],
            resource.Guidelines ?? [], resource.CustomGuidelines ?? [], idempotencyKey, resource.PatientMessage);
    }
}

public static class ConsultationQueryAssembler
{
    /// <summary>The <c>?state=</c> filter: null when absent, false when it is not a consultation state.</summary>
    public static bool TryToQuery(int patientId, string? state, out GetConsultationsByPatientIdQuery query)
    {
        query = new GetConsultationsByPatientIdQuery(patientId);
        if (string.IsNullOrWhiteSpace(state)) return true;
        try
        {
            query = query with { State = new ConsultationState(state) };
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// NC-2. Domain to Resource
// ---------------------------------------------------------------------------------------------

public static class ConsultationResourceAssembler
{
    public static ConsultationResource ToResource(ConsultationDetails details)
    {
        var consultation = details.Consultation;
        return new ConsultationResource(
            consultation.Id.Value,
            consultation.PatientId,
            consultation.PractitionerId,
            consultation.State.Value,
            consultation.CurrentStep.Value,
            consultation.CurrentStep.Number,
            consultation.StartedAt,
            consultation.LastSavedAt,
            details.Assessment is { } assessment ? ToMeasurement(assessment) : null,
            details.Diagnosis is { } diagnosis ? ToDiagnosis(diagnosis) : null,
            details.Plan is { } plan ? ToTargets(plan, details.ActivePlanLegacyRestrictions) : null,
            consultation.PublicationDraft is { } draft
                ? new ConsultationPublicationDraftResource(draft.Restrictions, draft.Guidelines, draft.CustomGuidelines,
                    draft.PatientMessage)
                : null,
            details.PatientCheckIn is { } checkIn ? ToCheckIn(checkIn) : null,
            consultation.CompletedAt,
            consultation.PublishedPlanVersion,
            consultation.IsFirstConsultation,
            consultation.ScheduledFollowUpId);
    }

    /// <summary>MA-4. The check in as Monitoring published it through its ACL.</summary>
    private static ConsultationPatientCheckInResource ToCheckIn(PreVisitCheckInItem checkIn)
    {
        return new ConsultationPatientCheckInResource(checkIn.FollowUpId, checkIn.Feeling, checkIn.Difficulties,
            checkIn.Questions.Select(q => new ConsultationCheckInQuestionResource(q.Text, q.Origin, q.Language))
                .ToList(),
            checkIn.SubmittedAt, checkIn.EditedAt, checkIn.IsLocked);
    }

    public static ConsultationSummaryResource ToResource(ConsultationSummary summary)
    {
        return new ConsultationSummaryResource(summary.ConsultationId, summary.State, summary.StartedAt,
            summary.CompletedAt, summary.PlanVersion, summary.IsFirstConsultation, summary.WeightKg, summary.Bmi,
            summary.WaistCm);
    }

    public static ConsultationTargetProposalResource ToResource(ConsultationTargetProposalOutcome outcome)
    {
        var inputs = outcome.Inputs;
        var proposal = outcome.Plan.TargetProposal;
        return new ConsultationTargetProposalResource(
            outcome.Plan.Id.Value,
            ToBasis(outcome.Plan),
            new TargetsResource(proposal.EnergyKcal, proposal.ProteinG, proposal.CarbG, proposal.FatG),
            new TargetInputsSummaryResource(inputs.Sex, inputs.AgeYears, inputs.HeightCm, inputs.WeightKg,
                inputs.ActivityLevel),
            outcome.ActivePlanLegacyRestrictions);
    }

    public static DiagnosisSuggestionResource ToResource(DiagnosisSuggestion suggestion, string disclaimer)
    {
        return new DiagnosisSuggestionResource(suggestion.AiGenerationId, suggestion.Code, suggestion.Rationale,
            suggestion.Source, disclaimer);
    }

    public static GuidelineSuggestionsResource ToResource(GuidelineSuggestions suggestions)
    {
        return new GuidelineSuggestionsResource(suggestions.Suggested, suggestions.AiGenerationId, suggestions.Source);
    }

    private static ConsultationMeasurementResource? ToMeasurement(NutritionalAssessment assessment)
    {
        var measurement = assessment.LatestMeasurement;
        if (measurement is null) return null;
        return new ConsultationMeasurementResource(
            assessment.Id.Value,
            measurement.WeightKg,
            measurement.HeightCm,
            measurement.WaistCircumferenceCm,
            measurement.BodyFatPercentage,
            measurement.BmiKgM2,
            measurement.BmiCategory,
            measurement.ProtocolChecks ?? [],
            assessment.ActivityLevel?.Value,
            assessment.EatingHabits is { } habits
                ? new EatingHabitsResource(habits.MealsPerDay, habits.WaterLitersPerDay, habits.MealsOutPerWeek)
                : null,
            assessment.BiochemistryPanel is { } panel
                ? new BiochemistryPanelResource(panel.FastingGlucoseMgDl, panel.TotalCholesterolMgDl,
                    panel.TriglyceridesMgDl)
                : null,
            assessment.AgeYears,
            assessment.BiologicalSex.Value);
    }

    private static ConsultationDiagnosisResource ToDiagnosis(NutritionalDiagnosis diagnosis)
    {
        return new ConsultationDiagnosisResource(diagnosis.Id.Value, diagnosis.Code?.Value, diagnosis.Source?.Value,
            diagnosis.Rationale.Value, diagnosis.BmiAtIssue, diagnosis.AiGenerationId, diagnosis.IssuedAt,
            diagnosis.IsPending);
    }

    private static ConsultationTargetsResource ToTargets(NutritionPlan plan, IReadOnlyList<string> legacy)
    {
        var proposal = plan.TargetProposal;
        var prescribed = plan.PrescribedTargets;
        return new ConsultationTargetsResource(
            plan.Id.Value,
            plan.Version,
            ToBasis(plan),
            new TargetsResource(proposal.EnergyKcal, proposal.ProteinG, proposal.CarbG, proposal.FatG),
            prescribed is null
                ? null
                : new TargetsResource(prescribed.EnergyKcal, prescribed.ProteinG, prescribed.CarbG, prescribed.FatG),
            prescribed?.Outcome.Value,
            prescribed?.OverrideReason?.Value,
            plan.IsPublished,
            legacy);
    }

    private static CalculationBasisResource ToBasis(NutritionPlan plan)
    {
        var basis = plan.CalculationBasis;
        return new CalculationBasisResource(basis.Equation.Value, basis.ReferenceWeight.Kind,
            basis.ReferenceWeight.ValueKg, basis.ActivityFactor, basis.DeficitStrategy.Kind,
            basis.DeficitStrategy.Value, basis.ComputedBmr, basis.ComputedTdee);
    }
}
