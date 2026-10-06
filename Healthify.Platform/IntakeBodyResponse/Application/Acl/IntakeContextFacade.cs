using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
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
    IFoodCatalogContextFacade foodCatalogContextFacade) : IIntakeContextFacade
{
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
            var entries = await diaryEntryQueryService.Handle(new GetDiaryEntriesByPatientIdQuery(patientId, null),
                ct);

            // Only when and whether it counted crosses: the food, the portion and the photo stay here.
            return entries
                .Where(e => e.LocalDate >= from && e.LocalDate <= to)
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
