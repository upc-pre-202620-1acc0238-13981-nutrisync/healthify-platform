using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.EventHandlers;
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

/// <summary>IN-2. A photo meal saved already confirmed, in a single transaction.</summary>
public class PhotoLogConfirmationTests
{
    private const int PatientId = 1;
    private const int CevicheId = 12;
    private const int LomoId = 15;

    private readonly IDiaryEntryRepository _repository = Substitute.For<IDiaryEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public PhotoLogConfirmationTests()
    {
        _repository.AddAsync(Arg.Do<DiaryEntry>(e => Identity.Assign(e, new DiaryEntryId(1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirmed_as_proposed_is_stored_proposed_and_confirmed_in_one_commit()
    {
        var result = await CreateService().Handle(
            PhotoLog(new PhotoConfirmation(PhotoConfirmation.AsProposed, null, null), "InPlan"));

        var entry = Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
        Assert.Equal(320m, entry.ProposedPortionGrams);
        Assert.Equal(320m, entry.ConfirmedPortionGrams);
        Assert.True(entry.PlanAdherence.IsInPlan);
        await _repository.Received(1).AddAsync(Arg.Any<DiaryEntry>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());

        var published = Fakes.Published(_mediator);
        Assert.Equal(2, published.Count);
        var logged = Assert.IsType<MealLogged>(published[0]);
        Assert.True(logged.ConfirmedOnCreation);
        Assert.Equal(320m, logged.ProposedPortionGrams);
        Assert.IsType<EstimateConfirmedByPatient>(published[1]);
    }

    [Fact]
    public async Task Adjusted_keeps_the_proposal_beside_the_correction()
    {
        var result = await CreateService().Handle(
            PhotoLog(new PhotoConfirmation(PhotoConfirmation.Adjusted, LomoId, 300m), "OffPlan"));

        var entry = Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
        Assert.Equal(CevicheId, entry.ProposedReferenceFoodId);
        Assert.Equal(320m, entry.ProposedPortionGrams);
        Assert.Equal(LomoId, entry.ConfirmedReferenceFoodId);
        Assert.Equal(300m, entry.ConfirmedPortionGrams);
        Assert.True(entry.PlanAdherence.IsOffPlan);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());

        var published = Fakes.Published(_mediator);
        Assert.IsType<MealLogged>(published[0]);
        Assert.IsType<EstimateAdjustedByPatient>(published[1]);
        Assert.IsType<EstimateConfirmedByPatient>(published[2]);
    }

    [Fact]
    public async Task Without_confirmation_the_behaviour_is_unchanged()
    {
        var result = await CreateService().Handle(PhotoLog(null, "InPlan"));

        var entry = Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
        Assert.False(entry.HasProposedEstimate);
        Assert.False(entry.HasConfirmedEstimate);
        Assert.False(entry.PlanAdherence.IsAnswered);

        var logged = Assert.IsType<MealLogged>(Assert.Single(Fakes.Published(_mediator)));
        Assert.False(logged.ConfirmedOnCreation);
        Assert.Equal(CevicheId, logged.ProposedReferenceFoodId);
    }

    [Theory]
    [InlineData("Maybe", null, null)]
    [InlineData(PhotoConfirmation.Adjusted, null, 300)]
    [InlineData(PhotoConfirmation.Adjusted, LomoId, null)]
    public async Task An_incomplete_confirmation_is_rejected(string kind, int? foodId, int? grams)
    {
        var result = await CreateService().Handle(
            PhotoLog(new PhotoConfirmation(kind, foodId, grams), "InPlan"));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.InvalidEstimateConfirmation, failure.Error);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task A_confirmation_requires_the_answer()
    {
        var result = await CreateService().Handle(
            PhotoLog(new PhotoConfirmation(PhotoConfirmation.AsProposed, null, null), null));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.PlanAdherenceRequired, failure.Error);
    }

    [Fact]
    public async Task Nothing_is_stored_when_the_correction_cannot_be_resolved()
    {
        var result = await CreateService().Handle(
            PhotoLog(new PhotoConfirmation(PhotoConfirmation.Adjusted, 999, 300m), "InPlan"));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.ReferenceFoodNotResolved, failure.Error);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task The_estimation_policy_skips_an_entry_confirmed_on_creation()
    {
        var commandService = Substitute.For<IDiaryEntryCommandService>();
        commandService.Handle(Arg.Any<EstimatePortionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<DiaryEntry, IntakeError>.Failure(IntakeError.DiaryEntryNotFound));
        var handler = new OnMealLoggedPhotoEstimationHandler(Fakes.ScopeFactoryWith(commandService),
            NullLogger<OnMealLoggedPhotoEstimationHandler>.Instance);

        await handler.Handle(new MealLogged(1, PatientId, DateTimeOffset.UtcNow, Provenance.Photo, CevicheId,
            320m, 0.8m, PlanAdherence.InPlan, ConfirmedOnCreation: true), CancellationToken.None);
        Assert.Empty(commandService.ReceivedCalls());

        await handler.Handle(new MealLogged(2, PatientId, DateTimeOffset.UtcNow, Provenance.Photo, CevicheId,
            320m, 0.8m), CancellationToken.None);
        await commandService.Received(1).Handle(Arg.Any<EstimatePortionCommand>(), Arg.Any<CancellationToken>());
    }

    private DiaryEntryCommandService CreateService()
    {
        return new DiaryEntryCommandService(_repository, _unitOfWork,
            Fakes.Catalog(Fakes.Food(CevicheId, "Ceviche", 120m), Fakes.Food(LomoId, "Lomo saltado", 180m)),
            new ConfigurationBuilder().Build(), NullLogger<DiaryEntryCommandService>.Instance, _mediator);
    }

    private static LogMealByPhotoCommand PhotoLog(PhotoConfirmation? confirmation, string? answer)
    {
        return new LogMealByPhotoCommand(PatientId, DateTimeOffset.UtcNow.AddHours(-1), "photo-ref", CevicheId,
            320m, 0.8m, confirmation, answer);
    }
}
