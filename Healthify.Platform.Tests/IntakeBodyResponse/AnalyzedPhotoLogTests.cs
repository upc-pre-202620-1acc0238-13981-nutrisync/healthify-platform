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

/// <summary>
///     IN-7, registration: a photo log that references an analysis takes its proposal from the analysis and keeps it
///     beside what the patient confirmed (Proposal Kept Alongside Confirmation); the analysis must be the patient's
///     and alive; and the same clientEntryId (photo-logs, manual-logs, manual-logs/batch) never creates a second
///     entry nor evaluates the day again.
/// </summary>
public class AnalyzedPhotoLogTests
{
    private const int PatientId = 10;
    private const int LomoId = 5;
    private const int ArrozId = 6;
    private const long GenerationId = 41;

    private readonly InMemoryDiary _diary = new();
    private readonly InMemoryMealPhotoAnalyses _analyses = new();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly MealPhotoAnalysis _analysis;

    public AnalyzedPhotoLogTests()
    {
        _analysis = new MealPhotoAnalysis(PatientId, LomoId, 320m, new Confidence(0.82m),
            [new MealPhotoAlternative("Arroz blanco cocido", 150m, ArrozId)], GenerationId, _clock.GetUtcNow().AddHours(24));
        _analyses.Seed(_analysis);
    }

    [Fact]
    public async Task The_proposal_comes_from_the_analysis_and_is_kept_beside_the_patients_adjustment()
    {
        // The body proposes something else: it is ignored.
        var entry = Success(await Service().Handle(PhotoLog(new PhotoConfirmation(PhotoConfirmation.Adjusted, LomoId,
            300m)) with { ReferenceFoodId = 999, PortionGrams = 50m, Confidence = 0.1m, PhotoRef = "device-ref" }));

        Assert.Equal((LomoId, 320m, 0.82m), (entry.ProposedReferenceFoodId, entry.ProposedPortionGrams,
            entry.ProposedConfidence));
        Assert.Equal((LomoId, 300m), (entry.ConfirmedReferenceFoodId, entry.ConfirmedPortionGrams));
        Assert.Equal((_analysis.Id, GenerationId), (entry.MealPhotoAnalysisId, entry.ProposedAiGenerationId));
        Assert.Null(entry.PhotoRef);
        Assert.Equal(PlanAdherence.InPlan, entry.PlanAdherence.Value);
        Assert.True(Assert.Single(Fakes.Published(_mediator).OfType<MealLogged>()).ConfirmedOnCreation);
        Assert.Single(Fakes.Published(_mediator).OfType<EstimateAdjustedByPatient>());
        Assert.Single(Fakes.Published(_mediator).OfType<EstimateConfirmedByPatient>());
    }

    [Fact]
    public async Task Confirmed_as_proposed_and_waiting_for_confirmation_both_store_the_ais_proposal()
    {
        var confirmed = Success(await Service().Handle(PhotoLog(new PhotoConfirmation(PhotoConfirmation.AsProposed,
            null, null))));
        Assert.Equal((LomoId, 320m, LomoId, 320m), (confirmed.ProposedReferenceFoodId, confirmed.ProposedPortionGrams,
            confirmed.ConfirmedReferenceFoodId, confirmed.ConfirmedPortionGrams));

        var second = new MealPhotoAnalysis(PatientId, ArrozId, 150m, new Confidence(0.6m), [], GenerationId + 1,
            _clock.GetUtcNow().AddHours(24));
        _analyses.Seed(second);
        var pending = Success(await Service().Handle(PhotoLog(null, second.Id, plan: null)));

        // «Por confirmar»: the policy stores the proposal from the event, which carries the analysis' values.
        Assert.False(pending.HasConfirmedEstimate);
        Assert.Equal(second.Id, pending.MealPhotoAnalysisId);
        var logged = Fakes.Published(_mediator).OfType<MealLogged>().Last();
        Assert.Equal((ArrozId, 150m, 0.6m, false), (logged.ProposedReferenceFoodId, logged.ProposedPortionGrams,
            logged.ProposedConfidence, logged.ConfirmedOnCreation));
    }

