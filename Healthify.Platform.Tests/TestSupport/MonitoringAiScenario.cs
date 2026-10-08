using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Application.Internal.CommandServices;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Repositories;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Caching;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Lexicon;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;
using Healthify.Platform.Shared.Infrastructure.Ai.Fakes;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     IA-2/IA-4/IA-5. The AI functions of Monitoring and Adherence with their real pieces: the shared pipeline with
///     its real settings, prompt catalog (the embedded prompts) and audit in memory, the fake language model, the
///     real lexicon, cache and facts reader; the other contexts are substitutes. The clock is Tuesday 15 September
///     2026 (UTC calendar), so the week to summarize is Monday 7 to Sunday 13.
/// </summary>
public sealed class MonitoringAiScenario
{
    public const int PatientId = 10;
    public const int PractitionerId = 20;
    public static readonly DateOnly WeekStart = new(2026, 9, 7);

    public MonitoringAiScenario()
    {
        Consent.IsAllowedAsync(Arg.Any<int>(), Arg.Any<AiFeature>(), Arg.Any<CancellationToken>()).Returns(true);
        Windows.Handle(Arg.Any<GetDailyComplianceByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<DailyCompliance>)Evaluated.ToList());
        Intake.GetDiaryEntryMoments(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<DiaryEntryMomentItem>)Moments
                .Where(m => m.Date >= call.ArgAt<DateOnly>(1) && m.Date <= call.ArgAt<DateOnly>(2)).ToList());
        Intake.GetWeightTrendSummaryBetween(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call => WeightBetween(call.ArgAt<DateOnly>(1), call.ArgAt<DateOnly>(2)));
        Care.GetActiveCareLinkByPatientId(PatientId, Arg.Any<CancellationToken>())
            .Returns(new CareLinkStatusItem(1, PatientId, PractitionerId, true, true, null));
        Care.IsCareLinkActive(PatientId, PractitionerId, Arg.Any<CancellationToken>()).Returns(true);
        Iam.GetPreferredLanguage(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns("es");
    }

    public MutableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero));
    public ClinicalTimeZoneFollowUpCalendar Calendar { get; } = new(TimeZoneInfo.Utc);
    public Dictionary<string, string?> Configuration { get; } = new() { ["Ai:Enabled"] = "true" };
    public IAiConsentPolicy Consent { get; } = Substitute.For<IAiConsentPolicy>();
    public FakeLanguageModelClient Model { get; } = new();
    public InMemoryAiGenerationLog Log { get; } = new();
    public List<DailyCompliance> Evaluated { get; } = [];
    public List<DiaryEntryMomentItem> Moments { get; } = [];
    public WeightTrendSummaryItem? Weight { get; set; } = new(-0.3m, -0.3m, 4);

    /// <summary>The trend the Intake facade answers for a range; by default <see cref="Weight" /> for any range.</summary>
    public Func<DateOnly, DateOnly, WeightTrendSummaryItem?>? WeightByRange { get; set; }

    private WeightTrendSummaryItem? WeightBetween(DateOnly from, DateOnly to)
    {
        return WeightByRange is null ? Weight : WeightByRange(from, to);
    }
    public IEvaluationWindowQueryService Windows { get; } = Substitute.For<IEvaluationWindowQueryService>();
    public IConsistencyIndexQueryService Consistency { get; } = Substitute.For<IConsistencyIndexQueryService>();
    public IIntakeContextFacade Intake { get; } = Substitute.For<IIntakeContextFacade>();
    public ICareRelationshipContextFacade Care { get; } = Substitute.For<ICareRelationshipContextFacade>();
    public IIamContextFacade Iam { get; } = Substitute.For<IIamContextFacade>();
    public INutritionalCareContextFacade NutritionalCare { get; } = Substitute.For<INutritionalCareContextFacade>();
    public IScheduledFollowUpRepository FollowUps { get; } = Substitute.For<IScheduledFollowUpRepository>();
    public IPreVisitCheckInRepository CheckIns { get; } = Substitute.For<IPreVisitCheckInRepository>();
    public InMemoryWeeklySummaries Summaries { get; } = new();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public InMemoryMonitoringAiCache Cache => _cache ??= new InMemoryMonitoringAiCache(Clock);
    private InMemoryMonitoringAiCache? _cache;

    public IAiSettings Settings => new ConfiguredAiSettings(
        new ConfigurationBuilder().AddInMemoryCollection(Configuration).Build(),
        NullLogger<ConfiguredAiSettings>.Instance);

    public IAiGenerationPipeline Pipeline => new AiGenerationPipeline(Settings, Consent,
        PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly), Model, Log, Clock,
        NullLogger<AiGenerationPipeline>.Instance);

    public MonitoringFactsReader Facts => new(Windows, Intake, Calendar, Clock);

    public WeeklySummaryCommandService WeeklySummaries => new(Summaries, UnitOfWork, Facts, Care, Iam, Pipeline,
        Settings, Consent, EmbeddedAiLanguageLexicon.Instance, Clock,
        NullLogger<WeeklySummaryCommandService>.Instance);

    public SuggestedQuestionsCommandService Questions => new(FollowUps, CheckIns, Facts, NutritionalCare, Iam,
        Pipeline, Settings, Consent, Cache, EmbeddedAiLanguageLexicon.Instance, Calendar, Clock,
        NullLogger<SuggestedQuestionsCommandService>.Instance);

    public MonitoringSummaryCommandService MonitoringSummaries => new(Facts, Consistency, Care, NutritionalCare, Iam,
        Pipeline, Settings, Consent, Cache, EmbeddedAiLanguageLexicon.Instance, Clock,
        NullLogger<MonitoringSummaryCommandService>.Instance);

    public MonitoringAiContentCommandService Purge => new(Summaries, UnitOfWork, Cache,
        NullLogger<MonitoringAiContentCommandService>.Instance);

    /// <summary>
    ///     Evaluates the days from <paramref name="from" />, one outcome each (Met, Exceeded, Short or Unlogged),
    ///     with a counted breakfast and lunch and, unless <paramref name="withoutDinner" />, a counted dinner on logged days.
    /// </summary>
    public void LogDays(DateOnly from, string[] outcomes, bool withoutDinner = false)
    {
        for (var i = 0; i < outcomes.Length; i++)
        {
            var date = from.AddDays(i);
            var outcome = outcomes[i];
            var observed = outcome switch
            {
                DailyCompliance.Met => 1800m,
                DailyCompliance.Exceeded => 2300m,
                DailyCompliance.Short => 1200m,
                _ => 0m
            };
            var logged = outcome != DailyCompliance.Unlogged;
            Evaluated.Add(new DailyCompliance(date, outcome, observed, 1800m, 1, logged ? 2 : 0, Clock.GetUtcNow(),
                logged && i == 0 ? 1 : 0));
            if (!logged) continue;

            Moments.Add(Moment(date, 8));
            Moments.Add(Moment(date, 13));
            if (!withoutDinner) Moments.Add(Moment(date, 20));
        }
    }

    /// <summary>The week of <see cref="WeekStart" />: Met on 5 days, Short on Thursday, Unlogged on Sunday.</summary>
    public void LogTheWeek()
    {
        LogDays(WeekStart, ["Met", "Met", "Met", "Short", "Met", "Met", "Unlogged"]);
    }

    private static DiaryEntryMomentItem Moment(DateOnly date, int hour)
    {
        return new DiaryEntryMomentItem(date,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(-5)), true, "InPlan");
    }
}

