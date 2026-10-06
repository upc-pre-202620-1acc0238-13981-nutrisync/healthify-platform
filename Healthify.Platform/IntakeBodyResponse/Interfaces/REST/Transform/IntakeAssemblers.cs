using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;

// ---------------------------------------------------------------------------------------------
// Resource to Command
// ---------------------------------------------------------------------------------------------
// Every assembler below takes the patient identifier from the authenticated session rather than
// from the payload. A request that names somebody else is refused by the controller before it gets
// here, and even if it were not, the command could not be built for that other patient.

public static class LogMealByPhotoCommandAssembler
{
    public static LogMealByPhotoCommand ToCommand(int patientId, LogMealByPhotoResource resource)
    {
        var confirmation = resource.Confirmation is null
            ? null
            : new PhotoConfirmation(resource.Confirmation.Kind ?? string.Empty,
                resource.Confirmation.ReferenceFoodId, resource.Confirmation.PortionGrams);

        return new LogMealByPhotoCommand(patientId, resource.LocalTimestamp, resource.PhotoRef,
            resource.ReferenceFoodId, resource.PortionGrams, resource.Confidence, confirmation,
            resource.PlanAdherence, resource.AnalysisId, resource.ClientEntryId);
    }
}

public static class LogMealManuallyCommandAssembler
{
    public static LogMealManuallyCommand ToCommand(int patientId, LogMealManuallyResource resource)
    {
        return new LogMealManuallyCommand(patientId, resource.LocalTimestamp, resource.ReferenceFoodId,
            resource.PortionGrams, resource.PlanAdherence, resource.ClientEntryId);
    }
}

public static class LogOffPlanMealCommandAssembler
{
    public static LogOffPlanMealCommand ToCommand(int patientId, LogOffPlanMealResource resource)
    {
        return new LogOffPlanMealCommand(patientId, resource.LocalTimestamp);
    }
}

public static class ConfirmEstimateCommandAssembler
{
    public static ConfirmEstimateCommand ToCommand(int diaryEntryId, int patientId,
        EstimateConfirmationResource? resource)
    {
        return new ConfirmEstimateCommand(diaryEntryId, patientId, resource?.PlanAdherence);
    }
}

public static class AdjustEstimateCommandAssembler
{
    public static AdjustEstimateCommand ToCommand(int diaryEntryId, int patientId,
        AdjustEstimateResource resource)
    {
        return new AdjustEstimateCommand(diaryEntryId, patientId, resource.ReferenceFoodId,
            resource.PortionGrams, resource.PlanAdherence);
    }
}

public static class RecordSelfWeighInCommandAssembler
{
    public static RecordSelfWeighInCommand ToCommand(int patientId, RecordSelfWeighInResource resource)
    {
        return new RecordSelfWeighInCommand(patientId, resource.ValueKg, resource.LocalTimestamp,
            resource.FastedState, resource.SameTimeOfDay, resource.SameScale);
    }
}

public static class SyncSelfWeighInsCommandAssembler
{
    public static SyncSelfWeighInsCommand ToCommand(int patientId, SyncSelfWeighInsResource resource)
    {
        var entries = (resource.Entries ?? [])
            .Select(e => new PendingSelfWeighIn(e.ClientEntryId, e.ValueKg, e.LocalTimestamp, e.FastedState,
                e.SameTimeOfDay, e.SameScale))
            .ToList();

        return new SyncSelfWeighInsCommand(patientId, entries);
    }
}

public static class SyncPendingEntriesCommandAssembler
{
    public static SyncPendingEntriesCommand ToCommand(int patientId, SyncPendingEntriesResource resource)
    {
        var entries = (resource.Entries ?? [])
            .Select(e => new PendingDiaryEntry(e.ClientEntryId, e.LocalTimestamp, e.Provenance,
                e.PhotoRef, e.ReferenceFoodId, e.PortionGrams, e.Confidence, e.Confirmed, e.PlanAdherence))
            .ToList();

        return new SyncPendingEntriesCommand(patientId, entries);
    }
}

// ---------------------------------------------------------------------------------------------
// Aggregate to Resource
// ---------------------------------------------------------------------------------------------

