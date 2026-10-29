using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal.QueryServices;

public class ActiveTargetsCacheQueryService(IActiveTargetsCacheRepository repository)
    : IActiveTargetsCacheQueryService
{
    public async Task<ActiveTargetsCache?> Handle(GetActiveTargetsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByPatientIdAsync(query.PatientId, cancellationToken);
    }
}

/// <summary>
///     IN-8. «¿Estaba en tu plan?» suggested from the published contract and the day's diary, without AI: the
///     restrictions of the plan through <see cref="IRestrictionLexicon" /> and what is left today as the meal ideas
///     compute it (<see cref="DailyIntakeCalculator" />).
/// </summary>
public class PlanFitQueryService(
    IActiveTargetsCacheRepository targetsRepository,
    IDiaryEntryRepository diaryEntryRepository,
    IFoodCatalogContextFacade foodCatalogContextFacade,
    IRestrictionLexicon lexicon,
    TimeProvider timeProvider) : IPlanFitQueryService
{
    /// <summary>IN-8: how many foods one request may ask about (a photo asks for its dish and three alternatives).</summary>
    public const int MaximumCandidates = 10;

    private const decimal NutrientBasisGrams = 100m;

    public async Task<Result<IReadOnlyList<PlanFit?>, IntakeError>> Handle(GetPlanFitsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.PatientId <= 0) return Failure(IntakeError.PatientWriteOnly);

        // The patient's day: today on their device, one day either side of the server's UTC date (as IA-3).
        var serverToday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (query.LocalDate is { } day && Math.Abs(day.DayNumber - serverToday.DayNumber) > 1)
            return Failure(IntakeError.InvalidLocalDate);

        var targets = await targetsRepository.FindByPatientIdAsync(query.PatientId, cancellationToken);
        if (targets is null) return Failure(IntakeError.ActiveTargetsCacheNotFound);

        RemainingTargets? remaining = null;
        if (query.LocalDate is { } localDate)
        {
            var entries = await diaryEntryRepository.ListByPatientIdAsync(query.PatientId, localDate,
                cancellationToken);
            var eaten = await DailyIntakeCalculator.SumAsync(entries, foodCatalogContextFacade, cancellationToken);
            remaining = RemainingTargets.Of(targets.EnergyKcal, targets.ProteinG, targets.CarbG, targets.FatG,
                eaten.EnergyKcal, eaten.ProteinG, eaten.CarbG, eaten.FatG);
        }

        var fits = new List<PlanFit?>();
        foreach (var candidate in query.Candidates.Take(MaximumCandidates))
        {
            var food = candidate.ReferenceFoodId > 0 && candidate.Grams > 0
                ? await foodCatalogContextFacade.GetReferenceFoodById(candidate.ReferenceFoodId, cancellationToken)
                : null;
            if (food is null)
            {
                fits.Add(null);
                continue;
            }

            var energy = food.EnergyKcalPer100g * candidate.Grams / NutrientBasisGrams;
            var names = (candidate.AlsoKnownAs ?? []).Prepend(food.LocalName);
            fits.Add(PlanFitRules.Evaluate(names, energy, remaining, targets.Restrictions, lexicon));
        }

        return new Result<IReadOnlyList<PlanFit?>, IntakeError>.Success(fits);
    }

    private static Result<IReadOnlyList<PlanFit?>, IntakeError> Failure(IntakeError error)
    {
        return new Result<IReadOnlyList<PlanFit?>, IntakeError>.Failure(error);
    }
}

public class DiaryEntryQueryService(
    IDiaryEntryRepository repository,
    IFoodCatalogContextFacade foodCatalogContextFacade) : IDiaryEntryQueryService
{
    public async Task<DiaryEntry?> Handle(GetDiaryEntryByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByIdAsync(query.DiaryEntryId, cancellationToken);
    }

    public async Task<IEnumerable<DiaryEntry>> Handle(GetDiaryEntriesByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, query.Date, cancellationToken);
    }

    public async Task<IEnumerable<DiaryEntry>> Handle(GetDiaryEntriesByPatientIdBetweenQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.To < query.From) return [];
        return await repository.ListByPatientIdBetweenAsync(query.PatientId, query.From, query.To,
            cancellationToken);
    }

    public async Task<IEnumerable<DiaryEntry>> Handle(GetPendingSyncQueueByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListUnreconciledByPatientIdAsync(query.PatientId, cancellationToken);
    }

    /// <remarks>
    ///     The names are resolved through the Food Catalog facade, which degrades to null, so a food it
    ///     cannot resolve simply has no label. Moved here from the Intake facade (IN-1) so that the
    ///     facade and the REST read models share one resolution.
    /// </remarks>
    public async Task<IReadOnlyDictionary<int, string>> Handle(GetFoodNamesQuery query,
        CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<int, string>();

        foreach (var referenceFoodId in query.ReferenceFoodIds.Where(id => id > 0).Distinct())
        {
            var food = await foodCatalogContextFacade.GetReferenceFoodById(referenceFoodId, cancellationToken);
            if (food is not null) names[referenceFoodId] = food.LocalName;
        }

        return names;
    }
}

public class SelfWeighInQueryService(ISelfWeighInRepository repository) : ISelfWeighInQueryService
{
    public async Task<IReadOnlyList<int>> Handle(GetPatientIdsWithSelfWeighInsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListPatientIdsAsync(cancellationToken);
    }

    public async Task<IEnumerable<SelfWeighIn>> Handle(GetSelfWeighInsByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.ListByPatientIdAsync(query.PatientId, cancellationToken);
    }
}

public class WeightTrendQueryService(
    IWeightTrendRepository repository,
    ISelfWeighInRepository selfWeighInRepository,
    ISelfWeighInProtocolProvider protocolProvider,
    TimeProvider timeProvider) : IWeightTrendQueryService
{
    public async Task<WeightTrend?> Handle(GetWeightTrendByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return await repository.FindByPatientIdAsync(query.PatientId, cancellationToken);
    }

    /// <remarks>
    ///     DECISIÓN IN-5: the range ends on today's UTC date. This context keeps no clinical time zone, and at
    ///     the edge of a 4-week range a few hours move nothing that a weekly slope can show.
    /// </remarks>
    public async Task<WeightTrendView?> Handle(GetWeightTrendRangeByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var trend = await repository.FindByPatientIdAsync(query.PatientId, cancellationToken);
        if (trend is null) return null;

        var weighIns = await selfWeighInRepository.ListByPatientIdAsync(query.PatientId, cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return new WeightTrendView(trend,
            trend.SummarizeLastWeeks(today, query.Weeks, weighIns, protocolProvider.Current));
    }

    public async Task<WeightTrendView?> Handle(GetWeightTrendBetweenByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.To < query.From) return null;
        var trend = await repository.FindByPatientIdAsync(query.PatientId, cancellationToken);
        if (trend is null) return null;

        var weighIns = await selfWeighInRepository.ListByPatientIdAsync(query.PatientId, cancellationToken);
        return new WeightTrendView(trend,
            trend.SummarizeBetween(query.From, query.To, weighIns, protocolProvider.Current));
    }
}
