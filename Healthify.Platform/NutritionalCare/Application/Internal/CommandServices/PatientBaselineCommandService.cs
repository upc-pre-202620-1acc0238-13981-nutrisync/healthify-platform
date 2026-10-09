using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

public class PatientBaselineCommandService(
    IPatientBaselineRepository baselineRepository,
    IUnitOfWork unitOfWork,
    IIamContextFacade iamContextFacade,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IClinicalDateProvider clinicalDate,
    ILogger<PatientBaselineCommandService> logger,
    IMediator mediator) : IPatientBaselineCommandService
{
    /// <summary>NC-1 - Record the patient baseline (EV-1).</summary>
    public async Task<Result<PatientBaseline, NutritionalCareError>> Handle(
        RecordPatientBaselineCommand command, CancellationToken cancellationToken = default)
    {
        var today = clinicalDate.Today();

        // 1. Value objects, each to its own error.
        var invalid = Validate(command.BirthDate, command.BiologicalSex, command.HeightCm,
            command.Conditions, today);
        if (invalid is not null) return Failure(invalid.Value);

        try
        {
            // 2. Business rules: Practitioner Only Measures, Active Care Link Required (Subflow 3.1)
            var denied = await AuthorizeAsync(command.PatientId, command.PractitionerId, cancellationToken);
            if (denied is not null) return Failure(denied.Value);

            // 3. Business rule: One Baseline Per Patient (NC-1). The unique index on patient_id backs
            //    this check when two requests race; the loser ends as an unexpected error.
            if (await baselineRepository.FindByPatientIdAsync(command.PatientId, cancellationToken) is not null)
                return Failure(NutritionalCareError.BaselineAlreadyRecorded);

            var baseline = new PatientBaseline(command, today);

            await baselineRepository.AddAsync(baseline, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(new PatientBaselineRecorded(baseline.PatientId), cancellationToken);

            return new Result<PatientBaseline, NutritionalCareError>.Success(baseline);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording the baseline of patient {PatientId}", command.PatientId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    /// <summary>NC-1 - Edit the patient baseline. Past assessments keep their own snapshot.</summary>
    public async Task<Result<PatientBaseline, NutritionalCareError>> Handle(
        UpdatePatientBaselineCommand command, CancellationToken cancellationToken = default)
    {
        var today = clinicalDate.Today();

        var invalid = Validate(command.BirthDate, command.BiologicalSex, command.HeightCm,
            command.Conditions, today);
        if (invalid is not null) return Failure(invalid.Value);

        try
        {
            var denied = await AuthorizeAsync(command.PatientId, command.PractitionerId, cancellationToken);
            if (denied is not null) return Failure(denied.Value);

            var baseline = await baselineRepository.FindByPatientIdAsync(command.PatientId, cancellationToken);
            if (baseline is null) return Failure(NutritionalCareError.BaselineNotFound);

            baseline.Update(command, today);

            baselineRepository.Update(baseline);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(new PatientBaselineUpdated(baseline.PatientId), cancellationToken);

            return new Result<PatientBaseline, NutritionalCareError>.Success(baseline);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating the baseline of patient {PatientId}", command.PatientId);
            return Failure(NutritionalCareError.UnexpectedError);
        }
    }

    private static NutritionalCareError? Validate(DateOnly birthDate, string? biologicalSex, decimal heightCm,
        IReadOnlyList<string>? conditions, DateOnly today)
    {
        try
        {
            PatientBaseline.EnsurePlausibleBirthDate(birthDate, today);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidBirthDate;
        }

        try
        {
            _ = new BiologicalSex(biologicalSex ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidBiologicalSex;
        }

        try
        {
            _ = new HeightCm(heightCm);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidHeight;
        }

        try
        {
            _ = MedicalCondition.ListFrom(conditions);
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.UnknownMedicalCondition;
        }

        return null;
    }

    private async Task<NutritionalCareError?> AuthorizeAsync(int patientId, int practitionerId,
        CancellationToken cancellationToken)
    {
        if (!await iamContextFacade.IsPractitioner(practitionerId, cancellationToken))
            return NutritionalCareError.PractitionerOnly;

        // Asked through the Open Host Service of Care Relationship, which degrades to false: no answer
        // means no access.
        if (!await careRelationshipContextFacade.IsCareLinkActive(patientId, practitionerId,
                cancellationToken))
            return NutritionalCareError.ActiveCareLinkRequired;

        return null;
    }

    private static Result<PatientBaseline, NutritionalCareError> Failure(NutritionalCareError error)
    {
        return new Result<PatientBaseline, NutritionalCareError>.Failure(error);
    }
}
