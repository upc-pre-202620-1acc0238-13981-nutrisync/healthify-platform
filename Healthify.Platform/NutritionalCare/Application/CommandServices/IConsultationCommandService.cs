using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     The guided consultation: start, the four steps, the publication draft and discarding. It is the only
///     path that records assessments, issues diagnoses and proposes and publishes targets.
/// </summary>
public interface IConsultationCommandService
{
    /// <summary>Start a consultation.</summary>
    Task<Result<Consultation, NutritionalCareError>> Handle(StartConsultationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Step 1: structured assessment and measurement.</summary>
    Task<Result<ConsultationMeasurementOutcome, NutritionalCareError>> Handle(
        RecordConsultationMeasurementCommand command, CancellationToken cancellationToken = default);

    /// <summary>Step 2: coded diagnosis, pending until step 4 replaces the active one.</summary>
    Task<Result<ConsultationDiagnosisOutcome, NutritionalCareError>> Handle(
        IssueConsultationDiagnosisCommand command, CancellationToken cancellationToken = default);

    /// <summary>Step 3: ProposeTargetsForConsultation, with the default parameters or changed ones.</summary>
    Task<Result<ConsultationTargetProposalOutcome, NutritionalCareError>> Handle(
        ProposeConsultationTargetsCommand command, CancellationToken cancellationToken = default);

    /// <summary>Step 3: accept or override the targets of the draft.</summary>
    Task<Result<ConsultationTargetsOutcome, NutritionalCareError>> Handle(
        PrescribeConsultationTargetsCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Step 4: PublishFromConsultation. Replaces the active diagnosis and version in one transaction and
    ///     closes the consultation.
    /// </summary>
    Task<Result<ConsultationPublicationOutcome, NutritionalCareError>> Handle(
        PublishFromConsultationCommand command, CancellationToken cancellationToken = default);

    /// <summary>Step 4: keep the restrictions and guidelines chosen in step 4 before publishing.</summary>
    Task<Result<Consultation, NutritionalCareError>> Handle(SaveConsultationPublicationDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Discard a consultation in progress; nothing active changes.</summary>
    Task<Result<Consultation, NutritionalCareError>> Handle(AbandonConsultationCommand command,
        CancellationToken cancellationToken = default);
}