public static class ActiveTargetsResourceAssembler
{
    public static ActiveTargetsResource ToResource(ActiveTargetsCache cache)
    {
        return new ActiveTargetsResource(cache.PatientId, cache.PlanVersion, cache.ValidFrom,
            cache.EnergyKcal, cache.ProteinG, cache.CarbG, cache.FatG, cache.Guidelines,
            cache.Restrictions, cache.RefreshedAt,
            cache.GuidelineItems.Select(g => new ActiveGuidelineResource(g.Code, g.Custom)).ToList(),
            cache.LegacyRestrictions,
            cache.ChangesFromPrevious?.Select(c =>
                new ActivePlanChangeResource(c.Type, c.Code, c.Custom, c.Macro, c.From, c.To)).ToList(),
            cache.PatientMessage);
    }
}

public static class DiaryEntryResourceAssembler
{
    /// <summary>
    ///     Business rule: Confidence And Provenance Always Exposed (Subflow 4.2). Both travel on
    ///     every entry this assembler produces, and there is no overload that omits them.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="foodName">Local name of the food shown, resolved through the query service.</param>
    public static DiaryEntryResource ToResource(DiaryEntry entry, string? foodName = null)
    {
        return new DiaryEntryResource(
            entry.Id.Value,
            entry.PatientId,
            entry.DeclaredLocalTimestamp,
            entry.LocalDate,
            entry.Provenance.Value,
            entry.PhotoRef,
            entry.ProposedReferenceFoodId,
            entry.ProposedPortionGrams,
            entry.ProposedConfidence,
            entry.ProposedEstimatedAt,
            entry.ConfirmedReferenceFoodId,
            entry.ConfirmedPortionGrams,
            entry.ConfirmedAt,
            entry.SyncState.Value,
            entry.PlanAdherence.Value,
            entry.HasConfirmedEstimate,
            foodName,
            entry.MealGroupId,
            entry.Origin?.Value);
    }

    /// <summary>The food an entry shows: the confirmed one, or the proposed one while it waits.</summary>
    public static int? DisplayedFoodId(DiaryEntry entry)
    {
        return entry.ConfirmedReferenceFoodId ?? entry.ProposedReferenceFoodId;
    }

    /// <summary>Every entry with the name of the food it shows, from names resolved in one query.</summary>
    public static IEnumerable<DiaryEntryResource> ToResources(IEnumerable<DiaryEntry> entries,
        IReadOnlyDictionary<int, string> foodNames)
    {
        return entries.Select(e => ToResource(e,
            DisplayedFoodId(e) is { } id && foodNames.TryGetValue(id, out var name) ? name : null));
    }
}

public static class SelfWeighInResourceAssembler
{
    /// <param name="weighIn">The reading.</param>
    /// <param name="protocol">IN-3. The protocol in force; null means the default (fasted only).</param>
    public static SelfWeighInResource ToResource(SelfWeighIn weighIn, SelfWeighInProtocol? protocol = null)
    {
        return new SelfWeighInResource(
            weighIn.Id.Value,
            weighIn.PatientId,
            weighIn.ValueKg,
            weighIn.DeclaredLocalTimestamp,
            weighIn.ProtocolFastedState,
            weighIn.ProtocolSameTimeOfDay,
            weighIn.ProtocolSameScale,
            weighIn.FollowsProtocolUnder(protocol ?? SelfWeighInProtocol.Default));
    }
}

public static class WeightTrendResourceAssembler
{
    public static WeightTrendResource ToResource(WeightTrend trend)
    {
        return new WeightTrendResource(
            trend.PatientId,
            trend.WindowSize,
            trend.LastRecalculatedAt,
            trend.Points.Select(p => new WeightTrendPointResource(p.Date, p.SmoothedValueKg)).ToList());
    }

    /// <summary>IN-5. The series with the summary of its last weeks.</summary>
    public static WeightTrendResource ToResource(WeightTrendView view)
    {
        var range = view.Range;
        return ToResource(view.Trend) with
        {
            ExcludedReadingsCount = range.ExcludedReadingsCount,
            ChangeKgOverRange = range.Summary.ChangeKg,
            SlopeKgPerWeek = range.Summary.SlopeKgPerWeek,
            RangeFrom = range.From,
            RangeTo = range.To
        };
    }
}

