using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     The guided consultation (NC-2): start, the four steps, the publication draft and discarding. Steps 1 to 3
///     arrived with NC-3 to NC-5 and step 4 with NC-7.
/// </summary>
public interface IConsultationCommandService
{
    /// <summary>NC-2 - Start a consultation.</summary>
    Task<Result<Consultation, NutritionalCareError>> Handle(StartConsultationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>NC-3 - Step 1: structured assessment and measurement (EV-2).</summary>
    Task<Result<ConsultationMeasurementOutcome, NutritionalCareError>> Handle(
        RecordConsultationMeasurementCommand command, CancellationToken cancellationToken = default);

    /// <summary>NC-4 - Step 2: coded diagnosis, pending until step 4 replaces the active one (EV-3, NC-7).</summary>
    Task<Result<ConsultationDiagnosisOutcome, NutritionalCareError>> Handle(
        IssueConsultationDiagnosisCommand command, CancellationToken cancellationToken = default);

    /// <summary>NC-5 - Step 3: ProposeTargetsForConsultation, with the default parameters or changed ones (EV-4).</summary>
    Task<Result<ConsultationTargetProposalOutcome, NutritionalCareError>> Handle(
        ProposeConsultationTargetsCommand command, CancellationToken cancellationToken = default);

    /// <summary>NC-5 - Step 3: accept or override the targets of the draft (EV-4).</summary>
    Task<Result<ConsultationTargetsOutcome, NutritionalCareError>> Handle(
        PrescribeConsultationTargetsCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-7 - Step 4: PublishFromConsultation. Replaces the active diagnosis and version in one transaction and
    ///     closes the consultation (EV-5).
    /// </summary>
    Task<Result<ConsultationPublicationOutcome, NutritionalCareError>> Handle(
        PublishFromConsultationCommand command, CancellationToken cancellationToken = default);

    /// <summary>NC-2 - Step 4: keep the restrictions and guidelines chosen in EV-5 before publishing.</summary>
    Task<Result<Consultation, NutritionalCareError>> Handle(SaveConsultationPublicationDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>NC-2 (P2) - Discard a consultation in progress; nothing active changes.</summary>
    Task<Result<Consultation, NutritionalCareError>> Handle(AbandonConsultationCommand command,
        CancellationToken cancellationToken = default);
}
