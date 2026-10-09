using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     Characterization of the retroactive logging window (Intake:RetroactiveLoggingWindowHours,
///     48 h by default): it applies to the interactive log endpoints and never to synchronization.
/// </summary>
public class RetroactiveLoggingWindowTests
{
    private const int PatientId = 1;

    private readonly IDiaryEntryRepository _repository = Substitute.For<IDiaryEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public RetroactiveLoggingWindowTests()
    {
        _repository.AddAsync(Arg.Do<DiaryEntry>(e => Identity.Assign(e, new DiaryEntryId(1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Entry_older_than_48_hours_is_rejected_by_default()
    {
        var result = await CreateService(WindowHours(null))
            .Handle(PhotoLog(DateTimeOffset.UtcNow.AddHours(-49)));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.RetroactiveLoggingWindowExceeded, failure.Error);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Entry_inside_48_hours_is_accepted_by_default()
    {
        var result = await CreateService(WindowHours(null))
            .Handle(PhotoLog(DateTimeOffset.UtcNow.AddHours(-47)));

        Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result);
    }

    [Fact]
    public async Task Window_is_read_from_configuration()
    {
        var result = await CreateService(WindowHours(24))
            .Handle(PhotoLog(DateTimeOffset.UtcNow.AddHours(-25)));

        var failure = Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result);
        Assert.Equal(IntakeError.RetroactiveLoggingWindowExceeded, failure.Error);
    }

    [Fact]
    public async Task Zero_disables_the_window()
    {
        var result = await CreateService(WindowHours(0))
            .Handle(PhotoLog(DateTimeOffset.UtcNow.AddDays(-30)));

        Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result);
    }

    [Fact]
    public async Task Synchronization_is_exempt_from_the_window()
    {
        var pending = new PendingDiaryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-5),
            Provenance.Photo, "photo-ref", null, null, null);
        _repository.FindByClientEntryIdAsync(pending.ClientEntryId, Arg.Any<CancellationToken>())
            .Returns((DiaryEntry?)null);

        var result = await CreateService(WindowHours(null))
            .Handle(new SyncPendingEntriesCommand(PatientId, [pending]));

        var success = Assert.IsType<Result<SyncOutcome, IntakeError>.Success>(result);
        var outcome = Assert.Single(success.Value.Entries);
        Assert.Equal(SyncedEntryOutcome.Created, outcome.Outcome);
    }

    private DiaryEntryCommandService CreateService(IConfiguration configuration)
    {
        return new DiaryEntryCommandService(_repository, _unitOfWork, Substitute.For<IFoodCatalogContextFacade>(),
            configuration, NullLogger<DiaryEntryCommandService>.Instance, _mediator);
    }

    private static IConfiguration WindowHours(int? hours)
    {
        var values = new Dictionary<string, string?>();
        if (hours is not null) values["Intake:RetroactiveLoggingWindowHours"] = hours.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static LogMealByPhotoCommand PhotoLog(DateTimeOffset localTimestamp)
    {
        return new LogMealByPhotoCommand(PatientId, localTimestamp, "photo-ref", 12, 320m, 0.8m);
    }
}
