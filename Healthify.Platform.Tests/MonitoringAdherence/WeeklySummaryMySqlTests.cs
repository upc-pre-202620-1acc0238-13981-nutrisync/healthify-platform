using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     IA-2/IA-5 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): the weekly summary
///     round-trips its dates, its texts and its facts (json), the latest one is the most recent week, the unique
///     index keeps one per patient and week, a purge removes them; and the review item existence of IA-5 is
///     translated to SQL.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class WeeklySummaryMySqlTests
{
    private static async Task<MonitoringPeriodFacts> WeekOf(DateOnly monday)
    {
        var scenario = new MonitoringAiScenario();
        scenario.LogDays(monday, ["Met", "Met", "Met", "Short", "Met", "Met", "Unlogged"]);
        return await scenario.Facts.ReadAsync(MonitoringAiScenario.PatientId, monday, monday.AddDays(6), true);
    }

    [MySqlFact]
    public async Task A_weekly_summary_round_trips_and_stays_one_per_patient_and_week()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var monday = MonitoringAiScenario.WeekStart;
        var generatedAt = new DateTimeOffset(2026, 9, 14, 11, 0, 0, TimeSpan.Zero);

        await using (var context = db.NewContext())
        {
            var repository = new WeeklySummaryRepository(context);
            await repository.AddAsync(new WeeklySummary(10, await WeekOf(monday.AddDays(-7)), "Semana anterior",
                ["Bien"], [], 1, "es", generatedAt.AddDays(-7)));
            await repository.AddAsync(new WeeklySummary(10, await WeekOf(monday), "Cumpliste tus metas 5 de 7 días.",
                ["Registraste 6 de 7 días.", "Tu tendencia bajó 0,3 kg."], ["El jueves, un almuerzo más."], 42, "es",
                generatedAt));
            await new UnitOfWork(context).CompleteAsync();
        }

        await using (var context = db.NewContext())
        {
            var repository = new WeeklySummaryRepository(context);
            var latest = await repository.FindLatestByPatientIdAsync(10);
            Assert.NotNull(latest);
            Assert.Equal((monday, monday.AddDays(6), 42L), (latest.WeekStart, latest.WeekEnd, latest.AiGenerationId));
            Assert.Equal(["Registraste 6 de 7 días.", "Tu tendencia bajó 0,3 kg."], latest.WentWell);
            Assert.Equal(["El jueves, un almuerzo más."], latest.WatchOut);
            Assert.Equal((5, 1, 6, 7, -0.3m), (latest.ComplianceFacts.MetDays, latest.ComplianceFacts.ShortDays,
                latest.ComplianceFacts.LoggedDays, latest.ComplianceFacts.TotalDays,
                latest.ComplianceFacts.WeightChangeKg));
            Assert.Equal(7, latest.ComplianceFacts.Days.Count);
            Assert.NotNull(await repository.FindByPatientIdAndWeekStartAsync(10, monday.AddDays(-7)));
            Assert.Single(await repository.ListGeneratedBeforeAsync(generatedAt.AddDays(-1), 10));

            // Business rule: One Summary Per Patient And Week (IA-2).
            context.Add(new WeeklySummary(10, await WeekOf(monday), "Otra", ["Bien"], [], 43, "es", generatedAt));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = db.NewContext())
        {
            var repository = new WeeklySummaryRepository(context);
            foreach (var summary in await repository.ListByPatientIdAsync(10)) repository.Remove(summary);
            await new UnitOfWork(context).CompleteAsync();
            Assert.Null(await repository.FindLatestByPatientIdAsync(10));
        }
    }

    [MySqlFact]
    public async Task A_resolved_consistency_escalation_still_counts_as_existing()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await using (var context = db.NewContext())
        {
            var item = new ReviewItem(new OpenReviewItemCommand(10, SignalType.ConsistencyEscalation, "{}"), 20);
            item.Resolve(false, "Conversado en consulta");
            context.Add(item);
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var repository = new ReviewItemRepository(context);
            Assert.True(await repository.ExistsForPatientAndSignalTypeAsync(10,
                new SignalType(SignalType.ConsistencyEscalation)));
            Assert.False(await repository.ExistsForPatientAndSignalTypeAsync(10,
                new SignalType(SignalType.SustainedDeviation)));
            Assert.False(await repository.ExistsForPatientAndSignalTypeAsync(11,
                new SignalType(SignalType.ConsistencyEscalation)));
        }
    }
}