    [Fact]
    public async Task An_analysis_of_another_patient_or_unknown_is_404_and_an_expired_one_is_422()
    {
        var foreign = new MealPhotoAnalysis(PatientId + 1, LomoId, 320m, new Confidence(0.8m), [], GenerationId,
            _clock.GetUtcNow().AddHours(24));
        _analyses.Seed(foreign);
        var expired = new MealPhotoAnalysis(PatientId, LomoId, 320m, new Confidence(0.8m), [], GenerationId,
            _clock.GetUtcNow().AddHours(1));
        _analyses.Seed(expired);

        Assert.Equal(IntakeError.MealPhotoAnalysisNotFound, Error(await Service().Handle(PhotoLog(As(), foreign.Id))));
        Assert.Equal(IntakeError.MealPhotoAnalysisNotFound, Error(await Service().Handle(PhotoLog(As(), Guid.NewGuid()))));
        _clock.Now += TimeSpan.FromHours(2);
        Assert.Equal(IntakeError.MealPhotoAnalysisExpired, Error(await Service().Handle(PhotoLog(As(), expired.Id))));

        Assert.Empty(_diary.Stored);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task An_analysis_is_logged_once_logging_it_again_returns_the_first_entry()
    {
        var first = Success(await Service().Handle(PhotoLog(As())));
        _mediator.ClearReceivedCalls();

        var again = Success(await Service().Handle(PhotoLog(As())));

        Assert.Same(first, again);
        Assert.Single(_diary.Stored);
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public async Task A_photo_log_retried_with_the_same_client_entry_id_does_not_duplicate_nor_reevaluate_the_day()
    {
        var clientEntryId = Guid.NewGuid();
        var first = Success(await Service().Handle(PhotoLog(As()) with { ClientEntryId = clientEntryId }));
        _mediator.ClearReceivedCalls();

        // The retry arrives after the analysis expired: the stored entry is still the answer.
        _clock.Now += TimeSpan.FromHours(30);
        var retry = Success(await Service().Handle(PhotoLog(As()) with { ClientEntryId = clientEntryId }));

        Assert.Same(first, retry);
        Assert.Equal(clientEntryId, Assert.Single(_diary.Stored).ClientEntryId);
        Assert.Empty(_mediator.ReceivedCalls()); // no MealLogged, no EstimateConfirmedByPatient: the day is not re-evaluated
    }

    [Fact]
    public async Task A_legacy_photo_log_and_a_manual_log_are_idempotent_by_client_entry_id_too()
    {
        var photoId = Guid.NewGuid();
        var legacy = new LogMealByPhotoCommand(PatientId, _clock.GetUtcNow().AddHours(-1), "ref", LomoId, 300m, 0.7m,
            ClientEntryId: photoId);
        Assert.Same(Success(await Service().Handle(legacy)), Success(await Service().Handle(legacy)));

        var manualId = Guid.NewGuid();
        var manual = new LogMealManuallyCommand(PatientId, _clock.GetUtcNow().AddHours(-1), ArrozId, 150m,
            PlanAdherence.OffPlan, manualId);
        var first = Success(await Service().Handle(manual));
        var publishedAfterFirst = Fakes.Published(_mediator).Count;
        Assert.Same(first, Success(await Service().Handle(manual)));

        Assert.Equal(2, _diary.Stored.Count);
        Assert.Equal(publishedAfterFirst, Fakes.Published(_mediator).Count);

        // Another patient's identifier is a client defect, never their entry.
        Assert.Equal(IntakeError.DuplicatedClientEntryId,
            Error(await Service().Handle(manual with { PatientId = PatientId + 1 })));
        Assert.Equal(IntakeError.ClientEntryIdRequired,
            Error(await Service().Handle(manual with { ClientEntryId = Guid.Empty })));
    }

    [Fact]
    public async Task A_meal_group_resent_with_the_same_client_entry_ids_returns_its_entries()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var command = Group(ids);

        var first = GroupSuccess(await Service().Handle(command));
        var published = Fakes.Published(_mediator).Count;
        var retry = GroupSuccess(await Service().Handle(command));

        Assert.Equal(first.MealGroupId, retry.MealGroupId);
        Assert.Equal(first.Entries.Select(e => e.ClientEntryId), retry.Entries.Select(e => e.ClientEntryId));
        Assert.Equal(2, _diary.Stored.Count);
        Assert.Equal(published, Fakes.Published(_mediator).Count);

        // Half a match, or an identifier repeated in the request, is a conflict.
        Assert.Equal(IntakeError.DuplicatedClientEntryId,
            GroupError(await Service().Handle(Group([ids[0], Guid.NewGuid()]))));
        Assert.Equal(IntakeError.DuplicatedClientEntryId,
            GroupError(await Service().Handle(Group([ids[0], ids[0]]))));
        Assert.Equal(2, _diary.Stored.Count);
    }

    [Fact]
    public async Task A_log_that_loses_the_race_against_its_own_retry_returns_the_winner()
    {
        var clientEntryId = Guid.NewGuid();
        var winner = new DiaryEntry(PatientId, new LocalTimestamp(_clock.GetUtcNow().AddHours(-1)),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced), clientEntryId: clientEntryId);
        // The retry committed between our lookup and our save.
        _diary.OnFirstCommit = () => _diary.SeedCommitted(winner);

        var result = Success(await Service().Handle(new LogMealManuallyCommand(PatientId,
            _clock.GetUtcNow().AddHours(-1), ArrozId, 150m, PlanAdherence.InPlan, clientEntryId)));

        Assert.Same(winner, result);
        Assert.Same(winner, Assert.Single(_diary.Stored));
        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Fact]
    public void A_photo_analysis_is_recorded_once_and_only_on_a_photo_entry()
    {
        var photo = new DiaryEntry(PatientId, new LocalTimestamp(_clock.GetUtcNow()), new Provenance(Provenance.Photo),
            new SyncState(SyncState.Synced));
        photo.RecordPhotoAnalysis(_analysis.Id, GenerationId);
        Assert.Throws<InvalidOperationException>(() => photo.RecordPhotoAnalysis(Guid.NewGuid(), GenerationId));

        var manual = new DiaryEntry(PatientId, new LocalTimestamp(_clock.GetUtcNow()),
            new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
        Assert.Throws<InvalidOperationException>(() => manual.RecordPhotoAnalysis(_analysis.Id, GenerationId));
        Assert.Throws<ArgumentException>(() => photo.RecordPhotoAnalysis(Guid.Empty, GenerationId));
    }

    private DiaryEntryCommandService Service()
    {
        return new DiaryEntryCommandService(_diary, _diary,
            Fakes.Catalog(Fakes.Food(LomoId, "Lomo saltado", 180m), Fakes.Food(ArrozId, "Arroz blanco cocido", 130m)),
            new ConfigurationBuilder().Build(), NullLogger<DiaryEntryCommandService>.Instance, _mediator, _analyses,
            _clock);
    }

    private static PhotoConfirmation As()
    {
        return new PhotoConfirmation(PhotoConfirmation.AsProposed, null, null);
    }

    private LogMealByPhotoCommand PhotoLog(PhotoConfirmation? confirmation, Guid? analysisId = null,
        string? plan = PlanAdherence.InPlan)
    {
        return new LogMealByPhotoCommand(PatientId, _clock.GetUtcNow().AddHours(-1), null, 0, 0m, 0m, confirmation,
            plan, analysisId ?? _analysis.Id);
    }

    private LogMealGroupManuallyCommand Group(IReadOnlyList<Guid> ids)
    {
        return new LogMealGroupManuallyCommand(PatientId, _clock.GetUtcNow().AddHours(-1), PlanAdherence.InPlan, null,
            ids.Select((id, i) => new MealGroupItem(i == 0 ? LomoId : ArrozId, 100m, id)).ToList());
    }

    private static DiaryEntry Success(Result<DiaryEntry, IntakeError> result)
    {
        return Assert.IsType<Result<DiaryEntry, IntakeError>.Success>(result).Value;
    }

    private static IntakeError Error(Result<DiaryEntry, IntakeError> result)
    {
        return Assert.IsType<Result<DiaryEntry, IntakeError>.Failure>(result).Error;
    }

    private static MealGroupLogOutcome GroupSuccess(Result<MealGroupLogOutcome, IntakeError> result)
    {
        return Assert.IsType<Result<MealGroupLogOutcome, IntakeError>.Success>(result).Value;
    }

    private static IntakeError GroupError(Result<MealGroupLogOutcome, IntakeError> result)
    {
        return Assert.IsType<Result<MealGroupLogOutcome, IntakeError>.Failure>(result).Error;
    }

    /// <summary>
    ///     The diary in memory, with the two unique indexes of IN-7 enforced at commit (client entry id and photo
    ///     analysis), as MySQL does. Reads see only what was committed.
    /// </summary>
    private sealed class InMemoryDiary : IDiaryEntryRepository, IUnitOfWork
    {
        private readonly List<DiaryEntry> _pending = [];
        private readonly List<DiaryEntry> _stored = [];
        private int _nextId = 1;

        public IReadOnlyList<DiaryEntry> Stored => _stored.ToList();

        /// <summary>Runs once, just before the first commit.</summary>
        public Action? OnFirstCommit { get; set; }

        public void SeedCommitted(DiaryEntry entry)
        {
            Identity.Assign(entry, new DiaryEntryId(_nextId++));
            _stored.Add(entry);
        }

        public Task AddAsync(DiaryEntry entity, CancellationToken cancellationToken = default)
        {
            _pending.Add(entity);
            return Task.CompletedTask;
        }

        public Task<DiaryEntry?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_stored.FirstOrDefault(e => e.Id.Value == id));
        }

