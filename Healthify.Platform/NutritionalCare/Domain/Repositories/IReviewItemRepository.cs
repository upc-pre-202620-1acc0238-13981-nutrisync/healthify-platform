using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.NutritionalCare.Domain.Repositories;

public interface IReviewItemRepository : IBaseRepository<ReviewItem>
{
    /// <summary>Backs the business rule One Open Item Per Patient And Signal Type (Subflow 3.7).</summary>
    Task<bool> ExistsOpenForPatientAndSignalTypeAsync(int patientId, SignalType signalType,
        CancellationToken cancellationToken = default);

    /// <summary>Read model: Practitioner Review Inbox.</summary>
    Task<IEnumerable<ReviewItem>> ListOpenByPractitionerIdAsync(int practitionerId,
        CancellationToken cancellationToken = default);

    /// <summary>NC-11. Read model: Practitioner Review Inbox, open or resolved, most recent first.</summary>
    Task<IEnumerable<ReviewItem>> ListByPractitionerIdAndStateAsync(int practitionerId, ReviewItemState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-10. Items whose scheduled recheck is due and not yet opened, oldest first, at most
    ///     <paramref name="batchSize" />.
    /// </summary>
    Task<IReadOnlyList<ReviewItem>> ListRecheckDueAsync(DateTimeOffset now, int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>NC-10. Whether the item is still open and without a proposal, read from the store (not tracked).</summary>
    Task<bool> ExistsOpenWithoutProposalAsync(int reviewItemId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     NC-10. Open items of <paramref name="signalType" /> without a proposal, created at or after
    ///     <paramref name="createdSince" />, oldest first, at most <paramref name="batchSize" />.
    /// </summary>
    Task<IReadOnlyList<ReviewItem>> ListOpenWithoutProposalAsync(SignalType signalType, DateTimeOffset createdSince,
        int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    ///     IA-8. Items of the patient, in any state, whose AI proposal no practitioner accepted, with the proposal
    ///     loaded (tracked).
    /// </summary>
    Task<IReadOnlyList<ReviewItem>> ListWithUnacceptedProposalByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default);

    Task<int> CountOpenByPractitionerIdAsync(int practitionerId, CancellationToken cancellationToken = default);

    /// <summary>IA-5. Whether an item of this signal type exists for the patient, in any state.</summary>
    Task<bool> ExistsForPatientAndSignalTypeAsync(int patientId, SignalType signalType,
        CancellationToken cancellationToken = default);
}
