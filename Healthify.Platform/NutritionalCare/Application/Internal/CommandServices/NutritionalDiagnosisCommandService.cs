using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

public class NutritionalDiagnosisCommandService(
    INutritionalDiagnosisRepository diagnosisRepository,
    INutritionalAssessmentRepository assessmentRepository,
    IUnitOfWork unitOfWork,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    ILogger<NutritionalDiagnosisCommandService> logger,
    IMediator mediator) : INutritionalDiagnosisCommandService
{
    /// <summary>Subflow 3.2 - Issue Diagnosis. Clinical phase 2.</summary>
    public async Task<Result<NutritionalDiagnosis, NutritionalCareError>> Handle(
        IssueDiagnosisCommand command, CancellationToken cancellationToken = default)
    {
        // Business rule: Clinical Rationale Required (Subflow 3.2), checked before anything loads. With a
        // code (NC-4) the rationale may be left empty: it is written from the measurement.
        if (command.Code is null && string.IsNullOrWhiteSpace(command.Rationale))
            return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                NutritionalCareError.ClinicalRationaleRequired);

        if (command.Code is not null)
        {
            try
            {
                _ = new DiagnosisCode(command.Code);
            }
            catch (ArgumentException)
            {
                return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                    NutritionalCareError.UnknownDiagnosisCode);
            }

            try
            {
                var source = new DiagnosisSource(command.Source ?? DiagnosisSource.PractitionerSelected);
                if (source.IsAiSuggestionAccepted &&
                    (command.AiGenerationId is null or <= 0 || string.IsNullOrWhiteSpace(command.Rationale)))
                    return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                        NutritionalCareError.InvalidDiagnosisSource);
            }
            catch (ArgumentException)
            {
                return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                    NutritionalCareError.InvalidDiagnosisSource);
            }
        }

        try
        {
            if (!await careRelationshipContextFacade.IsCareLinkActive(command.PatientId,
                    command.PractitionerId, cancellationToken))
                return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                    NutritionalCareError.ActiveCareLinkRequired);

            var assessment = await assessmentRepository.FindByIdAsync(command.AssessmentId, cancellationToken);
            if (assessment is null || assessment.PatientId != command.PatientId)
                return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                    NutritionalCareError.AssessmentNotFound);

            // Business rule: Closed Assessment Required (Subflow 3.2). A diagnosis reads a finished
            // picture, not one that is still being edited.
            if (!assessment.IsClosed)
                return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                    NutritionalCareError.ClosedAssessmentRequired);

            // Business rule: One Active Diagnosis Per Patient (Subflow 3.2)
            if (await diagnosisRepository.FindActiveByPatientIdAsync(command.PatientId, cancellationToken)
                is not null)
                return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                    NutritionalCareError.PatientAlreadyHasActiveDiagnosis);

            var diagnosis = new NutritionalDiagnosis(command, assessment.LatestMeasurement);

            await diagnosisRepository.AddAsync(diagnosis, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Stays inside this context. The patient does not read their diagnosis in the app, so no
            // other context declares a handler for this event.
            await mediator.PublishAsync(
                new NutritionalDiagnosisIssued(diagnosis.Id.Value, diagnosis.PatientId,
                    diagnosis.AssessmentId), cancellationToken);

            return new Result<NutritionalDiagnosis, NutritionalCareError>.Success(diagnosis);
        }
        catch (ArgumentException)
        {
            return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                NutritionalCareError.ClinicalRationaleRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error issuing a diagnosis for patient {PatientId}", command.PatientId);
            return new Result<NutritionalDiagnosis, NutritionalCareError>.Failure(
                NutritionalCareError.UnexpectedError);
        }
    }
}