/// <summary>IA-2. <see cref="IWeeklySummaryRepository" /> in memory, with the identity EF would assign.</summary>
public sealed class InMemoryWeeklySummaries : IWeeklySummaryRepository
{
    private readonly List<WeeklySummary> _rows = [];
    private int _nextId = 1;

    public IReadOnlyList<WeeklySummary> Rows => _rows.ToList();

    public Task AddAsync(WeeklySummary entity, CancellationToken cancellationToken = default)
    {
        if (_rows.Any(s => s.PatientId == entity.PatientId && s.WeekStart == entity.WeekStart))
            throw new InvalidOperationException("Duplicate (patient_id, week_start).");
        Identity.Assign(entity, new WeeklySummaryId(_nextId++));
        _rows.Add(entity);
        return Task.CompletedTask;
    }

    public Task<WeeklySummary?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_rows.FirstOrDefault(s => s.Id.Value == id));
    }

    public void Update(WeeklySummary entity)
    {
    }

    public void Remove(WeeklySummary entity)
    {
        _rows.Remove(entity);
    }

    public Task<IEnumerable<WeeklySummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<WeeklySummary>>(_rows.ToList());
    }

    public Task<WeeklySummary?> FindLatestByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_rows.Where(s => s.PatientId == patientId).MaxBy(s => s.WeekStart));
    }

    public Task<WeeklySummary?> FindByPatientIdAndWeekStartAsync(int patientId, DateOnly weekStart,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_rows.FirstOrDefault(s => s.PatientId == patientId && s.WeekStart == weekStart));
    }

    public Task<IEnumerable<WeeklySummary>> ListByPatientIdAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<WeeklySummary>>(_rows.Where(s => s.PatientId == patientId).ToList());
    }

    public Task<IEnumerable<WeeklySummary>> ListGeneratedBeforeAsync(DateTimeOffset cut, int batchSize,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<WeeklySummary>>(_rows.Where(s => s.GeneratedAt < cut)
            .OrderBy(s => s.GeneratedAt).Take(batchSize).ToList());
    }
}
