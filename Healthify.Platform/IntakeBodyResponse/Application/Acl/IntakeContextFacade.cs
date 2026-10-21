using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;

namespace Healthify.Platform.IntakeBodyResponse.Application.Acl;

/// <inheritdoc cref="IIntakeContextFacade" />
/// <remarks>
///     The nutrient totals are computed here rather than stored, because a stored total would be a
///     second source of truth that drifts the moment a catalog entry is corrected. The arithmetic is
///     the plainest possible: the catalog holds nutrients per 100 grams, the entry holds the portion.
///     Only confirmed estimates are counted. A proposal the patient has not spoken about is a model's
///     guess, and adding it to a total would let that guess be read as intake.
/// </remarks>
public class IntakeContextFacade(
    IDiaryEntryQueryService diaryEntryQueryService,
    IWeightTrendQueryService weightTrendQueryService,
    IFoodCatalogContextFacade foodCatalogContextFacade,
    ISelfWeighInQueryService? selfWeighInQueryService = null,
    ISelfWeighInProtocolProvider? protocolProvider = null) : IIntakeContextFacade
{
    private const decimal NutrientBasisGrams = 100m;

    public async Task<DailyIntakeSummaryItem?> GetDailyIntakeSummary(int patientId, DateOnly date,
        CancellationToken ct = default)
    {
        try
        {
            var entries = (await diaryEntryQueryService.Handle(
                new GetDiaryEntriesByPatientIdQuery(patientId, date), ct)).ToList();
            var totals = await DailyIntakeCalculator.SumAsync(entries, foodCatalogContextFacade, ct);

            return new DailyIntakeSummaryItem(
                patientId,
                date,
                totals.EnergyKcal,
                totals.ProteinG,
                totals.CarbG,
                totals.FatG,
                entries.Count,
                entries.Count(e => e.PlanAdherence.IsOffPlan),
                entries.Count > 0,
                totals.OffPlanEnergyKcal);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<WeightTrendPointItem>> GetWeightTrendPoints(int patientId, int days,
        CancellationToken ct = default)
    {
        try
        {
            var trend = await weightTrendQueryService.Handle(
                new GetWeightTrendByPatientIdQuery(patientId), ct);
            if (trend is null) return [];

            return Tail(trend, days);
        }
        catch
        {
            return [];
        }
    }

    public async Task<WeightTrendSummaryItem?> GetWeightTrendSummary(int patientId, int weeks,
        CancellationToken ct = default)
    {
        try
        {
            // IN-5: the same range the weight trend endpoint shows, so PAC-1 and PT20 agree.
            var view = await weightTrendQueryService.Handle(
                new GetWeightTrendRangeByPatientIdQuery(patientId, weeks), ct);
            if (view is null) return null;

            var summary = view.Range.Summary;
            return new WeightTrendSummaryItem(summary.SlopeKgPerWeek, summary.ChangeKg, summary.PointCount);
        }
        catch
        {
            return null;
        }
    }

    public async Task<WeightTrendSummaryItem?> GetWeightTrendSummaryBetween(int patientId, DateOnly from,
        DateOnly to, CancellationToken ct = default)
    {
        try
        {
            var view = await weightTrendQueryService.Handle(
                new GetWeightTrendBetweenByPatientIdQuery(patientId, from, to), ct);
            if (view is null) return null;

            var summary = view.Range.Summary;
            return new WeightTrendSummaryItem(summary.SlopeKgPerWeek, summary.ChangeKg, summary.PointCount);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Both halves of every entry are carried across whole. The food names are resolved through
    ///     the Food Catalog facade, and an entry whose food cannot be resolved keeps its identifier
    ///     and loses only the label: dropping the entry would quietly shorten a diary the
    ///     practitioner is reading as a record of what was said.
    /// </remarks>
    public async Task<IReadOnlyList<DiaryEntryItem>> GetDiaryEntries(int patientId, DateOnly date,
        CancellationToken ct = default)
    {
        try
        {
            var entries = (await diaryEntryQueryService.Handle(
                new GetDiaryEntriesByPatientIdQuery(patientId, date), ct)).ToList();

            var names = await diaryEntryQueryService.Handle(new GetFoodNamesQuery(entries
                .SelectMany(e => new[] { e.ProposedReferenceFoodId, e.ConfirmedReferenceFoodId })
                .OfType<int>()
                .ToList()), ct);

            var items = new List<DiaryEntryItem>(entries.Count);

            foreach (var entry in entries.OrderBy(e => e.LocalTimestamp))
            {
                var proposedName = NameOf(entry.ProposedReferenceFoodId, names);
                var confirmedName = NameOf(entry.ConfirmedReferenceFoodId, names);

                items.Add(new DiaryEntryItem(
                    entry.Id.Value,
                    entry.DeclaredLocalTimestamp,
                    entry.Provenance.Value,
                    entry.SyncState.Value,
                    entry.ProposedReferenceFoodId,
                    proposedName,
                    entry.ProposedPortionGrams,
                    entry.ProposedConfidence,
                    entry.ConfirmedReferenceFoodId,
                    confirmedName,
                    entry.ConfirmedPortionGrams,
                    entry.ConfirmedAt,
                    entry.PlanAdherence.Value,
                    // RM-3: as the diary resource of IN-1 shows them. Only a confirmed entry is intake.
                    entry.HasConfirmedEstimate,
                    confirmedName ?? proposedName));
            }

            return items;
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<DiaryEntryMomentItem>> GetDiaryEntryMoments(int patientId, DateOnly from,
        DateOnly to, CancellationToken ct = default)
    {
        try
        {
            if (to < from) return [];
            var entries = await diaryEntryQueryService.Handle(
                new GetDiaryEntriesByPatientIdBetweenQuery(patientId, from, to), ct);

            // Only when and whether it counted crosses: the food, the portion and the photo stay here.
            return entries
                .OrderBy(e => e.LocalTimestamp)
                .Select(e => new DiaryEntryMomentItem(e.LocalDate, e.DeclaredLocalTimestamp, e.HasConfirmedEstimate,
                    e.PlanAdherence.Value))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The nutrients are computed like the day's totals (catalog per 100 g × portion), with every food of the
    ///     diary read from the catalog in one batch. An entry the patient has not confirmed carries the proposal and
    ///     says so (<c>IsConfirmed</c> false), so the copy is the whole diary without passing a guess for intake.
    /// </remarks>
    public async Task<IReadOnlyList<DiaryExportEntryItem>> GetDiaryExport(int patientId,
        CancellationToken ct = default)
    {
        try
        {
            var entries = (await diaryEntryQueryService.Handle(
                new GetDiaryEntriesByPatientIdQuery(patientId, null), ct)).ToList();

            var foodIds = entries
                .Select(e => e.ConfirmedReferenceFoodId ?? e.ProposedReferenceFoodId)
                .OfType<int>()
                .Distinct()
                .ToList();
            var foods = foodIds.Count == 0
                ? new Dictionary<int, ReferenceFoodItem>()
                : (await foodCatalogContextFacade.GetReferenceFoodsByIds(foodIds, ct))
                .ToDictionary(f => f.ReferenceFoodId);

            return entries
                .OrderBy(e => e.LocalTimestamp)
                .Select(e =>
                {
                    var foodId = e.ConfirmedReferenceFoodId ?? e.ProposedReferenceFoodId;
                    var grams = e.HasConfirmedEstimate ? e.ConfirmedPortionGrams : e.ProposedPortionGrams;
                    var food = foodId is not null && foods.TryGetValue(foodId.Value, out var f) ? f : null;
                    decimal? factor = food is not null && grams is not null ? grams.Value / NutrientBasisGrams : null;

                    return new DiaryExportEntryItem(
                        e.DeclaredLocalTimestamp,
                        food?.LocalName,
                        grams,
                        Portion(food?.EnergyKcalPer100g, factor),
                        Portion(food?.ProteinGPer100g, factor),
                        Portion(food?.CarbGPer100g, factor),
                        Portion(food?.FatGPer100g, factor),
                        e.HasConfirmedEstimate,
                        e.PlanAdherence.Value,
                        e.Provenance.Value,
                        e.Origin?.Value);
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<SelfWeighInExportItem>> GetSelfWeighIns(int patientId,
        CancellationToken ct = default)
    {
        if (selfWeighInQueryService is null) return [];

        try
        {
            var protocol = protocolProvider?.Current ?? SelfWeighInProtocol.Default;
            var weighIns = await selfWeighInQueryService.Handle(new GetSelfWeighInsByPatientIdQuery(patientId), ct);

            return weighIns
                .OrderBy(w => w.LocalTimestamp)
                .Select(w => new SelfWeighInExportItem(w.DeclaredLocalTimestamp, w.ValueKg, w.ProtocolFastedState,
                    w.FollowsProtocolUnder(protocol)))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static decimal? Portion(decimal? per100g, decimal? factor)
    {
        return per100g is null || factor is null ? null : decimal.Round(per100g.Value * factor.Value, 2);
    }

    private static string? NameOf(int? referenceFoodId, IReadOnlyDictionary<int, string> names)
    {
        return referenceFoodId is not null && names.TryGetValue(referenceFoodId.Value, out var name)
            ? name
            : null;
    }

    private static List<WeightTrendPointItem> Tail(WeightTrend trend, int days)
    {
        var take = days <= 0 ? trend.Points.Count : Math.Min(days, trend.Points.Count);

        return trend.Points
            .Skip(trend.Points.Count - take)
            .Select(p => new WeightTrendPointItem(p.Date, p.SmoothedValueKg))
            .ToList();
    }
}
