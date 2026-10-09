using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.CareRelationship.Application.CommandServices;

public interface ICareLinkCommandService
{
    /// <summary>Subflow 2.2 - Establish Care Link. Invoked by a policy, never by an endpoint.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(EstablishCareLinkCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.3 - Grant Consent.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(GrantConsentCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.5 - Withdraw Consent.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(WithdrawConsentCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>CR-2 - Change AI Processing Consent.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(ChangeAiProcessingConsentCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.5 - Revoke Care Link. Invoked by a policy, never by an endpoint.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(RevokeCareLinkCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.5 - Discharge Patient.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(DischargePatientCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Subflow 2.4 - Mark Targets Pending Acknowledgement. Invoked by the policy that reacts to
    ///     Active Targets Updated, published by Nutritional Care. It has no endpoint.
    /// </summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(MarkTargetsPendingAcknowledgementCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 2.4 - Acknowledge Active Targets.</summary>
    Task<Result<CareLink, CareRelationshipError>> Handle(AcknowledgeActiveTargetsCommand command,
        CancellationToken cancellationToken = default);
}
