using Cortex.Mediator;
using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Maintenance;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Protocols;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-3. «¿Te pesaste en ayunas?» is the whole protocol: fasted readings smooth the trend, the two removed
///     questions are optional and null on new readings, the protocol can be widened again by configuration,
///     and existing trends are rebuilt by a one-shot job.
/// </summary>
public class FastedOnlyProtocolTests
{
    private const int PatientId = 7;
    private static readonly DateOnly Day = new(2026, 9, 1);

    [Fact]
    public void Fasted_alone_follows_the_protocol()
    {
        Assert.True(new ProtocolCompliance(true).FollowsProtocol);
        Assert.False(new ProtocolCompliance(false).FollowsProtocol);
    }

    [Fact]
    public void A_legacy_reading_fasted_on_another_scale_now_follows_the_protocol()
    {
        // Before IN-3 this reading was excluded because the scale was not the usual one.
        Assert.True(new ProtocolCompliance(true, true, false).FollowsProtocol);
        Assert.False(new ProtocolCompliance(false, true, true).FollowsProtocol);
    }

    [Fact]
    public void A_configured_condition_left_unanswered_is_not_met()
    {
        var protocol = new SelfWeighInProtocol([SelfWeighInProtocol.Fasted, SelfWeighInProtocol.SameScale]);

        Assert.False(new ProtocolCompliance(true).FollowsUnder(protocol));
        Assert.False(new ProtocolCompliance(true, null, false).FollowsUnder(protocol));
        Assert.True(new ProtocolCompliance(true, null, true).FollowsUnder(protocol));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Fasted,OnTheMoon")]
    public void A_protocol_with_no_or_unknown_conditions_is_rejected(string conditions)
    {
        var list = conditions.Split(',', StringSplitOptions.RemoveEmptyEntries);

        Assert.Throws<ArgumentException>(() => new SelfWeighInProtocol(list));
    }

    [Fact]
    public void The_provider_defaults_to_fasted_only_and_ignores_an_invalid_list()
    {
        Assert.Equal(SelfWeighInProtocol.Default, Provider().Current);
        Assert.Equal(SelfWeighInProtocol.Default, Provider("Fasted", "Gibberish").Current);
        Assert.Equal(["Fasted", "SameScale"], Provider("fasted", "SameScale").Current.Conditions);
    }

    [Fact]
    public void The_trend_takes_fasted_readings_and_excludes_the_rest()
    {
        var trend = new WeightTrend(PatientId);
        var readings = new[]
        {
            WeighIns.Reading(1, PatientId, Day, 80m),
            WeighIns.Reading(2, PatientId, Day.AddDays(1), 79m, sameTimeOfDay: false, sameScale: false),
            WeighIns.Reading(3, PatientId, Day.AddDays(2), 90m, false)
        };

        var excluded = trend.Recalculate(readings);

        Assert.Equal([3], excluded);
        Assert.Equal(2, trend.Points.Count);
        Assert.Equal(79.5m, trend.Points[^1].SmoothedValueKg);
    }

    [Fact]
    public void A_wider_configured_protocol_excludes_readings_that_did_not_answer_it()
    {
        var trend = new WeightTrend(PatientId);
        var protocol = new SelfWeighInProtocol([SelfWeighInProtocol.Fasted, SelfWeighInProtocol.SameScale]);

        var excluded = trend.Recalculate(
            [WeighIns.Reading(1, PatientId, Day, 80m), WeighIns.Reading(2, PatientId, Day, 80m, sameScale: true)],
            protocol);

        Assert.Equal([1], excluded);
    }

    [Fact]
    public async Task A_new_reading_stores_only_fasted_and_announces_it_follows_the_protocol()
    {
        var repository = Substitute.For<ISelfWeighInRepository>();
        SelfWeighIn? added = null;
        _ = repository.AddAsync(Arg.Do<SelfWeighIn>(w => added = Identity.Assign(w, new SelfWeighInId(1))),
            Arg.Any<CancellationToken>());
        var mediator = Substitute.For<IMediator>();
        var service = new SelfWeighInCommandService(repository, Provider(), Substitute.For<IUnitOfWork>(),
            NullLogger<SelfWeighInCommandService>.Instance, mediator);

        var result = await service.Handle(
            new RecordSelfWeighInCommand(PatientId, 78.4m, DateTimeOffset.UtcNow.AddHours(-1), true));

        Assert.True(result.IsSuccess);
        Assert.True(added!.ProtocolFastedState);
        Assert.Null(added.ProtocolSameTimeOfDay);
        Assert.Null(added.ProtocolSameScale);
        Assert.True(Fakes.Published(mediator).OfType<SelfWeighInRecorded>().Single().FollowsProtocol);

        var resource = SelfWeighInResourceAssembler.ToResource(added);
        Assert.Null(resource.SameTimeOfDay);
        Assert.Null(resource.SameScale);
        Assert.True(resource.FollowsProtocol);
    }

    [Fact]
    public void An_older_client_that_still_sends_the_three_answers_keeps_them()
    {
        var command = RecordSelfWeighInCommandAssembler.ToCommand(PatientId,
            new RecordSelfWeighInResource(PatientId, 78m, DateTimeOffset.UtcNow, true, true, false));

        Assert.Equal(new RecordSelfWeighInCommand(PatientId, 78m, command.LocalTimestamp, true, true, false),
            command);
    }

    [Fact]
    public async Task The_one_shot_job_recalculates_every_patient_and_survives_a_failure()
    {
        var queries = Substitute.For<ISelfWeighInQueryService>();
        queries.Handle(Arg.Any<GetPatientIdsWithSelfWeighInsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<int> { 1, 2, 3 });

        var commands = Substitute.For<IWeightTrendCommandService>();
        commands.Handle(Arg.Any<RecalculateWeightTrendCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<WeightTrend, IntakeError>.Success(new WeightTrend(1)));
        commands.Handle(Arg.Is<RecalculateWeightTrendCommand>(c => c.PatientId == 2), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));

        var job = new WeightTrendRecalculationJob(
            Fakes.ScopeFactoryWith((typeof(ISelfWeighInQueryService), queries),
                (typeof(IWeightTrendCommandService), commands)),
            NullLogger<WeightTrendRecalculationJob>.Instance);

        var report = await job.RunAsync();

        Assert.Equal(3, report.Patients);
        Assert.Equal(2, report.Recalculated);
        Assert.Equal([2], report.FailedPatientIds);
        await commands.Received(1).Handle(new RecalculateWeightTrendCommand(3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recalculation_uses_the_configured_protocol()
    {
        var readings = Substitute.For<ISelfWeighInRepository>();
        readings.ListByPatientIdAsync(PatientId, Arg.Any<CancellationToken>())
            .Returns([WeighIns.Reading(1, PatientId, Day, 80m)]);
        var trends = Substitute.For<IWeightTrendRepository>();
        var mediator = Substitute.For<IMediator>();
        var service = new WeightTrendCommandService(trends, readings, Provider("Fasted", "SameScale"),
            Substitute.For<IUnitOfWork>(), NullLogger<WeightTrendCommandService>.Instance, mediator);

        var result = await service.Handle(new RecalculateWeightTrendCommand(PatientId));

        var trend = Assert.IsType<Result<WeightTrend, IntakeError>.Success>(result).Value;
        Assert.False(trend.HasPoints);
        Assert.Single(Fakes.Published(mediator).OfType<SelfWeighInExcludedFromTrend>());
    }

    private static ISelfWeighInProtocolProvider Provider(params string[] conditions)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(conditions.Select((c, i) =>
                new KeyValuePair<string, string?>($"Intake:SelfWeighInProtocol:{i}", c)))
            .Build();
        return new ConfiguredSelfWeighInProtocolProvider(configuration,
            NullLogger<ConfiguredSelfWeighInProtocolProvider>.Instance);
    }
}
