using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;

/// <summary>
///     Policy "When Sustained Deviation Detected" (Nutritional Care, Subflow 3.7).
/// </summary>
/// <remarks>
///     Integration event 12 of 13, and one of the two places where the automation of this platform
///     ends on purpose.
///     Business rule: Signal Notifies Never Modifies The Plan (Subflow 3.7), reformulated by NC-10 as "without an
///     explicit action of the practitioner", and invariant 3 of Monitoring and Adherence. This handler resolves
///     exactly one service and issues exactly one command, and that command opens an item in an inbox. It does not
///     resolve the plan command service, and there is no branch in it that could reach one. A signal arrives, a
///     review item is created, and the chain dies there until a person picks it up.
///     NC-10: once the item exists, its AI plan proposal is queued for a background worker. A proposal is a text in
///     the inbox; the plan changes only when the practitioner accepts it, on a different endpoint and service.
///     That a human takes the next step is not a user experience detail. It is what stops an
///     algorithm changing a clinical plan on the strength of an estimate somebody made from a photo.
/// </remarks>
public class OnSustainedDeviationDetectedHandler(
    IServiceScopeFactory scopeFactory,
    IPlanProposalGenerationQueue planProposalGenerationQueue,
    ILogger<OnSustainedDeviationDetectedHandler> logger) : IEventHandler<SustainedDeviationDetected>
{
    public async Task Handle(SustainedDeviationDetected notification, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var commandService = scope.ServiceProvider.GetRequiredService<IReviewItemCommandService>();

        var result = await commandService.Handle(
            new OpenReviewItemCommand(notification.PatientId, SignalType.SustainedDeviation,
                notification.Evidence, EvidenceDataOf(notification)), cancellationToken);

        if (result is not Result<ReviewItem, NutritionalCareError>.Success opened)
        {
            logger.LogInformation(
                "No review item was opened for the sustained deviation of patient {PatientId}",
                notification.PatientId);
            return;
        }

        // NC-10, option A: generated in the background, after the item exists, so this handler never waits for a
        // model. Generating it attaches a text to the item and never touches the plan.
        planProposalGenerationQueue.TryEnqueue(opened.Value.Id.Value);
    }

    /// <summary>
    ///     NC-11. The evidence as numbers too, so the client words it in the practitioner's language. Null when the
    ///     producer predates NC-11 or the numbers do not hold together: the text evidence still opens the item.
    /// </summary>
    private static ReviewItemEvidenceDto? EvidenceDataOf(SustainedDeviationDetected notification)
    {
        try
        {
            var evidence = ReviewItemEvidence.FromSustainedDeviation(notification.Magnitude, notification.Direction,
                notification.DeviatingDaysConsidered, notification.LoggedDaysConsidered,
                notification.MagnitudeEnergyKcal);
            return evidence is null
                ? null
                : new ReviewItemEvidenceDto(evidence.AveragePercentFromTarget, evidence.DeviatedDays,
                    evidence.LoggedDaysConsidered, evidence.Direction,
                    AverageEnergyKcalFromTarget: evidence.AverageEnergyKcalFromTarget);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
