using Cortex.Mediator;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Events;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.EventHandlers;
using Healthify.Platform.NutritionalCare.Application.Internal.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-11. The review inbox carries the patient's name, whether a plan proposal is attached, and the evidence as
///     numbers (PR14: "registró en promedio 40 % menos de su meta de energía en 5 de sus últimos 9 días registrados"),
///     besides the original sentence; and it can be read open or resolved (PR13.1).
/// </summary>
public class ReviewInboxEvidenceTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    [Fact]
    public void A_sustained_deviation_below_the_target_reads_as_a_negative_percentage_of_logged_days()
    {
        var evidence = ReviewItemEvidence.FromSustainedDeviation(0.4m, "Below", 5, 9)!;

        Assert.Equal(-40m, evidence.AveragePercentFromTarget);
        Assert.Equal(5, evidence.DeviatedDays);
        Assert.Equal(9, evidence.LoggedDaysConsidered);
        Assert.Equal(ReviewItemEvidence.Below, evidence.Direction);
        Assert.Equal(12.3m, ReviewItemEvidence.FromSustainedDeviation(0.1234m, "above", 3, 7)!.AveragePercentFromTarget);
    }

    [Fact]
    public void Deviated_days_are_logged_days_and_never_exceed_them()
    {
        // Business rule: Only Logged Days Count. An unlogged day can never read as a deviated one.
        Assert.Throws<ArgumentException>(() => new ReviewItemEvidence(-40m, 6, 5, "Below"));
        Assert.Throws<ArgumentException>(() => new ReviewItemEvidence(-40m, 1, 5, "Sideways"));
        Assert.Throws<ArgumentException>(() => new ReviewItemEvidence(null, -1, null, null));
    }

    [Fact]
    public void A_producer_before_NC_11_has_no_counts_and_leaves_the_numbers_out()
    {
        Assert.Null(ReviewItemEvidence.FromSustainedDeviation(0.4m, "Below", 0, 0));
    }

    [Fact]
    public async Task Monitoring_publishes_the_counts_of_the_sustained_deviation()
    {
        var deviation = Identity.Assign(new Deviation(new WindowId(1), PatientId, new DeviationMagnitude(0.4m, 700m),
            new DeviationDirection(DeviationDirection.Below), 9, 5), new DeviationId(3));
        var repository = Substitute.For<IDeviationRepository>();
        repository.FindByIdAsync(3, Arg.Any<CancellationToken>()).Returns(deviation);
        var mediator = Substitute.For<IMediator>();
        var service = new DeviationCommandService(repository, Substitute.For<IEvaluationWindowRepository>(),
            Substitute.For<IUnitOfWork>(), new ConfigurationBuilder().Build(),
            NullLogger<DeviationCommandService>.Instance, mediator);

        await service.Handle(new FlagSustainedDeviationCommand(3));

        var detected = Assert.Single(Fakes.Published(mediator).OfType<SustainedDeviationDetected>());
        Assert.Equal(5, detected.DeviatingDaysConsidered);
        Assert.Equal(9, detected.LoggedDaysConsidered);
        Assert.Equal(DeviationDirection.Below, detected.Direction);
    }

    [Fact]
    public async Task The_policy_opens_the_item_with_the_sentence_and_the_numbers()
    {
        var commandService = Substitute.For<IReviewItemCommandService>();
        OpenReviewItemCommand? sent = null;
        commandService.Handle(Arg.Do<OpenReviewItemCommand>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(new Result<ReviewItem, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError));
        var handler = new OnSustainedDeviationDetectedHandler(Fakes.ScopeFactoryWith(commandService),
            Substitute.For<IPlanProposalGenerationQueue>(),
            NullLogger<OnSustainedDeviationDetectedHandler>.Instance);

        await handler.Handle(new SustainedDeviationDetected(3, PatientId, 0.4m, "Below", "Mean energy 40.0% below", 5,
            9), CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal("Mean energy 40.0% below", sent!.Evidence);
        Assert.Equal(new ReviewItemEvidenceDto(-40m, 5, 9, "Below"), sent.EvidenceData);
    }

    [Fact]
    public async Task Numbers_that_do_not_hold_together_still_open_the_item_with_the_sentence()
    {
        var commandService = Substitute.For<IReviewItemCommandService>();
        OpenReviewItemCommand? sent = null;
        commandService.Handle(Arg.Do<OpenReviewItemCommand>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(new Result<ReviewItem, NutritionalCareError>.Failure(NutritionalCareError.UnexpectedError));
        var handler = new OnSustainedDeviationDetectedHandler(Fakes.ScopeFactoryWith(commandService),
            Substitute.For<IPlanProposalGenerationQueue>(),
            NullLogger<OnSustainedDeviationDetectedHandler>.Instance);

        await handler.Handle(new SustainedDeviationDetected(3, PatientId, 0.4m, "Below", "text", 12, 9),
            CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Null(sent!.EvidenceData);
    }

    [Fact]
    public void The_evidence_is_stored_as_json_and_read_back_unchanged()
    {
        var evidence = new ReviewItemEvidence(-40m, 5, 9, "Below");
        var recheck = new ReviewItemEvidence(null, 2, 6, null, new DateOnly(2026, 9, 8), 4);

        var json = ReviewItemEvidenceJsonConverter.Serialize(evidence);

        Assert.Equal("{\"averagePercentFromTarget\":-40,\"deviatedDays\":5,\"loggedDaysConsidered\":9,\"direction\":\"Below\"}",
            json);
        Assert.Equal(evidence, ReviewItemEvidenceJsonConverter.Deserialize(json));
        Assert.Equal(recheck, ReviewItemEvidenceJsonConverter.Deserialize(ReviewItemEvidenceJsonConverter.Serialize(recheck)));
    }

    [Fact]
    public async Task The_inbox_lists_by_state_with_the_name_of_each_patient()
    {
        var open = Item(1, 10);
        var resolved = Item(2, 11);
        resolved.Resolve(false, null);
        var repository = Substitute.For<IReviewItemRepository>();
        repository.ListByPractitionerIdAndStateAsync(PractitionerId, Arg.Any<ReviewItemState>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new[] { open, resolved }.Where(i => i.State == call.ArgAt<ReviewItemState>(1)).ToList());
        var iam = Substitute.For<IIamContextFacade>();
        iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
            {
                [10] = new(10, "ana@x.pe", "Patient", "Ana", "Flores"),
                [11] = new(11, "luis@x.pe", "Patient")
            });
        var service = new ReviewItemQueryService(repository, iam, Substitute.For<IPlanProposalGenerationQueue>(),
            Substitute.For<INutritionPlanRepository>());

        var openEntries = await service.Handle(new GetReviewItemsByPractitionerIdQuery(PractitionerId,
            new ReviewItemState(ReviewItemState.Open)));
        var resolvedEntries = await service.Handle(new GetReviewItemsByPractitionerIdQuery(PractitionerId,
            new ReviewItemState(ReviewItemState.Resolved)));

        var openEntry = Assert.Single(openEntries);
        Assert.Equal("Ana Flores", openEntry.PatientFullName);
        var resource = ReviewItemResourceAssembler.ToResource(openEntry);
        Assert.Equal("Ana Flores", resource.PatientFullName);
        Assert.False(resource.HasPlanProposal);
        Assert.Equal(-40m, resource.EvidenceData!.AveragePercentFromTarget);
        Assert.Equal("Below", resource.EvidenceData.Direction);
        Assert.Equal("Mean energy 40.0% below", resource.Evidence);

        var resolvedEntry = Assert.Single(resolvedEntries);
        Assert.Equal(2, resolvedEntry.ReviewItem.Id.Value);
        // An account without a name (before IAM-1) shows no name rather than an empty one.
        Assert.Null(resolvedEntry.PatientFullName);
    }

    [Fact]
    public async Task Without_Iam_the_inbox_still_lists_its_items_without_names()
    {
        var repository = Substitute.For<IReviewItemRepository>();
        repository.ListByPractitionerIdAndStateAsync(PractitionerId, Arg.Any<ReviewItemState>(),
            Arg.Any<CancellationToken>()).Returns([Item(1, 10)]);
        var iam = Substitute.For<IIamContextFacade>();
        iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>());

        var entries = await new ReviewItemQueryService(repository, iam, Substitute.For<IPlanProposalGenerationQueue>(),
            Substitute.For<INutritionPlanRepository>()).Handle(
            new GetReviewItemsByPractitionerIdQuery(PractitionerId, new ReviewItemState(ReviewItemState.Open)));

        Assert.Null(Assert.Single(entries).PatientFullName);
    }

    [Theory]
    [InlineData(null, "Open")]
    [InlineData("", "Open")]
    [InlineData("resolved", "Resolved")]
    [InlineData("Open", "Open")]
    public void The_state_filter_defaults_to_the_open_inbox(string? state, string expected)
    {
        Assert.True(ReviewItemQueryAssembler.TryToQuery(PractitionerId, state, out var query));
        Assert.Equal(expected, query.State.Value);
    }

    [Fact]
    public void Any_other_state_is_rejected()
    {
        Assert.False(ReviewItemQueryAssembler.TryToQuery(PractitionerId, "Archived", out _));
    }

    [Fact]
    public void An_item_opened_without_numbers_has_none()
    {
        var item = Identity.Assign(new ReviewItem(
            new OpenReviewItemCommand(PatientId, SignalType.ConsistencyEscalation, "text"), PractitionerId),
            new ReviewItemId(1));

        Assert.Null(item.EvidenceData);
        Assert.Null(ReviewItemResourceAssembler.ToResource(item).EvidenceData);
    }

    private static ReviewItem Item(int id, int patientId)
    {
        return Identity.Assign(new ReviewItem(new OpenReviewItemCommand(patientId, SignalType.SustainedDeviation,
            "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")), PractitionerId),
            new ReviewItemId(id));
    }
}
