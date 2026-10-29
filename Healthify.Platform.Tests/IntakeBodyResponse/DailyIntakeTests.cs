using System.Security.Claims;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     What the patient logged today next to their targets, for the home gauge: confirmed entries only, what is
///     left never below zero, and the day is described, never rated.
/// </summary>
public class DailyIntakeTests
{
    private const int PatientId = 10;
    private static readonly DateOnly Today = new(2026, 9, 15);

    private readonly IActiveTargetsCacheRepository _targets = Substitute.For<IActiveTargetsCacheRepository>();
    private readonly IDiaryEntryRepository _diary = Substitute.For<IDiaryEntryRepository>();
    private readonly IFoodCatalogContextFacade _catalog = Substitute.For<IFoodCatalogContextFacade>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero));

    public DailyIntakeTests()
    {
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(Targets());
        // 121 kcal, 5.2 g protein, 15 g carbohydrate and 4 g fat per 100 g.
        _catalog.GetReferenceFoodById(1, Arg.Any<CancellationToken>())
            .Returns(new ReferenceFoodItem(1, "Arroz con pollo", 121m, 5.2m, 15m, 4m, false));
    }

    [Fact]
    public async Task Only_confirmed_entries_add_to_the_day()
    {
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>())
            .Returns(_ => [Eaten(1000m), Unconfirmed()]);

        var intake = Success(await Service().Handle(new GetDailyIntakeQuery(PatientId, Today)));

        Assert.Equal((1210m, 52m, 150m, 40m), (intake.EnergyKcal, intake.ProteinG, intake.CarbG, intake.FatG));
        Assert.Equal(1, intake.EntryCount);
        // 1800 kcal, 100 g, 200 g and 60 g published.
        Assert.Equal((590m, 48m, 50m, 20m),
            (intake.Remaining.EnergyKcal, intake.Remaining.ProteinG, intake.Remaining.CarbG, intake.Remaining.FatG));
        Assert.Equal((2, 1800m), (intake.PlanVersion, intake.TargetEnergyKcal));
    }

    [Fact]
    public async Task What_is_left_never_goes_below_zero()
    {
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns(_ => [Eaten(2000m)]);

        var intake = Success(await Service().Handle(new GetDailyIntakeQuery(PatientId, Today)));

        Assert.Equal(2420m, intake.EnergyKcal);
        Assert.Equal(0m, intake.Remaining.EnergyKcal);
        Assert.Equal(0m, intake.Remaining.FatG);
    }

    [Fact]
    public async Task A_day_without_entries_is_all_left()
    {
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns(_ => []);

        var intake = Success(await Service().Handle(new GetDailyIntakeQuery(PatientId, Today)));

        Assert.Equal((0m, 0), (intake.EnergyKcal, intake.EntryCount));
        Assert.Equal(1800m, intake.Remaining.EnergyKcal);
    }

    [Fact]
    public async Task Without_a_published_plan_there_is_nothing_to_compare()
    {
        _targets.FindByPatientIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns((ActiveTargetsCache?)null);

        var result = await Service().Handle(new GetDailyIntakeQuery(PatientId, Today));

        Assert.Equal(IntakeError.ActiveTargetsCacheNotFound,
            Assert.IsType<Result<DailyIntake, IntakeError>.Failure>(result).Error);
    }

    [Fact]
    public async Task A_day_that_is_not_today_on_the_device_is_refused()
    {
        var result = await Service().Handle(new GetDailyIntakeQuery(PatientId, Today.AddDays(-3)));

        Assert.Equal(IntakeError.InvalidLocalDate,
            Assert.IsType<Result<DailyIntake, IntakeError>.Failure>(result).Error);
        await _diary.DidNotReceiveWithAnyArgs().ListByPatientIdAsync(default, default, default);
    }

    [Fact]
    public async Task Another_patient_cannot_read_the_day()
    {
        var service = Substitute.For<IDailyIntakeQueryService>();
        var controller = Controller(service);

        var problem = Assert.IsType<ObjectResult>(await controller.GetDailyIntake(PatientId + 1, Today));

        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        await service.DidNotReceiveWithAnyArgs().Handle(default!, default);
    }

    [Fact]
    public async Task The_patient_reads_their_day_with_what_is_left()
    {
        _diary.ListByPatientIdAsync(PatientId, Today, Arg.Any<CancellationToken>()).Returns(_ => [Eaten(1000m)]);
        var controller = Controller(Service());

        var ok = Assert.IsType<OkObjectResult>(await controller.GetDailyIntake(PatientId, Today));

        var resource = Assert.IsType<DailyIntakeResource>(ok.Value);
        Assert.Equal((Today, 590m), (resource.Date, resource.RemainingEnergyKcal));
    }

    private DailyIntakeQueryService Service()
    {
        return new DailyIntakeQueryService(_targets, _diary, _catalog, _clock);
    }

    private static PatientIntakeController Controller(IDailyIntakeQueryService service)
    {
        var localizer = Substitute.For<IStringLocalizer<IntakeMessages>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        return new PatientIntakeController(Substitute.For<IActiveTargetsCacheQueryService>(),
            Substitute.For<IDiaryEntryQueryService>(), Substitute.For<IWeightTrendQueryService>(),
            Substitute.For<IPlanFitQueryService>(), service, localizer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", PatientId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, PatientId.ToString()), new Claim("id", PatientId.ToString())],
                        "test"))
                }
            }
        };
    }

    private static DailyIntake Success(Result<DailyIntake, IntakeError> result)
    {
        return Assert.IsType<Result<DailyIntake, IntakeError>.Success>(result).Value;
    }

    private static ActiveTargetsCache Targets()
    {
        return new ActiveTargetsCache(new RefreshActiveTargetsCacheCommand(PatientId, 2,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), 1800m, 100m, 200m, 60m, [], []));
    }

    private static DiaryEntry Eaten(decimal grams)
    {
        var entry = Unconfirmed();
        entry.ConfirmDirectly(new ConfirmedEstimate(1, grams, DateTimeOffset.UtcNow),
            new PlanAdherence(PlanAdherence.InPlan));
        return entry;
    }

    private static DiaryEntry Unconfirmed()
    {
        return new DiaryEntry(PatientId,
            new LocalTimestamp(new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.FromHours(-5))),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
    }
}
