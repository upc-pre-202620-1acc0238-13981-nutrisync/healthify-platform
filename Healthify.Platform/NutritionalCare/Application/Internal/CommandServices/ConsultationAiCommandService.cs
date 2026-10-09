using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

/// <summary>
///     IA-6 - Suggest Diagnosis and IA-7 - Suggest Guidelines, inside the guided consultation.
/// </summary>
/// <remarks>
///     The order is the specification:
///     1. only a practitioner (Iam), and only the one leading the consultation, with the care link still active;
///     2. the step the suggestion needs: the measurement of step 1 (IA-6) or the diagnosis of step 2 (IA-7);
///     3. the deterministic suggestion, computed first: it is the answer whenever the AI is not;
///     4. the generation through the shared pipeline (kill switch, the patient's AI consent, §12-#5, quota, the
///     provider, the validator of the function). Any failure there is logged and answered with step 3.
///     The input is the clinical snapshot of the assessment (index, waist, body fat, sex, age, condition codes,
///     activity, habits and labs): never a name, an identifier, the free-text history or anything a patient wrote.
///     These are practitioner functions, so they may read the diagnosis (NC-4); their output never reaches the
///     patient. Nothing is stored here: the suggestion is shown, and only what the practitioner then saves (with
///     <c>AiGenerationId</c>, NC-4) becomes part of the record.
/// </remarks>
public class ConsultationAiCommandService(
    IConsultationRepository consultationRepository,
    INutritionalAssessmentRepository assessmentRepository,
    INutritionalDiagnosisRepository diagnosisRepository,
    IDefaultGuidelinesProvider defaultGuidelines,
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IAiGenerationPipeline pipeline,
    ILogger<ConsultationAiCommandService> logger) : IConsultationAiCommandService
{
    public async Task<Result<DiagnosisSuggestion, NutritionalCareError>> Handle(
        SuggestConsultationDiagnosisCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Authorization.
            var (consultation, error) = await LoadLedConsultationAsync(command.ConsultationId, command.PractitionerId,
                cancellationToken);
            if (consultation is null) return new Result<DiagnosisSuggestion, NutritionalCareError>.Failure(error);

            // 2. Business rule: Steps In Order (NC-2). No suggestion without the measurement of step 1.
            var assessment = consultation.AssessmentId is { } assessmentId
                ? await assessmentRepository.FindByIdAsync(assessmentId, cancellationToken)
                : null;
            var measurement = assessment?.LatestMeasurement;
            if (assessment is null || measurement is null)
                return new Result<DiagnosisSuggestion, NutritionalCareError>.Failure(
                    NutritionalCareError.ConsultationStepOutOfOrder);

            // 3. The deterministic suggestion (NC-4 fallback): EV-3 is never left empty.
            var bmiCategory = DiagnosisCode.FromBodyMassIndex(measurement.BmiKgM2);
            var rule = new DiagnosisSuggestion(bmiCategory.Value,
                ClinicalRationale.FromMeasurement(measurement.BmiKgM2, measurement.WaistCircumferenceCm,
                    measurement.BodyFatPercentage).Value, DiagnosisSuggestion.RuleSource, null);

            // 4. The AI, validated against the index (AI Suggestion Within One Grade Of The Index).
            var generation = await pipeline.GenerateAsync(
                new AiGenerationRequest(AiFeature.DiagnosisSuggestion, consultation.PatientId, command.PractitionerId,
                    await LanguageOfAsync(command.PractitionerId, cancellationToken),
                    ClinicalInputOf(assessment, measurement)),
                new DiagnosisSuggestionOutputValidator(bmiCategory), cancellationToken);

            if (generation is not Result<AiGenerationOutcome<DiagnosisSuggestionOutput>, AiError>.Success success)
            {
                LogFallback(AiFeature.DiagnosisSuggestion, generation);
                return new Result<DiagnosisSuggestion, NutritionalCareError>.Success(rule);
            }

            var output = success.Value.Output;
            return new Result<DiagnosisSuggestion, NutritionalCareError>.Success(new DiagnosisSuggestion(
                new DiagnosisCode(output.Code).Value, output.Rationale.Trim(), DiagnosisSuggestion.AiSource,
                success.Value.GenerationId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error suggesting the diagnosis of consultation {ConsultationId}",
                command.ConsultationId);
            return new Result<DiagnosisSuggestion, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError);
        }
    }

    public async Task<Result<GuidelineSuggestions, NutritionalCareError>> Handle(
        SuggestConsultationGuidelinesCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Authorization.
            var (consultation, error) = await LoadLedConsultationAsync(command.ConsultationId, command.PractitionerId,
                cancellationToken);
            if (consultation is null) return new Result<GuidelineSuggestions, NutritionalCareError>.Failure(error);

            // 2. Business rule: Steps In Order (NC-2). The guidelines follow the coded diagnosis of step 2.
            var diagnosis = consultation.DiagnosisId is { } diagnosisId
                ? await diagnosisRepository.FindByIdAsync(diagnosisId, cancellationToken)
                : null;
            if (diagnosis?.Code is null)
                return new Result<GuidelineSuggestions, NutritionalCareError>.Failure(
                    NutritionalCareError.ConsultationStepOutOfOrder);

            // 3. The fixed table of NC-6: cheap, deterministic and the answer whenever the AI is not.
            var rule = new GuidelineSuggestions(defaultGuidelines.For(diagnosis.Code), DiagnosisSuggestion.RuleSource,
                null);

            var assessment = consultation.AssessmentId is { } assessmentId
                ? await assessmentRepository.FindByIdAsync(assessmentId, cancellationToken)
                : null;
            var measurement = assessment?.LatestMeasurement;
            if (assessment is null || measurement is null)
                return new Result<GuidelineSuggestions, NutritionalCareError>.Success(rule);

            // 4. The AI: catalog codes only (Closed Guideline Catalog).
            var generation = await pipeline.GenerateAsync(
                new AiGenerationRequest(AiFeature.GuidelineSuggestions, consultation.PatientId, command.PractitionerId,
                    await LanguageOfAsync(command.PractitionerId, cancellationToken),
                    new
                    {
                        diagnosisCode = diagnosis.Code.Value,
                        clinical = ClinicalInputOf(assessment, measurement),
                        guidelineCatalog = Guideline.Codes
                    }),
                GuidelineSuggestionsOutputValidator.Instance, cancellationToken);

            if (generation is not Result<AiGenerationOutcome<GuidelineSuggestionsOutput>, AiError>.Success success)
            {
                LogFallback(AiFeature.GuidelineSuggestions, generation);
                return new Result<GuidelineSuggestions, NutritionalCareError>.Success(rule);
            }

            // In the order of EV-5, whatever order the model used.
            var suggested = Guideline.Codes.Where(c => success.Value.Output.Suggested.Contains(c)).ToList();
            return new Result<GuidelineSuggestions, NutritionalCareError>.Success(
                new GuidelineSuggestions(suggested, DiagnosisSuggestion.AiSource, success.Value.GenerationId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error suggesting the guidelines of consultation {ConsultationId}",
                command.ConsultationId);
            return new Result<GuidelineSuggestions, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>
    ///     What leaves for the model: the clinical snapshot the assessment took at the consultation. Codes and
    ///     numbers only; the free-text history and habits stay here.
    /// </summary>
    internal static object ClinicalInputOf(NutritionalAssessment assessment, ClinicalMeasurement measurement)
    {
        var labs = assessment.BiochemistryPanel;
        return new
        {
            bmiKgM2 = measurement.BmiKgM2,
            bmiCategory = DiagnosisCode.FromBodyMassIndex(measurement.BmiKgM2).Value,
            waistCm = measurement.WaistCircumferenceCm,
            bodyFatPercentage = measurement.BodyFatPercentage,
            sex = assessment.BiologicalSex.Value,
            ageYears = assessment.AgeYears,
            conditions = assessment.ConditionsSnapshot ?? [],
            activityLevel = assessment.ActivityLevel?.Value,
            habits = assessment.EatingHabits is null
                ? null
                : new
                {
                    mealsPerDay = assessment.MealsPerDay,
                    waterLitersPerDay = assessment.WaterLitersPerDay,
                    mealsOutPerWeek = assessment.MealsOutPerWeek
                },
            biochemistry = labs is null
                ? null
                : new
                {
                    fastingGlucoseMgDl = labs.FastingGlucoseMgDl,
                    totalCholesterolMgDl = labs.TotalCholesterolMgDl,
                    triglyceridesMgDl = labs.TriglyceridesMgDl
                }
        };
    }

    /// <summary>The consultation, if the practitioner leads it and still cares for the patient.</summary>
    private async Task<(Consultation? Consultation, NutritionalCareError Error)> LoadLedConsultationAsync(
        int consultationId, int practitionerId, CancellationToken cancellationToken)
    {
        // Business rule: only a practitioner issues a diagnosis (Subflow 3.2), and only the one leading it (NC-2).
        if (!await iamContextFacade.IsPractitioner(practitionerId, cancellationToken))
            return (null, NutritionalCareError.PractitionerOnly);

        var consultation = await consultationRepository.FindByIdAsync(consultationId, cancellationToken);
        if (consultation is null) return (null, NutritionalCareError.ConsultationNotFound);
        if (consultation.PractitionerId != practitionerId) return (null, NutritionalCareError.PractitionerOnly);

        // Business rule: Active Care Link Required (Subflow 3.2), asked again at every step.
        if (!await careRelationshipContextFacade.IsCareLinkActive(consultation.PatientId, practitionerId,
                cancellationToken))
            return (null, NutritionalCareError.ActiveCareLinkRequired);

        return (consultation, NutritionalCareError.UnexpectedError);
    }

    private async Task<string> LanguageOfAsync(int practitionerId, CancellationToken cancellationToken)
    {
        var preferred = await iamContextFacade.GetPreferredLanguage(practitionerId, cancellationToken);
        return string.Equals(preferred?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
    }

    /// <summary>Why the deterministic suggestion was used; never anything about the patient.</summary>
    private void LogFallback<T>(AiFeature feature, Result<AiGenerationOutcome<T>, AiError> generation)
    {
        var reason = generation is Result<AiGenerationOutcome<T>, AiError>.Failure failure
            ? failure.Error.ToString()
            : "Unknown";
        logger.LogInformation("AI feature {Feature} answered with its deterministic fallback: {Reason}", feature.Name,
            reason);
    }
}
