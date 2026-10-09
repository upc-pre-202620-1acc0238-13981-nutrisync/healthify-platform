using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>IN-1 on the offline synchronisation, including queues sent by older clients.</summary>
public class SyncPlanAdherenceTests
{
    private const int PatientId = 1;
    private const int CevicheId = 12;

    private readonly IDiaryEntryRepository _repository = Substitute.For<IDiaryEntryRepository>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private DiaryEntry? _added;

    public SyncPlanAdherenceTests()
    {
        _repository.AddAsync(Arg.Do<DiaryEntry>(e => _added = Identity.Assign(e, new DiaryEntryId(1))),
            Arg.Any<CancellationToken>());
        _repository.FindByClientEntryIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DiaryEntry?)null);
    }

    [Fact]
    public async Task Confirmed_entry_with_an_answer_is_stored_with_it()
    {
        var outcome = await Sync(Pending(Provenance.Manual, confirmed: true, answer: "OffPlan"));

        Assert.Equal(SyncedEntryOutcome.Created, outcome.Outcome);
        Assert.True(_added!.HasConfirmedEstimate);
        Assert.True(_added.PlanAdherence.IsOffPlan);
        Assert.Equal(PlanAdherence.OffPlan,
            Fakes.Published(_mediator).OfType<MealLogged>().Single().PlanAdherence);
    }

    [Fact]
    public async Task Unconfirmed_photo_is_stored_as_a_proposal_that_does_not_count()
    {
        var outcome = await Sync(Pending(Provenance.Photo, confirmed: false, answer: null, confidence: 0.7m));

        Assert.Equal(SyncedEntryOutcome.Created, outcome.Outcome);
        Assert.True(_added!.HasProposedEstimate);
        Assert.False(_added.HasConfirmedEstimate);
        Assert.False(_added.PlanAdherence.IsAnswered);
    }

    [Fact]
    public async Task Legacy_off_plan_provenance_is_accepted()
    {
        var outcome = await Sync(new PendingDiaryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-2),
            Provenance.OffPlan, null, null, null, null));

        Assert.Equal(SyncedEntryOutcome.Created, outcome.Outcome);
        Assert.True(_added!.Provenance.IsOffPlan);
        Assert.True(_added.PlanAdherence.IsOffPlan);
        Assert.Contains(Fakes.Published(_mediator), e => e is OffPlanEntryLogged);
    }

    [Fact]
    public async Task Legacy_confirmed_entry_without_an_answer_is_accepted_not_answered()
    {
        var outcome = await Sync(Pending(Provenance.Manual, confirmed: null, answer: null));

        Assert.Equal(SyncedEntryOutcome.Created, outcome.Outcome);
        Assert.True(_added!.HasConfirmedEstimate);
        Assert.False(_added.PlanAdherence.IsAnswered);
    }

    [Fact]
    public async Task An_invalid_answer_rejects_only_that_entry()
    {
        var outcome = await Sync(Pending(Provenance.Manual, confirmed: true, answer: "Sometimes"));

        Assert.Equal(SyncedEntryOutcome.Rejected, outcome.Outcome);
        Assert.Equal(nameof(IntakeError.InvalidPlanAdherence), outcome.Reason);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task A_resend_fills_the_missing_answer_of_an_existing_entry()
    {
        var pending = Pending(Provenance.Manual, confirmed: true, answer: "InPlan");
        var existing = new DiaryEntry(PatientId, new LocalTimestamp(pending.LocalTimestamp),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Pending), null, pending.ClientEntryId);
        existing.ConfirmFromLegacySync(new ConfirmedEstimate(CevicheId, 280m, DateTimeOffset.UtcNow));
        Identity.Assign(existing, new DiaryEntryId(9));
        _repository.FindByClientEntryIdAsync(pending.ClientEntryId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var outcome = await Sync(pending);

        Assert.Equal(SyncedEntryOutcome.ConflictResolved, outcome.Outcome);
        Assert.True(existing.PlanAdherence.IsInPlan);
    }

    private async Task<SyncedEntryOutcome> Sync(PendingDiaryEntry pending)
    {
        var service = new DiaryEntryCommandService(_repository, Substitute.For<IUnitOfWork>(),
            Fakes.Catalog(Fakes.Food(CevicheId, "Ceviche", 120m)), new ConfigurationBuilder().Build(),
            NullLogger<DiaryEntryCommandService>.Instance, _mediator);

        var result = await service.Handle(new SyncPendingEntriesCommand(PatientId, [pending]));

        return Assert.Single(Assert.IsType<Result<SyncOutcome, IntakeError>.Success>(result).Value.Entries);
    }

    private static PendingDiaryEntry Pending(string provenance, bool? confirmed, string? answer,
        decimal? confidence = null)
    {
        return new PendingDiaryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-3), provenance, null,
            CevicheId, 280m, confidence, confirmed, answer);
    }
}
