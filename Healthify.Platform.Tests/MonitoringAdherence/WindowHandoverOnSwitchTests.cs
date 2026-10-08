using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Domain.Model.Events;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Application.Internal.QueryServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     CR-1. When a patient switches practitioners the evaluation window is handed over from the
///     previous link to the new one whatever the order of Care Link Revoked and Care Link Established,
///     and even if the revocation policy fails: the patient always ends with exactly one open window,
///     the new link's.
/// </summary>
public class WindowHandoverOnSwitchTests
{
    private const int PatientId = 7;
    private const int PreviousLinkId = 1;
    private const int NewLinkId = 2;

    private readonly InMemoryWindows _repository = new();
    private readonly EvaluationWindowCommandService _commands;
    private readonly EvaluationWindowQueryService _queries;

    public WindowHandoverOnSwitchTests()
    {
        _commands = new EvaluationWindowCommandService(_repository, Substitute.For<IIntakeContextFacade>(),
            Substitute.For<IUnitOfWork>(), new ConfigurationBuilder().Build(),
            NullLogger<EvaluationWindowCommandService>.Instance, Substitute.For<IMediator>());
        _queries = new EvaluationWindowQueryService(_repository);
    }

    [Fact]
    public async Task In_the_usual_order_the_previous_window_closes_and_the_new_one_opens()
    {
        await Established(PreviousLinkId);

        await Revoked(PreviousLinkId);
        await Established(NewLinkId);

        AssertOnlyTheNewWindowIsOpen();
    }

    [Fact]
    public async Task In_reverse_order_the_patient_still_ends_with_only_the_new_window_open()
    {
        await Established(PreviousLinkId);

        // Care Link Established for the new link arrives before Care Link Revoked for the old one.
        await Established(NewLinkId);
        await Revoked(PreviousLinkId);

        AssertOnlyTheNewWindowIsOpen();
    }

    [Fact]
    public async Task If_the_revocation_policy_fails_the_new_link_still_takes_over_the_window()
    {
        await Established(PreviousLinkId);

        var failing = Substitute.For<IEvaluationWindowCommandService>();
        failing.Handle(Arg.Any<CloseEvaluationWindowCommand>(), Arg.Any<CancellationToken>())
            .Returns(new Result<EvaluationWindow, MonitoringError>.Failure(MonitoringError.UnexpectedError));
        await new OnCareLinkRevokedHandler(Fakes.ScopeFactoryWith<IEvaluationWindowCommandService>(failing),
                NullLogger<OnCareLinkRevokedHandler>.Instance)
            .Handle(RevokedEvent(PreviousLinkId), CancellationToken.None);
        Assert.True(Window(PreviousLinkId).IsOpen);

        await Established(NewLinkId);

        AssertOnlyTheNewWindowIsOpen();
    }

    [Fact]
    public async Task A_repeated_care_link_established_does_not_open_a_second_window()
    {
        await Established(NewLinkId);
        var first = Window(NewLinkId);

        await Established(NewLinkId);

        var open = Assert.Single(_repository.Windows, w => w.IsOpen);
        Assert.Same(first, open);
        Assert.Single(_repository.Windows);
    }

    [Fact]
    public async Task A_revocation_for_another_link_never_closes_the_open_window()
    {
        await Established(NewLinkId);

        var result = await _commands.Handle(
            new CloseEvaluationWindowCommand(PatientId, DateTimeOffset.UtcNow, PreviousLinkId));

        var failure = Assert.IsType<Result<EvaluationWindow, MonitoringError>.Failure>(result);
        Assert.Equal(MonitoringError.EvaluationWindowNotFound, failure.Error);
        Assert.True(Window(NewLinkId).IsOpen);
    }

    private void AssertOnlyTheNewWindowIsOpen()
    {
        var open = Assert.Single(_repository.Windows, w => w.IsOpen);
        Assert.Equal(NewLinkId, open.CareLinkId);
        Assert.False(Window(PreviousLinkId).IsOpen);
        Assert.Equal(2, _repository.Windows.Count);
    }

    private EvaluationWindow Window(int careLinkId)
    {
        return Assert.Single(_repository.Windows, w => w.CareLinkId == careLinkId);
    }

    private Task Established(int careLinkId)
    {
        var handler = new OnCareLinkEstablishedHandler(
            Fakes.ScopeFactoryWith((typeof(IEvaluationWindowCommandService), _commands),
                (typeof(IEvaluationWindowQueryService), _queries)),
            NullLogger<OnCareLinkEstablishedHandler>.Instance);
        return handler.Handle(new CareLinkEstablished(careLinkId, PatientId, 40 + careLinkId),
            CancellationToken.None);
    }

    private Task Revoked(int careLinkId)
    {
        var handler = new OnCareLinkRevokedHandler(
            Fakes.ScopeFactoryWith<IEvaluationWindowCommandService>(_commands),
            NullLogger<OnCareLinkRevokedHandler>.Instance);
        return handler.Handle(RevokedEvent(careLinkId), CancellationToken.None);
    }

    private static CareLinkRevoked RevokedEvent(int careLinkId)
    {
        return new CareLinkRevoked(careLinkId, PatientId, 40 + careLinkId, DateTimeOffset.UtcNow,
            "SwitchedPractitioner");
    }

    /// <summary>The windows table, in memory, with the identity EF Core would assign.</summary>
    private sealed class InMemoryWindows : IEvaluationWindowRepository
    {
        public List<EvaluationWindow> Windows { get; } = [];

        public Task AddAsync(EvaluationWindow entity, CancellationToken cancellationToken = default)
        {
            Windows.Add(Identity.Assign(entity, new WindowId(Windows.Count + 1)));
            return Task.CompletedTask;
        }

        public Task<EvaluationWindow?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Windows.FirstOrDefault(w => w.Id.Value == id));
        }

        public void Update(EvaluationWindow entity)
        {
        }

        public void Remove(EvaluationWindow entity)
        {
            throw new InvalidOperationException("Evaluation windows are never removed.");
        }

        public Task<IEnumerable<EvaluationWindow>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<EvaluationWindow>>(Windows);
        }

        public Task<EvaluationWindow?> FindOpenByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Windows.FirstOrDefault(w => w.PatientId == patientId && w.IsOpen));
        }

        public Task<IEnumerable<EvaluationWindow>> ListByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<EvaluationWindow>>(
                Windows.Where(w => w.PatientId == patientId).Reverse().ToList());
        }

        public Task<IEnumerable<EvaluationWindow>> ListOpenAsync(int batchSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<EvaluationWindow>>(Windows.Where(w => w.IsOpen).Take(batchSize).ToList());
        }
    }
}
