using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

public class NutritionalAssessmentCommandService(
    INutritionalAssessmentRepository assessmentRepository,
    IPatientBaselineRepository baselineRepository,
    IUnitOfWork unitOfWork,
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    ILogger<NutritionalAssessmentCommandService> logger,
    IMediator mediator) : INutritionalAssessmentCommandService
{
    /// <summary>Subflow 3.1 - Record Assessment.</summary>
    public async Task<Result<NutritionalAssessment, NutritionalCareError>> Handle(
        RecordAssessmentCommand command, CancellationToken cancellationToken = default)
    {
        // NC-3: the structured values are optional here; when present, each one to its own error. A
        // request without them behaves exactly as before.
        var invalid = StructuredAssessmentInput.TryActivityLevel(command.ActivityLevelCode, out _)
                      ?? StructuredAssessmentInput.TryEatingHabits(command.EatingHabitsData, out _)
                      ?? StructuredAssessmentInput.TryBiochemistry(command.BiochemistryData, out _);
        if (invalid is not null)
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(invalid.Value);

        try
        {
            // Business rule: Practitioner Only Measures (Subflow 3.1)
            if (!await iamContextFacade.IsPractitioner(command.PractitionerId, cancellationToken))
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.PractitionerOnly);

            // Business rule: Active Care Link Required (Subflow 3.1). Asked through the Open Host
            // Service of Care Relationship, which degrades to false: no answer means no access.
            if (!await careRelationshipContextFacade.IsCareLinkActive(command.PatientId,
                    command.PractitionerId, cancellationToken))
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.ActiveCareLinkRequired);

            // DECISIÓN §12-#13: when the patient already has a baseline, the assessment keeps a snapshot
            // of its medical history; age and sex still come from this endpoint's own fields.
            var baseline = await baselineRepository.FindByPatientIdAsync(command.PatientId, cancellationToken);
            var assessment = new NutritionalAssessment(command, baseline?.Conditions);

            await assessmentRepository.AddAsync(assessment, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new NutritionalAssessmentRecorded(assessment.Id.Value, assessment.PatientId,
                    assessment.PractitionerId), cancellationToken);

            return new Result<NutritionalAssessment, NutritionalCareError>.Success(assessment);
        }
        catch (ArgumentException)
        {
            // Business rule: Habits History And Activity Required (Subflow 3.1)
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.HabitsHistoryAndActivityRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording an assessment for patient {PatientId}", command.PatientId);
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>Subflow 3.1 - Take Clinical Measurement.</summary>
    public async Task<Result<NutritionalAssessment, NutritionalCareError>> Handle(
        TakeClinicalMeasurementCommand command, CancellationToken cancellationToken = default)
    {
        // Business rule: Measurement Protocol Recorded (Subflow 3.1), checked before anything loads.
        if (string.IsNullOrWhiteSpace(command.Protocol))
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.MeasurementProtocolRequired);

        // NC-3: the EV-2 checklist is optional on this endpoint; when sent, at least one check.
        if (command.ProtocolChecks is not null
            && StructuredAssessmentInput.TryProtocolChecks(command.ProtocolChecks, out _) is { } checklistError)
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(checklistError);

        try
        {
            var assessment = await assessmentRepository.FindByIdAsync(command.AssessmentId, cancellationToken);
            if (assessment is null)
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.AssessmentNotFound);

            // Business rule: Practitioner Only Measures (Subflow 3.1). The patient never measures.
            if (assessment.PractitionerId != command.PractitionerId)
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.PractitionerOnly);

            // Business rule: No Measurement On Closed Assessment (Subflow 3.1)
            if (assessment.IsClosed)
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.AssessmentAlreadyClosed);

            var measurement = assessment.TakeClinicalMeasurement(command);

            assessmentRepository.Update(assessment);
            await unitOfWork.CompleteAsync(cancellationToken);

            // Integration event 3 of 13: Monitoring appends an anthropometry point.
            await mediator.PublishAsync(
                new ClinicalMeasurementTaken(assessment.Id.Value, assessment.PatientId,
                    measurement.WeightKg, measurement.TakenAt), cancellationToken);

            return new Result<NutritionalAssessment, NutritionalCareError>.Success(assessment);
        }
        catch (InvalidOperationException)
        {
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.AssessmentAlreadyClosed);
        }
        catch (ArgumentException)
        {
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.MeasurementProtocolRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error taking a clinical measurement on assessment {AssessmentId}",
                command.AssessmentId);
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>Subflow 3.1 - Close Assessment. After this the aggregate is immutable.</summary>
    public async Task<Result<NutritionalAssessment, NutritionalCareError>> Handle(
        CloseAssessmentCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            var assessment = await assessmentRepository.FindByIdAsync(command.AssessmentId, cancellationToken);
            if (assessment is null)
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.AssessmentNotFound);

            if (assessment.PractitionerId != command.PractitionerId)
                return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                    NutritionalCareError.PractitionerOnly);

            // Business rule: Closed Assessment Is Immutable (Subflow 3.1)
            assessment.Close();

            assessmentRepository.Update(assessment);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new AssessmentClosed(assessment.Id.Value, assessment.PatientId, assessment.ClosedAt!.Value),
                cancellationToken);

            return new Result<NutritionalAssessment, NutritionalCareError>.Success(assessment);
        }
        catch (InvalidOperationException)
        {
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.AssessmentAlreadyClosed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error closing assessment {AssessmentId}", command.AssessmentId);
            return new Result<NutritionalAssessment, NutritionalCareError>.Failure(
                NutritionalCareError.UnexpectedError);
        }
    }
}