public static class SelfWeighInSyncOutcomeResourceAssembler
{
    public static SelfWeighInSyncOutcomeResource ToResource(SelfWeighInSyncOutcome outcome)
    {
        return new SelfWeighInSyncOutcomeResource(
            outcome.PatientId,
            outcome.Created,
            outcome.AlreadyPresent,
            outcome.Rejected,
            outcome.Entries
                .Select(e => new SyncedSelfWeighInOutcomeResource(e.ClientEntryId, e.SelfWeighInId, e.Outcome,
                    e.Reason))
                .ToList());
    }
}

public static class SyncOutcomeResourceAssembler
{
    public static SyncOutcomeResource ToResource(SyncOutcome outcome)
    {
        return new SyncOutcomeResource(
            outcome.PatientId,
            outcome.Created,
            outcome.AlreadyPresent,
            outcome.ConflictsResolved,
            outcome.Rejected,
            outcome.Entries
                .Select(e => new SyncedEntryOutcomeResource(e.ClientEntryId, e.DiaryEntryId, e.Outcome,
                    e.Reason))
                .ToList());
    }
}

/// <summary>IN-7.</summary>
public static class MealPhotoAnalysisResourceAssembler
{
    public static MealPhotoAnalysisResource ToResource(MealPhotoAnalysisView view)
    {
        return new MealPhotoAnalysisResource(view.AnalysisId, view.ReferenceFoodId, view.FoodName,
            view.EstimatedGrams, view.Confidence,
            view.Alternatives.Select(a => new MealPhotoAlternativeResource(a.Name, a.Grams, a.ReferenceFoodId))
                .ToList(),
            view.ExpiresAt);
    }
}

/// <summary>IA-3.</summary>
public static class MealIdeasResourceAssembler
{
    public static GenerateMealIdeasCommand ToCommand(int patientId, GenerateMealIdeasResource? resource)
    {
        return new GenerateMealIdeasCommand(patientId, resource?.LocalDate, resource?.ExcludeIdeaIds);
    }

    public static MealIdeasResource ToResource(MealIdeasView view, string disclaimer)
    {
        return new MealIdeasResource(view.LocalDate, view.Remaining.EnergyKcal, view.Remaining.ProteinG,
            view.Remaining.CarbG, view.Remaining.FatG, view.Restrictions,
            view.Ideas.Select(i => new MealIdeaResource(i.MealIdeaId, i.Name, i.EnergyKcal, i.ProteinG, i.CarbG,
                i.FatG,
                i.Ingredients.Select(g => new MealIdeaIngredientResource(g.Name, g.Grams, g.ReferenceFoodId,
                    g.CatalogName, g.IsResolved)).ToList(),
                i.Why, i.NutrientsFromCatalog)).ToList(),
            view.AiGenerationId, view.GeneratedAt, disclaimer);
    }
}

/// <summary>IN-6.</summary>
public static class LogMealGroupCommandAssembler
{
    public static LogMealGroupManuallyCommand ToCommand(int patientId, LogMealGroupResource resource)
    {
        return new LogMealGroupManuallyCommand(patientId, resource.LocalTimestamp, resource.PlanAdherence,
            resource.Origin is null ? null : new MealGroupOrigin(resource.Origin.Kind, resource.Origin.MealIdeaId),
            (resource.Items ?? []).Select(i => new MealGroupItem(i.ReferenceFoodId, i.PortionGrams, i.ClientEntryId))
            .ToList());
    }

    public static MealGroupLogResource ToResource(MealGroupLogOutcome outcome,
        IReadOnlyDictionary<int, string> foodNames)
    {
        return new MealGroupLogResource(outcome.MealGroupId, outcome.Entries.FirstOrDefault()?.Origin?.Value,
            DiaryEntryResourceAssembler.ToResources(outcome.Entries, foodNames).ToList());
    }
}
