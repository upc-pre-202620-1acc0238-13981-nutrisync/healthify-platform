using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Acl;
using Healthify.Platform.MonitoringAdherence.Application.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.Internal.EventHandlers;
using Healthify.Platform.MonitoringAdherence.Application.Internal.QueryServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     MA-2. The agenda of Monitoring with the real command and query services over an in-memory repository, the
///     policy on Consultation Completed and the missed-visit worker, all wired as in production.
/// </summary>
public sealed class InMemoryScheduledFollowUps
{
    public InMemoryScheduledFollowUps(IFollowUpCalendar calendar)
    {
        Repository = new FollowUpRepository(Rows);
        CareRelationship.IsCareLinkActive(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        Commands = new ScheduledFollowUpCommandService(Repository, CareRelationship, calendar, UnitOfWork,
            NullLogger<ScheduledFollowUpCommandService>.Instance, Mediator);
        Queries = new ScheduledFollowUpQueryService(Repository, Iam);
        CheckInRepository = new CheckInRepositoryInMemory(CheckIns);
        CheckInCommands = new PreVisitCheckInCommandService(CheckInRepository, Repository, UnitOfWork, Clock,
            NullLogger<PreVisitCheckInCommandService>.Instance, Mediator);
        CheckInQueries = new PreVisitCheckInQueryService(CheckInRepository, Repository, Clock);
    }

    /// <summary>MA-4. The clock of the check in services; tests move it to cross the hour of a visit.</summary>
    public MutableTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>MA-4. The stored check ins.</summary>
    public List<PreVisitCheckIn> CheckIns { get; } = [];

    public IPreVisitCheckInRepository CheckInRepository { get; }
    public IPreVisitCheckInCommandService CheckInCommands { get; }
    public IPreVisitCheckInQueryService CheckInQueries { get; }

    /// <summary>MA-4. The Monitoring facade over these services, as other contexts read it.</summary>
    public MonitoringContextFacade Facade()
    {
        return new MonitoringContextFacade(Substitute.For<IEvaluationWindowQueryService>(),
            Substitute.For<IConsistencyIndexQueryService>(), Substitute.For<IReferralQueryService>(), Queries,
            CheckInQueries);
    }

    public List<ScheduledFollowUp> Rows { get; } = [];
    public IScheduledFollowUpRepository Repository { get; }
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public IMediator Mediator { get; } = Substitute.For<IMediator>();
    public ICareRelationshipContextFacade CareRelationship { get; } = Substitute.For<ICareRelationshipContextFacade>();
    public IIamContextFacade Iam { get; } = Substitute.For<IIamContextFacade>();
    public IScheduledFollowUpCommandService Commands { get; }
    public IScheduledFollowUpQueryService Queries { get; }

    /// <summary>The policy on Consultation Completed, opening its scope as in production.</summary>
    public OnConsultationCompletedHandler Handler()
    {
        return new OnConsultationCompletedHandler(Fakes.ScopeFactoryWith(Commands),
            NullLogger<OnConsultationCompletedHandler>.Instance);
    }

    /// <summary>A visit on the calendar, then moved to <paramref name="scheduledFor" /> as if time had passed.</summary>
    public ScheduledFollowUp Add(int patientId, int practitionerId, DateTimeOffset scheduledFor,
        params string[] preparation)
    {
        var followUp = new ScheduledFollowUp(new ScheduleFollowUpCommand(patientId, practitionerId,
            DateTimeOffset.UtcNow.AddDays(1), preparation));
        typeof(ScheduledFollowUp).GetProperty(nameof(ScheduledFollowUp.ScheduledFor))!.SetValue(followUp,
            scheduledFor);
        Rows.Add(Identity.Assign(followUp, new FollowUpId(Rows.Count + 1)));
        return followUp;
    }

    /// <summary>
    ///     One cycle of the real missed-visit worker, with its own scope. Since .NET 10 a BackgroundService runs
    ///     ExecuteAsync on its own thread, so the cycle is awaited through the end of its overdue batch: the
    ///     worker reads the overdue visits once per cycle and then flags each, and the cycle is over when every
    ///     visit it read has been asked to be flagged.
    /// </summary>
    public async Task RunMissedFollowUpCycleAsync()
    {
        var cycleDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = new SignallingQueries(Queries, Commands, cycleDone);
        var worker = new MissedFollowUpHostedService(
            Fakes.ScopeFactoryWith((typeof(IScheduledFollowUpQueryService), queries),
                (typeof(IScheduledFollowUpCommandService), queries)),
            new ConfigurationBuilder().Build(), NullLogger<MissedFollowUpHostedService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await cycleDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Passes the worker's two calls through to the real services and signals once every overdue visit of
    ///     the cycle has been handled (or right away when there was none).
    /// </summary>
    private sealed class SignallingQueries(
        IScheduledFollowUpQueryService queries,
        IScheduledFollowUpCommandService commands,
        TaskCompletionSource cycleDone) : IScheduledFollowUpQueryService, IScheduledFollowUpCommandService
    {
        private int _pending;

        public async Task<IEnumerable<ScheduledFollowUp>> Handle(GetOverdueScheduledFollowUpsQuery query,
            CancellationToken cancellationToken = default)
        {
            var overdue = (await queries.Handle(query, cancellationToken)).ToList();
            _pending = overdue.Count;
            if (_pending == 0) cycleDone.TrySetResult();
            return overdue;
        }

        public async Task<Result<ScheduledFollowUp, MonitoringError>> Handle(FlagMissedFollowUpCommand command,
            CancellationToken cancellationToken = default)
        {
            var result = await commands.Handle(command, cancellationToken);
            if (--_pending == 0) cycleDone.TrySetResult();
            return result;
        }

        public Task<IEnumerable<ScheduledFollowUp>> Handle(GetScheduledFollowUpsByPractitionerIdQuery query,
            CancellationToken cancellationToken = default)
        {
            return queries.Handle(query, cancellationToken);
        }

        public Task<IReadOnlyList<FollowUpAgendaEntry>> Handle(GetPractitionerAgendaQuery query,
            CancellationToken cancellationToken = default)
        {
            return queries.Handle(query, cancellationToken);
        }

        public Task<IEnumerable<ScheduledFollowUp>> Handle(GetScheduledFollowUpsByPatientIdsQuery query,
            CancellationToken cancellationToken = default)
        {
            return queries.Handle(query, cancellationToken);
        }

        public Task<ScheduledFollowUp?> Handle(GetScheduledFollowUpByIdQuery query,
            CancellationToken cancellationToken = default)
        {
            return queries.Handle(query, cancellationToken);
        }

        public Task<PatientFollowUpEntry?> Handle(GetNextFollowUpByPatientIdQuery query,
            CancellationToken cancellationToken = default)
        {
            return queries.Handle(query, cancellationToken);
        }

        public Task<IReadOnlyList<PatientFollowUpEntry>> Handle(GetFollowUpsByPatientIdQuery query,
            CancellationToken cancellationToken = default)
        {
            return queries.Handle(query, cancellationToken);
        }

        public Task<Result<ScheduledFollowUp, MonitoringError>> Handle(ScheduleFollowUpCommand command,
            CancellationToken cancellationToken = default)
        {
            return commands.Handle(command, cancellationToken);
        }

        public Task<Result<ScheduledFollowUp, MonitoringError>> Handle(CancelFollowUpCommand command,
            CancellationToken cancellationToken = default)
        {
            return commands.Handle(command, cancellationToken);
        }

        public Task<Result<ScheduledFollowUp, MonitoringError>> Handle(RescheduleFollowUpCommand command,
            CancellationToken cancellationToken = default)
        {
            return commands.Handle(command, cancellationToken);
        }

        public Task<Result<ScheduledFollowUp, MonitoringError>> Handle(
            CompleteFollowUpFromConsultationCommand command, CancellationToken cancellationToken = default)
        {
            return commands.Handle(command, cancellationToken);
        }

        public Task<Result<IReadOnlyList<ScheduledFollowUp>, MonitoringError>> Handle(
            CancelFollowUpsForPatientCommand command, CancellationToken cancellationToken = default)
        {
            return commands.Handle(command, cancellationToken);
        }
    }

    private sealed class FollowUpRepository(List<ScheduledFollowUp> rows) : IScheduledFollowUpRepository
    {
        public Task AddAsync(ScheduledFollowUp entity, CancellationToken cancellationToken = default)
        {
            rows.Add(Identity.Assign(entity, new FollowUpId(rows.Count + 1)));
            return Task.CompletedTask;
        }

        public Task<ScheduledFollowUp?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rows.FirstOrDefault(f => f.Id.Value == id));
        }

        public void Update(ScheduledFollowUp entity)
        {
        }

        public void Remove(ScheduledFollowUp entity)
        {
            rows.Remove(entity);
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ScheduledFollowUp>>(rows.ToList());
        }

        public Task<ScheduledFollowUp?> FindScheduledByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rows.FirstOrDefault(f => f.PatientId == patientId && f.IsScheduled));
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListByPractitionerIdAsync(int practitionerId,
            CancellationToken cancellationToken = default)
        {
            return ListByPractitionerIdAsync(practitionerId, null, null, cancellationToken);
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListByPractitionerIdAsync(int practitionerId,
            FollowUpState? state, DateTimeOffset? from, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ScheduledFollowUp>>(rows
                .Where(f => f.PractitionerId == practitionerId && (state is null || f.State == state) &&
                            (from is null || f.ScheduledFor >= from))
                .OrderBy(f => f.ScheduledFor).ToList());
        }

        public Task<ScheduledFollowUp?> FindOpenForDayAsync(int patientId, int practitionerId,
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rows
                .Where(f => f.PatientId == patientId && f.PractitionerId == practitionerId &&
                            (f.State.IsScheduled || f.State.IsMissed) && f.ScheduledFor >= from &&
                            f.ScheduledFor < to)
                .OrderBy(f => f.ScheduledFor).FirstOrDefault());
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListScheduledByPatientIdsAsync(
            IReadOnlyCollection<int> patientIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ScheduledFollowUp>>(rows
                .Where(f => patientIds.Contains(f.PatientId) && f.IsScheduled).OrderBy(f => f.ScheduledFor)
                .ToList());
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListOpenByPatientAndPractitionerAsync(int patientId,
            int practitionerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ScheduledFollowUp>>(rows
                .Where(f => f.PatientId == patientId && f.PractitionerId == practitionerId &&
                            (f.State.IsScheduled || f.State.IsMissed))
                .OrderByDescending(f => f.ScheduledFor).ToList());
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListByPatientIdAsync(int patientId, FollowUpState? state,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ScheduledFollowUp>>(rows
                .Where(f => f.PatientId == patientId && (state is null || f.State == state))
                .OrderByDescending(f => f.ScheduledFor).ToList());
        }

        public Task<IEnumerable<ScheduledFollowUp>> ListOverdueAsync(DateTimeOffset asOf, int batchSize,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<ScheduledFollowUp>>(rows
                .Where(f => f.IsScheduled && f.ScheduledFor <= asOf).OrderBy(f => f.ScheduledFor).Take(batchSize)
                .ToList());
        }
    }

    /// <summary>MA-4. One check in per visit, as the unique index enforces.</summary>
    private sealed class CheckInRepositoryInMemory(List<PreVisitCheckIn> rows) : IPreVisitCheckInRepository
    {
        public Task AddAsync(PreVisitCheckIn entity, CancellationToken cancellationToken = default)
        {
            if (rows.Any(c => c.FollowUpId == entity.FollowUpId))
                throw new InvalidOperationException("Duplicate entry for ux_pre_visit_check_ins_follow_up_id.");
            rows.Add(Identity.Assign(entity, new PreVisitCheckInId(rows.Count + 1)));
            return Task.CompletedTask;
        }

        public Task<PreVisitCheckIn?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rows.FirstOrDefault(c => c.Id.Value == id));
        }

        public void Update(PreVisitCheckIn entity)
        {
        }

        public void Remove(PreVisitCheckIn entity)
        {
            rows.Remove(entity);
        }

        public Task<IEnumerable<PreVisitCheckIn>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<PreVisitCheckIn>>(rows.ToList());
        }

        public Task<PreVisitCheckIn?> FindByFollowUpIdAsync(int followUpId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rows.FirstOrDefault(c => c.FollowUpId == followUpId));
        }

        public Task<IEnumerable<PreVisitCheckIn>> ListByFollowUpIdsAsync(IReadOnlyCollection<int> followUpIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<PreVisitCheckIn>>(rows.Where(c => followUpIds.Contains(c.FollowUpId))
                .ToList());
        }
    }
}