        public void Update(DiaryEntry entity)
        {
        }

        public void Remove(DiaryEntry entity)
        {
            throw new InvalidOperationException("Entry Never Deleted.");
        }

        public Task<IEnumerable<DiaryEntry>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DiaryEntry>>(_stored.ToList());
        }

        public Task<IEnumerable<DiaryEntry>> ListByPatientIdAsync(int patientId, DateOnly? date,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DiaryEntry>>(_stored.Where(e => e.PatientId == patientId).ToList());
        }

        public Task<DiaryEntry?> FindByClientEntryIdAsync(Guid clientEntryId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_stored.FirstOrDefault(e => e.ClientEntryId == clientEntryId));
        }

        public Task<IEnumerable<DiaryEntry>> ListUnreconciledByPatientIdAsync(int patientId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DiaryEntry>>([]);
        }

        public Task<IReadOnlyList<int>> ListMostLoggedReferenceFoodIdsAsync(int minimumPatients, int max,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        public Task<DiaryEntry?> FindByMealPhotoAnalysisIdAsync(Guid analysisId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_stored.FirstOrDefault(e => e.MealPhotoAnalysisId == analysisId));
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (OnFirstCommit is { } once)
            {
                OnFirstCommit = null;
                once();
            }

            var duplicate = _pending.Any(p =>
                _stored.Any(s => (p.ClientEntryId is not null && s.ClientEntryId == p.ClientEntryId) ||
                                 (p.MealPhotoAnalysisId is not null && s.MealPhotoAnalysisId == p.MealPhotoAnalysisId)));
            if (duplicate)
            {
                // As the failed SaveChanges: nothing of this request is stored, and the entity stays tracked.
                throw new InvalidOperationException("Duplicate entry for a unique index of diary_entries.");
            }

            foreach (var entry in _pending.Where(e => e.Id is null)) Identity.Assign(entry, new DiaryEntryId(_nextId++));
            _stored.AddRange(_pending);
            _pending.Clear();
            return Task.CompletedTask;
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            return work(cancellationToken);
        }
    }
}
